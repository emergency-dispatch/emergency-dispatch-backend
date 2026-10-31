using System.Diagnostics;
using System.Text;
using System.Text.Json;
using EmergencyDispatch.Application.DTOs.Ai;
using EmergencyDispatch.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Infrastructure.Services;

/// <summary>
/// Client gọi YOLO FastAPI microservice để phân loại ảnh sự cố nhanh (< 500ms).
/// Hai model chạy song song trên microservice: accident-fire và traffic-jam.
/// </summary>
public class YoloClassificationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<YoloClassificationService> _logger;

    public YoloClassificationService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<YoloClassificationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Gọi YOLO microservice để classify ảnh. Trả null nếu YOLO không khả dụng.
    /// </summary>
    public async Task<AiClassificationResultDto?> ClassifyAsync(
        string mediaUrl,
        CancellationToken cancellationToken = default)
    {
        var enabled = _configuration.GetValue<bool>("YoloService:Enabled");
        var serviceUrl = _configuration["YoloService:BaseUrl"] ?? "http://127.0.0.1:8001";
        var endpoint = $"{serviceUrl.TrimEnd('/')}/api/yolo/classify";

        if (!enabled)
        {
            _logger.LogDebug("YOLO Service is disabled in configuration. Skipping.");
            return null;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient("YoloClient");
            client.Timeout = TimeSpan.FromSeconds(
                _configuration.GetValue("YoloService:TimeoutSeconds", 30));

            var requestBody = JsonSerializer.Serialize(new { image_url = mediaUrl });
            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(endpoint, content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "YOLO Service trả về {StatusCode}: {Body}",
                    (int)response.StatusCode,
                    responseBody.Length > 300 ? responseBody[..300] : responseBody);
                return null;
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            // ── Kiểm tra lỗi phía YOLO (cả hai model đều không hoạt động) ──
            var isError = root.TryGetProperty("is_error", out var errProp) && errProp.GetBoolean();
            var errorMessage = root.TryGetProperty("error_message", out var errMsgProp)
                ? errMsgProp.GetString() : null;

            if (isError)
            {
                stopwatch.Stop();
                _logger.LogWarning(
                    "YOLO Service trả về is_error=true: {Error}. Chuyển sang Qwen-VL.",
                    errorMessage ?? "unknown error");
                return null;
            }

            var isAccidentFireEvaluated = root.TryGetProperty("is_accident_fire_evaluated", out var afEvalProp) && afEvalProp.GetBoolean();
            var incidentType = root.TryGetProperty("incident_type", out var itProp)
                ? itProp.GetString() ?? "unverified" : "unverified";

            // Lấy confidence của sự cố (chỉ có khi model accident-fire được đánh giá thành công)
            double? incidentConfidence = null;
            if (root.TryGetProperty("incident_confidence", out var confProp) && confProp.ValueKind == JsonValueKind.Number)
            {
                incidentConfidence = confProp.GetDouble();
            }

            var rawJamStatus = root.TryGetProperty("traffic_jam_status", out var tjsProp)
                ? tjsProp.GetString()?.ToLowerInvariant().Trim() : null;
            var trafficJamEvidence = root.TryGetProperty("traffic_jam_evidence", out var tjeProp)
                ? tjeProp.GetString() : null;

            TrafficJamStatus trafficJamStatus = rawJamStatus switch
            {
                "suspected" => TrafficJamStatus.Suspected,
                "no_evidence" => TrafficJamStatus.NoEvidence,
                "unverified" => TrafficJamStatus.Unverified,
                _ => (root.TryGetProperty("is_traffic_jam", out var jp) && jp.GetBoolean())
                    ? TrafficJamStatus.Suspected
                    : TrafficJamStatus.NoEvidence
            };

            // AI chỉ dừng ở mức Suspected, không tự tiện gán IsTrafficJam=true
            var isJam = false;
            var needsHumanReview = root.TryGetProperty("needs_human_review", out var revProp) && revProp.GetBoolean();
            if (trafficJamStatus == TrafficJamStatus.Suspected)
            {
                needsHumanReview = true;
            }

            var summary = root.TryGetProperty("summary", out var sumProp) ? sumProp.GetString() : "";

            var hazardTags = new List<string>();
            if (root.TryGetProperty("hazard_tags", out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var tag in tagsProp.EnumerateArray())
                {
                    var t = tag.GetString();
                    if (!string.IsNullOrWhiteSpace(t))
                        hazardTags.Add(t);
                }
            }

            if (!isAccidentFireEvaluated)
            {
                if (!hazardTags.Contains("YoloAccidentFireUnverified"))
                    hazardTags.Add("YoloAccidentFireUnverified");
                if (!hazardTags.Contains("NeedsHumanReview"))
                    hazardTags.Add("NeedsHumanReview");
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "YOLO inference: EvaluatedAF={AfOk}, Type={Type}, JamStatus={JamStatus}, Conf={Conf}, NeedsReview={Review} ({Ms}ms)",
                isAccidentFireEvaluated, incidentType, trafficJamStatus, incidentConfidence?.ToString("P1") ?? "N/A", needsHumanReview, stopwatch.ElapsedMilliseconds);

            return new AiClassificationResultDto
            {
                IncidentType = incidentType,
                IsTrafficJam = isJam,
                TrafficJamStatus = trafficJamStatus,
                TrafficJamEvidence = trafficJamEvidence,
                SeverityScore = 0, // YOLO luôn là 0 (Unclassified), thẩm định mức độ giao cho Qwen-VL hoặc Operator
                ConfidenceScore = incidentConfidence,
                HazardTags = hazardTags,
                Summary = summary,
                IsSuccess = true,
                ModelName = "YOLO11n (Dual: accident-fire + jam)",
                RawResponse = responseBody,
                ProcessingDurationMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "YOLO Service ({Url}) không khả dụng: {Reason}. Tiếp tục với Qwen-VL.",
                endpoint,
                ex is TaskCanceledException ? "quá thời gian chờ" : ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Kiểm tra YOLO microservice có đang hoạt động hay không
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        var serviceUrl = _configuration["YoloService:BaseUrl"] ?? "http://127.0.0.1:8001";
        try
        {
            var client = _httpClientFactory.CreateClient("YoloClient");
            client.Timeout = TimeSpan.FromSeconds(5);
            var response = await client.GetAsync($"{serviceUrl.TrimEnd('/')}/api/yolo/health", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return false;

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            var status = doc.RootElement.TryGetProperty("status", out var stProp)
                ? stProp.GetString()?.ToLowerInvariant()
                : null;

            // Khả dụng nếu status là "healthy" hoặc "degraded" (ít nhất 1 model sẵn sàng hoạt động)
            // Trả false nếu status là "unhealthy" (cả hai model đều không hoạt động)
            return status is "healthy" or "degraded";
        }
        catch
        {
            return false;
        }
    }
}
