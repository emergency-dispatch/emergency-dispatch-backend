using System.Text;
using System.Text.Json;
using EmergencyDispatch.Application.DTOs.Ai;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Infrastructure.Services;

/// <summary>
/// Chế độ CnnTriage:Engine = Http: gọi YoloService POST /api/yolo/triage7 (phiếu 7 mức của ResNet-50 và YOLO11n-cls,
/// độ tin cậy = xác suất softmax). Mặc định (Engine = Onnx) backend tự chạy 2 model, xem OnnxCnnTriageService.
/// Địa chỉ: CnnTriage:BaseUrl, mặc định dùng YoloService:BaseUrl. Không phụ thuộc cờ YoloService:Enabled (cờ đó chỉ bật Dual-Engine)
/// </summary>
public class CnnTriageService : ICnnTriageService
{
    public const string HttpClientName = "CnnTriageClient";
    public static readonly string[] ModelNames = { "ResNet-50 (7 levels)", "YOLO11n-cls (7 levels)" };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CnnTriageService> _logger;

    public CnnTriageService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<CnnTriageService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AiModelVoteDto>> ClassifyAsync(string mediaUrl, CancellationToken cancellationToken = default)
    {
        var baseUrl = _configuration["CnnTriage:BaseUrl"] ?? _configuration["YoloService:BaseUrl"] ?? "http://127.0.0.1:8001";
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            client.Timeout = TimeSpan.FromSeconds(_configuration.GetValue("CnnTriage:TimeoutSeconds", 30));
            var content = new StringContent(JsonSerializer.Serialize(new { image_url = mediaUrl }), Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{baseUrl.TrimEnd('/')}/api/yolo/triage7", content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("CNN triage trả về {StatusCode}: {Body}", (int)response.StatusCode, body.Length > 300 ? body[..300] : body);
                return Unavailable($"HTTP {(int)response.StatusCode}");
            }
            return Parse(body);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Không gọi được CNN triage tại {BaseUrl}", baseUrl);
            return Unavailable(ex.Message);
        }
    }

    /// <summary>
    /// Đọc phản hồi của /api/yolo/triage7 (tách riêng để test). Nhãn ngoài 7 lớp → phiếu không hợp lệ
    /// </summary>
    public static IReadOnlyList<AiModelVoteDto> Parse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var votes = new List<AiModelVoteDto>();
        if (doc.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in models.EnumerateArray())
            {
                var name = m.TryGetProperty("model_name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : "CNN";
                var available = m.TryGetProperty("is_available", out var a) && a.ValueKind == JsonValueKind.True;
                var label = m.TryGetProperty("predicted_class", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                double? confidence = m.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : null;
                var error = m.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

                var vote = new AiModelVoteDto { Model = name, Confidence = confidence, ConfidenceSource = "softmax", Error = error };
                if (available && TryParseLabel(label, out var type, out var severity) && confidence is >= 0 and <= 1)
                {
                    vote.IsAvailable = true;
                    vote.IncidentType = type;
                    vote.Severity = severity;
                }
                else if (available)
                {
                    vote.Error ??= $"Nhãn / độ tin cậy không hợp lệ: {label}";
                }
                votes.Add(vote);
            }
        }
        return votes.Count > 0 ? votes : Unavailable("Phản hồi không có danh sách models");
    }

    /// <summary>fire_L2 → (fire, 2); accident_L3 → (accident, 3); none → (none, 0)</summary>
    public static bool TryParseLabel(string? label, out string type, out int severity)
    {
        type = "none";
        severity = 0;
        if (label == "none") return true;
        var parts = label?.Split("_L") ?? Array.Empty<string>();
        if (parts.Length != 2 || parts[0] is not ("fire" or "accident") || !int.TryParse(parts[1], out severity) || severity is < 1 or > 3)
        {
            severity = 0;
            return false;
        }
        type = parts[0];
        return true;
    }

    internal static IReadOnlyList<AiModelVoteDto> Unavailable(string error)
        => ModelNames.Select(n => new AiModelVoteDto { Model = n, IsAvailable = false, Error = error }).ToList();
}
