using System.Diagnostics;
using System.Text.RegularExpressions;
using EmergencyDispatch.Application.DTOs.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;

namespace EmergencyDispatch.Infrastructure.Services.CnnTriage;

/// <summary>
/// Giữ 2 phiên ONNX Runtime (ResNet-50 + YOLO11n-cls 7 mức), đăng ký singleton. Nạp lười ở lần gọi đầu, chạy tuần tự
/// từng ảnh một (giới hạn RAM trên Render free). Model thiếu / lỗi → phiếu của model đó is_available = false
/// Cấu hình: CnnTriage:ResNetModelPath, CnnTriage:YoloModelPath (tương đối theo thư mục chạy),
/// CnnTriage:Threads (mặc định 1), CnnTriage:KeepModelsLoaded (mặc định true; false = giải phóng sau mỗi lần chạy)
/// </summary>
public sealed class OnnxTriageModels : IDisposable
{
    public const string DefaultResNetPath = "AiModels/triage_resnet50.onnx";
    public const string DefaultYoloPath = "AiModels/triage_yolo11n_cls7.onnx";

    /// <summary>Thứ tự lớp đầu ra của ResNet-50 (giống YoloService/triage_models.py lúc train)</summary>
    public static readonly string[] ResNetLabels = { "fire_L1", "fire_L2", "fire_L3", "accident_L1", "accident_L2", "accident_L3", "none" };

    private readonly IConfiguration _configuration;
    private readonly ILogger<OnnxTriageModels> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private InferenceSession? _resnet;
    private InferenceSession? _yolo;
    private string[]? _yoloLabels;
    private string? _resnetError;
    private string? _yoloError;
    private bool _loaded;

    public OnnxTriageModels(IConfiguration configuration, ILogger<OnnxTriageModels> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AiModelVoteDto>> ClassifyAsync(DecodedImage image, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
                EnsureLoaded();
                var votes = new List<AiModelVoteDto>
                {
                    RunResNet(image),
                    RunYolo(image)
                };
                if (!_configuration.GetValue("CnnTriage:KeepModelsLoaded", true)) Unload();
                GlibcMemory.Trim();
                return (IReadOnlyList<AiModelVoteDto>)votes;
            }, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        GlibcMemory.ConfigureForLargeBuffers();
        var threads = Math.Max(1, _configuration.GetValue("CnnTriage:Threads", 1));
        (_resnet, _resnetError) = Load(_configuration["CnnTriage:ResNetModelPath"] ?? DefaultResNetPath, threads);
        (_yolo, _yoloError) = Load(_configuration["CnnTriage:YoloModelPath"] ?? DefaultYoloPath, threads);
        if (_yolo != null)
        {
            _yoloLabels = ParseYoloNames(_yolo.ModelMetadata.CustomMetadataMap.GetValueOrDefault("names"));
            if (_yoloLabels == null || !_yoloLabels.OrderBy(x => x).SequenceEqual(ResNetLabels.OrderBy(x => x)))
            {
                _yoloError = "YOLO model metadata 'names' is not the 7 triage classes";
                _logger.LogError("Model YOLO11n-cls không có đúng 7 lớp (metadata names = {Names})", _yolo.ModelMetadata.CustomMetadataMap.GetValueOrDefault("names"));
                _yolo.Dispose();
                _yolo = null;
            }
        }
        _loaded = true;
    }

    private (InferenceSession? Session, string? Error) Load(string path, int threads)
    {
        var full = Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
        if (!File.Exists(full))
        {
            _logger.LogWarning("Không thấy file model CNN {Path}", full);
            return (null, $"Model file not found: {Path.GetFileName(full)}");
        }
        try
        {
            // Giữ RAM thấp (Render free 512 MB, backend đã dùng ~160 MB): tắt memory arena / memory pattern / prepacking
            // và không dùng mức tối ưu ALL (đổi layout NCHWc tạo thêm bản sao trọng số). Đo trên Linux: ResNet-50
            // thường trực ~+110 MB, đỉnh ~+170 MB; mức ALL nhanh hơn ~20% nhưng đỉnh ~+215 MB
            var options = new SessionOptions
            {
                IntraOpNumThreads = threads,
                InterOpNumThreads = 1,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_EXTENDED,
                EnableCpuMemArena = false,
                EnableMemoryPattern = false
            };
            options.AddSessionConfigEntry("session.disable_prepacking", "1");
            var session = new InferenceSession(full, options);
            _logger.LogInformation("Đã nạp model CNN {Path}", full);
            return (session, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không nạp được model CNN {Path}", full);
            return (null, $"Model load failed: {ex.Message}");
        }
    }

    private AiModelVoteDto RunResNet(DecodedImage image)
    {
        if (_resnet == null) return Vote(CnnTriageService.ModelNames[0], null, _resnetError ?? "Model not loaded");
        try
        {
            var sw = Stopwatch.StartNew();
            var logits = Run(_resnet, CnnImagePreprocessor.ResNetTensor(image), CnnImagePreprocessor.ResNetSize);
            var probs = Softmax(logits);
            _logger.LogDebug("ResNet-50 7 mức: {Ms} ms", sw.ElapsedMilliseconds);
            return Vote(CnnTriageService.ModelNames[0], Best(ResNetLabels, probs), null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi chạy ResNet-50 7 mức");
            return Vote(CnnTriageService.ModelNames[0], null, ex.Message);
        }
    }

    private AiModelVoteDto RunYolo(DecodedImage image)
    {
        if (_yolo == null || _yoloLabels == null) return Vote(CnnTriageService.ModelNames[1], null, _yoloError ?? "Model not loaded");
        try
        {
            var output = Run(_yolo, CnnImagePreprocessor.YoloTensor(image), CnnImagePreprocessor.YoloSize);
            // Đầu ra ultralytics classify đã là softmax; phòng khi xuất lại không có softmax
            var sum = output.Sum();
            var probs = output.All(p => p >= 0) && Math.Abs(sum - 1) < 1e-3 ? output : Softmax(output);
            return Vote(CnnTriageService.ModelNames[1], Best(_yoloLabels, probs), null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lỗi chạy YOLO11n-cls 7 mức");
            return Vote(CnnTriageService.ModelNames[1], null, ex.Message);
        }
    }

    private static float[] Run(InferenceSession session, float[] tensor, int size)
    {
        using var input = OrtValue.CreateTensorValueFromMemory(tensor, new long[] { 1, 3, size, size });
        using var runOptions = new RunOptions();
        var inputName = session.InputNames[0];
        var outputName = session.OutputNames[0];
        using var results = session.Run(runOptions, new[] { inputName }, new[] { input }, new[] { outputName });
        return results[0].GetTensorDataAsSpan<float>().ToArray();
    }

    /// <summary>"{0: 'accident_L1', 1: 'accident_L2', ...}" (metadata ultralytics) → mảng nhãn theo chỉ số</summary>
    public static string[]? ParseYoloNames(string? names)
    {
        if (string.IsNullOrWhiteSpace(names)) return null;
        var map = Regex.Matches(names, @"(\d+)\s*:\s*'([^']*)'")
            .ToDictionary(m => int.Parse(m.Groups[1].Value), m => m.Groups[2].Value);
        if (map.Count == 0 || !Enumerable.Range(0, map.Count).All(map.ContainsKey)) return null;
        return Enumerable.Range(0, map.Count).Select(i => map[i]).ToArray();
    }

    public static float[] Softmax(float[] logits)
    {
        var max = logits.Max();
        var exp = logits.Select(v => Math.Exp(v - max)).ToArray();
        var total = exp.Sum();
        return exp.Select(v => (float)(v / total)).ToArray();
    }

    private static (string Label, double Confidence) Best(string[] labels, float[] probs)
    {
        if (probs.Length != labels.Length) throw new InvalidOperationException($"Model trả {probs.Length} lớp, cần {labels.Length}");
        var best = 0;
        for (var i = 1; i < probs.Length; i++)
            if (probs[i] > probs[best]) best = i;
        return (labels[best], probs[best]);
    }

    private static AiModelVoteDto Vote(string model, (string Label, double Confidence)? prediction, string? error)
    {
        var vote = new AiModelVoteDto { Model = model, ConfidenceSource = "softmax", Error = error };
        if (prediction is { } p && CnnTriageService.TryParseLabel(p.Label, out var type, out var severity))
        {
            vote.IsAvailable = true;
            vote.IncidentType = type;
            vote.Severity = severity;
            vote.Confidence = Math.Round(p.Confidence, 6);
        }
        return vote;
    }

    private void Unload()
    {
        _resnet?.Dispose();
        _yolo?.Dispose();
        _resnet = _yolo = null;
        _yoloLabels = null;
        _loaded = false;
    }

    public void Dispose()
    {
        Unload();
        _gate.Dispose();
    }
}
