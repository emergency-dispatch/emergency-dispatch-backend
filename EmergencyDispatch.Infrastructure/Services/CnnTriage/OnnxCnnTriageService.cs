using EmergencyDispatch.Application.DTOs.Ai;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Validators;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Infrastructure.Services.CnnTriage;

/// <summary>
/// Chế độ CnnTriage:Engine = Onnx (mặc định): backend tự tải ảnh sự cố và chạy ResNet-50 + YOLO11n-cls bằng ONNX Runtime,
/// không cần YoloService / PyTorch (chạy được trên Render free). Lỗi tải / đọc ảnh → cả 2 phiếu is_available = false
/// </summary>
public class OnnxCnnTriageService : ICnnTriageService
{
    public const string DownloadClientName = "CnnTriageDownload";
    public const long MaxImageBytes = 15 * 1024 * 1024;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly OnnxTriageModels _models;
    private readonly ILogger<OnnxCnnTriageService> _logger;

    public OnnxCnnTriageService(IHttpClientFactory httpClientFactory, IConfiguration configuration,
        OnnxTriageModels models, ILogger<OnnxCnnTriageService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _models = models;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AiModelVoteDto>> ClassifyAsync(string mediaUrl, CancellationToken cancellationToken = default)
    {
        // URL đã được kiểm khi tạo sự cố; kiểm lại vì backend tự tải (tránh dò mạng nội bộ)
        if (!MediaUrlPolicy.IsTrusted(mediaUrl, _configuration))
            return CnnTriageService.Unavailable("Media URL is not a system-uploaded image");

        byte[] bytes;
        try
        {
            bytes = await DownloadAsync(mediaUrl, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Không tải được ảnh cho 2 model CNN");
            return CnnTriageService.Unavailable($"Image download failed: {ex.Message}");
        }

        DecodedImage image;
        try
        {
            image = CnnImagePreprocessor.Decode(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không đọc được ảnh cho 2 model CNN");
            return CnnTriageService.Unavailable($"Image decode failed: {ex.Message}");
        }
        return await _models.ClassifyAsync(image, cancellationToken);
    }

    private async Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(DownloadClientName);
        client.Timeout = TimeSpan.FromSeconds(_configuration.GetValue("CnnTriage:DownloadTimeoutSeconds", 30));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("image/jpeg, image/png, image/webp;q=0.9, image/*;q=0.5");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxImageBytes)
            throw new InvalidDataException($"Image larger than {MaxImageBytes / (1024 * 1024)} MB");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxImageBytes)
                throw new InvalidDataException($"Image larger than {MaxImageBytes / (1024 * 1024)} MB");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
