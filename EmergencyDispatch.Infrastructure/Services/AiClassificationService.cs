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
                SeverityScore = 4, // Level 4
                Summary = "[Mô phỏng AI Qwen2.5-VL] Phát hiện đám cháy bùng phát tại khu vực nhà ở kèm khói đen đặc, nguy cơ lan rộng cao.",
                ConfidenceScore = 0.92,
                IsSuccess = true,
                ModelName = $"{modelName} (Simulated)",
                RawResponse = "{\"hazardTags\":[\"fire\",\"heavy_smoke\",\"residential_area\"],\"severityScore\":4,\"summary\":\"Phát hiện đám cháy bùng phát tại khu vực nhà ở kèm khói đen đặc.\",\"confidenceScore\":0.92}",
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
                               "  \"severityScore\": 4,\n" +
                               "  \"summary\": \"Brief concise assessment in Vietnamese.\",\n" +
                               "  \"confidenceScore\": 0.95\n" +
                               "}\n" +
                               "Rules:\n" +
                               "- severityScore MUST be an integer between 1 and 5 (1: Rất thấp, 2: Thấp, 3: Trung bình, 4: Cao, 5: Cực kỳ khẩn cấp).\n" +
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
