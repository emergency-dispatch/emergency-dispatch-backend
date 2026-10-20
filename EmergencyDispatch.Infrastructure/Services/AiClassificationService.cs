using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EmergencyDispatch.Application.DTOs.Ai;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Infrastructure.Services;

public class AiClassificationService : IAiClassificationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiClassificationService> _logger;

    public AiClassificationService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<AiClassificationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AiClassificationResultDto> AnalyzeAsync(
        string mediaUrl,
        string? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // Ưu tiên 1: Gọi mô hình cục bộ đã fine-tune (Qwen2.5-VL LoRA Adapter trên GPU máy)
        var localResult = await TryAnalyzeLocalAsync(mediaUrl, additionalContext, stopwatch, cancellationToken);
        if (localResult != null)
        {
            return localResult;
        }

        var apiKey = _configuration["OpenRouter:ApiKey"] ?? string.Empty;
        var baseUrl = _configuration["OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1";
        var modelName = _configuration["OpenRouter:Model"] ?? "qwen/qwen3-vl-32b-instruct";
        var siteUrl = _configuration["OpenRouter:SiteUrl"] ?? "https://emergencydispatch.local";
        var siteName = _configuration["OpenRouter:SiteName"] ?? "Emergency Dispatch System";

        // Trường hợp chưa cấu hình API Key: Cung cấp chế độ Mock phân loại thông minh để test luồng
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.StartsWith("sk-or-v1-YOUR_"))
        {
            _logger.LogWarning("OpenRouter API Key chưa được cấu hình. Sử dụng mô phỏng phản hồi AI thông minh để kiểm thử luồng.");
            await Task.Delay(300, cancellationToken); // Giả lập độ trễ AI
            stopwatch.Stop();

            return new AiClassificationResultDto
            {
                HazardTags = new List<string> { "fire", "heavy_smoke", "residential_area" },
                SeverityScore = 3, // Level 3
                Summary = "[Mô phỏng AI Qwen2.5-VL] Phát hiện đám cháy bùng phát tại khu vực nhà ở kèm khói đen đặc, nguy cơ lan rộng cao.",
                ConfidenceScore = 0.92,
                IsSuccess = true,
                ModelName = $"{modelName} (Simulated)",
                RawResponse = "{\"hazardTags\":[\"fire\",\"heavy_smoke\",\"residential_area\"],\"severityScore\":3,\"summary\":\"Phát hiện đám cháy bùng phát tại khu vực nhà ở kèm khói đen đặc.\",\"confidenceScore\":0.92}",
                ProcessingDurationMs = stopwatch.ElapsedMilliseconds
            };
        }

        string? rawResponseContent = null;

        try
        {
            var client = _httpClientFactory.CreateClient("OpenRouterClient");
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            if (!client.DefaultRequestHeaders.Contains("HTTP-Referer"))
            {
                client.DefaultRequestHeaders.Add("HTTP-Referer", siteUrl);
            }
            if (!client.DefaultRequestHeaders.Contains("X-Title"))
            {
                client.DefaultRequestHeaders.Add("X-Title", siteName);
            }

            // Chuẩn bị System Prompt ép output đúng cấu trúc JSON
            var systemPrompt = "You are an expert AI emergency dispatcher and hazard assessor for an emergency response system. " +
                               "Analyze the provided emergency image and context with extreme precision. " +
                               "You MUST output ONLY a valid, raw JSON object (NO markdown backticks, NO ```json blocks, NO preamble) matching this schema:\n" +
                               "{\n" +
                               "  \"hazardTags\": [\"fire\", \"smoke\", \"trapped_victims\"],\n" +
                               "  \"severityScore\": 3,\n" +
                               "  \"summary\": \"Brief concise assessment in Vietnamese.\",\n" +
                               "  \"confidenceScore\": 0.95\n" +
                               "}\n" +
                               "Rules:\n" +
                               "- severityScore MUST be an integer between 0 and 3:\n" +
                               "  0: Not an emergency / reject (cooking, BBQ, controlled burning, drills, normal traffic, blurry or irrelevant image).\n" +
                               "  1: Minor (small trash or grass fire, minor collision, no one trapped).\n" +
                               "  2: Moderate (one vehicle, room or kiosk on fire; overturned vehicle or injured people but nobody trapped).\n" +
                               "  3: Critical (large building or factory fire, multi-vehicle crash, tanker involved, trapped or multiple victims).\n" +
                               "- hazardTags: concise lowercase English keywords.\n" +
                               "- summary: 1-2 Vietnamese sentences.\n" +
                               "- confidenceScore: float between 0.0 and 1.0.";

            var userText = string.IsNullOrWhiteSpace(additionalContext)
                ? "Hãy phân tích hình ảnh hiện trường sự cố khẩn cấp này và đánh giá mức độ nguy hiểm."
                : $"Hãy phân tích hiện trường sự cố khẩn cấp này. Bối cảnh người dân cung cấp: {additionalContext}";

            // Payload theo chuẩn OpenAI Vision tương thích Qwen3-VL trên OpenRouter
            // Hỗ trợ tự động chuyển đổi dự phòng (failover) nếu model chính gặp sự cố
            var requestPayload = new
            {
                model = modelName,
                models = new[] { modelName, "qwen/qwen-2.5-vl-72b-instruct:free", "qwen/qwen3.8-flash" },
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = systemPrompt
                    },
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = userText },
                            new { type = "image_url", image_url = new { url = mediaUrl } }
                        }
                    }
                },
                temperature = 0.2,
                max_tokens = 600
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestPayload),
                Encoding.UTF8,
                "application/json");

            var response = await client.PostAsync("chat/completions", jsonContent, cancellationToken);
            rawResponseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"OpenRouter API trả về mã lỗi {(int)response.StatusCode}: {response.ReasonPhrase}. Nội dung: {rawResponseContent}");
            }

            // Parse response từ OpenRouter
            using var doc = JsonDocument.Parse(rawResponseContent);
            var root = doc.RootElement;
            var messageContent = root
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(messageContent))
            {
                throw new InvalidOperationException("Mô hình AI trả về nội dung trống.");
            }

            // Chuẩn hóa và làm sạch JSON
            var cleanJson = CleanJsonString(messageContent);
            var parsedResult = JsonSerializer.Deserialize<AiClassificationResultDto>(cleanJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (parsedResult == null)
            {
                throw new JsonException("Không thể parse dữ liệu JSON từ kết quả của AI.");
            }

            stopwatch.Stop();
            parsedResult.IsSuccess = true;
            parsedResult.ModelName = modelName;
            parsedResult.RawResponse = rawResponseContent;
            parsedResult.ProcessingDurationMs = stopwatch.ElapsedMilliseconds;

            return parsedResult;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Lỗi khi gọi mô hình Vision-Language Qwen2.5-VL hoặc parse phản hồi. Kích hoạt cơ chế Fallback (Severity = 0 / Unclassified).");

            // Kích hoạt Fallback an toàn (Severity = 0)
            return AiClassificationResultDto.CreateFallback(
                errorMessage: ex.Message,
                rawResponse: rawResponseContent,
                durationMs: stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Gọi microservice AI cục bộ (Qwen2.5-VL-3B LoRA Adapter trên GPU nội bộ)
    /// </summary>
    private async Task<AiClassificationResultDto?> TryAnalyzeLocalAsync(
        string mediaUrl,
        string? additionalContext,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var useLocal = _configuration.GetValue<bool>("AiService:UseLocalModel");
        var localUrl = _configuration["AiService:LocalServiceUrl"] ?? "http://127.0.0.1:8000/api/analyze-incident";
        if (!useLocal)
        {
            return null;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(45);

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(mediaUrl), "image_url");
            if (!string.IsNullOrWhiteSpace(additionalContext))
            {
                form.Add(new StringContent(additionalContext), "citizen_note");
            }

            var response = await client.PostAsync(localUrl, form, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Local AI Service trả về mã {StatusCode}. Tự động chuyển fallback.", response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var incidentType = root.TryGetProperty("incident_type", out var itProp) ? itProp.GetString() : "none";
            var severityLevel = root.TryGetProperty("severity_level", out var slProp) ? slProp.GetInt32() : 0;
            var trafficJam = root.TryGetProperty("traffic_jam", out var tjProp) && tjProp.GetBoolean();
            var description = root.TryGetProperty("description", out var descProp) ? descProp.GetString() : string.Empty;
            var confidence = root.TryGetProperty("confidence", out var confProp) ? confProp.GetDouble() : 0.95;

            var tags = new List<string>();
            if (root.TryGetProperty("tags", out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var tag in tagsProp.EnumerateArray())
                {
                    var t = tag.GetString();
                    if (!string.IsNullOrWhiteSpace(t))
                    {
                        tags.Add(t);
                    }
                }
            }

            stopwatch.Stop();
            _logger.LogInformation("Phân loại AI thành công bằng Local Qwen2.5-VL LoRA: Type={Type}, Level={Level}, Jam={Jam} ({Ms}ms)",
                incidentType, severityLevel, trafficJam, stopwatch.ElapsedMilliseconds);

            return new AiClassificationResultDto
            {
                IncidentType = incidentType,
                SeverityScore = severityLevel,
                IsTrafficJam = trafficJam,
                HazardTags = tags,
                Summary = description,
                ConfidenceScore = confidence,
                IsSuccess = true,
                ModelName = "Qwen2.5-VL-3B-LoRA (Local GPU)",
                RawResponse = content,
                ProcessingDurationMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Local AI Service ({Url}) hiện không khả dụng. Chuyển sang dịch vụ AI đám mây/mô phỏng.", localUrl);
            return null;
        }
    }

    /// <summary>
    /// Loại bỏ các định dạng Markdown như ```json ... ``` hoặc ký tự thừa xung quanh JSON
    /// </summary>
    private static string CleanJsonString(string raw)
    {
        var trimmed = raw.Trim();

        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[7..];
        }
        else if (trimmed.StartsWith("```"))
        {
            trimmed = trimmed[3..];
        }

        if (trimmed.EndsWith("```"))
        {
            trimmed = trimmed[..^3];
        }

        trimmed = trimmed.Trim();

        // Tìm điểm bắt đầu '{' và kết thúc '}' nếu có text trò chuyện ngoài lề
        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            trimmed = trimmed.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        return trimmed;
    }
}
