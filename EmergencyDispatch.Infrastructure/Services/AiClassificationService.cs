using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EmergencyDispatch.Application.DTOs.Ai;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Enums;
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

        // Trường hợp chưa cấu hình API Key: trả Fallback rõ ràng (KHÔNG mô phỏng kết quả giả)
        // Kết quả giả (fire, Level 3) sẽ bị DualEngine ghép với YOLO như thật → nguy hiểm
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.StartsWith("sk-or-v1-YOUR_"))
        {
            _logger.LogWarning("OpenRouter API Key chưa được cấu hình. Trả Fallback an toàn (Severity = 0).");
            stopwatch.Stop();

            return AiClassificationResultDto.CreateFallback(
                errorMessage: "OpenRouter API Key chưa được cấu hình. Cần cài đặt API key hợp lệ trong appsettings.json → OpenRouter:ApiKey.",
                durationMs: stopwatch.ElapsedMilliseconds);
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
                               "  \"incidentType\": \"fire\",\n" +
                               "  \"isTrafficJam\": false,\n" +
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
                               "- incidentType MUST be one of: \"fire\" (fire / explosion), \"accident\" (traffic accident), \"none\" (neither).\n" +
                               "- isTrafficJam: true if vehicles block the road and would delay rescue access.\n" +
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
                // JSON kết quả chỉ ~150 token; để thấp giúp không bị 402 khi tài khoản còn ít credit
                max_tokens = _configuration.GetValue("OpenRouter:MaxTokens", 300)
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
            using var contentDoc = JsonDocument.Parse(cleanJson);
            var contentRoot = contentDoc.RootElement;

            // Parse and validate the same aliases; never accept prose-only JSON as a classification.
            // Chuẩn hóa cùng một bộ tên trường và kiểm tra đủ dữ liệu trước khi ghép với YOLO.
            if (!TryGetResponseField(contentRoot, out var incidentTypeField, "incidentType", "incident_type")
                || incidentTypeField.ValueKind != JsonValueKind.String)
                throw new JsonException("Qwen-VL thiếu loại sự cố hợp lệ (incidentType).");
            var incidentType = incidentTypeField.GetString()?.Trim().ToLowerInvariant();
            if (incidentType is not ("fire" or "accident" or "none"))
                throw new JsonException("Qwen-VL trả loại sự cố ngoài fire/accident/none.");

            if (!TryGetResponseField(contentRoot, out var severityField, "severityScore", "severity_score", "severity", "severity_level")
                || severityField.ValueKind != JsonValueKind.Number
                || !severityField.TryGetInt32(out var severity)
                || severity is < 0 or > 3)
                throw new JsonException("Qwen-VL thiếu mức độ nghiêm trọng nguyên từ 0 đến 3.");

            if (!TryGetResponseField(contentRoot, out var summaryField, "summary", "description")
                || summaryField.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(summaryField.GetString()))
                throw new JsonException("Qwen-VL thiếu nội dung đánh giá hiện trường.");

            var tags = new List<string>();
            if (TryGetResponseField(contentRoot, out var tagsField, "hazardTags", "hazard_tags")
                && tagsField.ValueKind != JsonValueKind.Null)
            {
                if (tagsField.ValueKind != JsonValueKind.Array)
                    throw new JsonException("Qwen-VL trả hazardTags không phải danh sách.");
                foreach (var tag in tagsField.EnumerateArray())
                {
                    if (tag.ValueKind != JsonValueKind.String)
                        throw new JsonException("Qwen-VL trả hazardTags chứa phần tử không phải chuỗi.");
                    if (!string.IsNullOrWhiteSpace(tag.GetString())) tags.Add(tag.GetString()!.Trim());
                }
            }

            double confidence = 0;
            if (TryGetResponseField(contentRoot, out var confidenceField, "confidenceScore", "confidence_score", "confidence")
                && confidenceField.ValueKind != JsonValueKind.Null)
            {
                if (confidenceField.ValueKind != JsonValueKind.Number
                    || !confidenceField.TryGetDouble(out confidence)
                    || !double.IsFinite(confidence) || confidence is < 0 or > 1)
                    throw new JsonException("Qwen-VL trả độ tin cậy ngoài khoảng 0 đến 1.");
            }

            var hasTrafficJamField = TryGetResponseField(contentRoot, out var tjElem,
                "isTrafficJam", "is_traffic_jam", "trafficJam", "traffic_jam");
            var parsedResult = new AiClassificationResultDto
            {
                IncidentType = incidentType,
                SeverityScore = severity,
                Summary = summaryField.GetString(),
                HazardTags = tags,
                ConfidenceScore = confidence
            };

            stopwatch.Stop();
            parsedResult.IsSuccess = true;
            // A provider fallback may use a different model than the configured first choice.
            parsedResult.ModelName = root.TryGetProperty("model", out var actualModel)
                && actualModel.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(actualModel.GetString()) ? actualModel.GetString() : modelName;
            parsedResult.RawResponse = rawResponseContent;
            parsedResult.ProcessingDurationMs = stopwatch.ElapsedMilliseconds;

            // Chuẩn hóa trạng thái ùn tắc từ Qwen-VL (AI chỉ dừng ở Suspected hoặc NoEvidence, IsTrafficJam=false chờ con người Confirmed)
            // Nếu không có trường traffic_jam trong JSON trả về -> Trạng thái phải là Unverified!
            if (hasTrafficJamField && (tjElem.ValueKind == JsonValueKind.True || tjElem.ValueKind == JsonValueKind.False))
            {
                var isJam = tjElem.GetBoolean();
                if (isJam)
                {
                    parsedResult.TrafficJamStatus = TrafficJamStatus.Suspected;
                    parsedResult.TrafficJamEvidence = "Qwen-VL nhận định có dấu hiệu cản trở hoặc ùn tắc giao thông tại hiện trường.";
                }
                else
                {
                    parsedResult.TrafficJamStatus = TrafficJamStatus.NoEvidence;
                    parsedResult.TrafficJamEvidence = "Qwen-VL không phát hiện dấu hiệu ùn tắc trên ảnh.";
                }
            }
            else
            {
                parsedResult.TrafficJamStatus = TrafficJamStatus.Unverified;
                parsedResult.TrafficJamEvidence = "Qwen-VL không trả về trường đánh giá ùn tắc giao thông (thiếu dữ liệu).";
            }
            parsedResult.IsTrafficJam = false;

            return parsedResult;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Lỗi khi gọi Qwen-VL hoặc parse phản hồi. Kích hoạt cơ chế Fallback (Severity = 0 / Unclassified).");

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
            // Lần suy luận đầu sau khi bật GPU có thể mất > 30s
            client.Timeout = TimeSpan.FromSeconds(_configuration.GetValue("AiService:LocalTimeoutSeconds", 90));

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(mediaUrl), "image_url");
            if (!string.IsNullOrWhiteSpace(additionalContext))
            {
                form.Add(new StringContent(additionalContext), "citizen_note");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, localUrl) { Content = form };

            // AI service chạy trên Kaggle/Colab qua ngrok: gửi khóa bí mật (AiService:ApiKey)
            // và bỏ trang cảnh báo của ngrok bản free (nếu không sẽ nhận về HTML thay vì JSON)
            var serviceKey = _configuration["AiService:ApiKey"];
            if (!string.IsNullOrWhiteSpace(serviceKey))
            {
                request.Headers.Add("X-AI-Service-Key", serviceKey);
            }
            request.Headers.Add("ngrok-skip-browser-warning", "true");

            var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Local AI Service trả về mã {StatusCode}: {Body}. Tự động chuyển fallback.",
                    (int)response.StatusCode, errorBody.Length > 300 ? errorBody[..300] : errorBody);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            // Kiểm tra nếu JSON trả về rỗng ({}) hoặc thiếu toàn bộ trường cần thiết
            var hasIncidentType = root.TryGetProperty("incident_type", out var itProp);
            var hasSeverity = root.TryGetProperty("severity_level", out var slProp);
            var hasTrafficJam = root.TryGetProperty("traffic_jam", out var tjProp);
            var hasDescription = root.TryGetProperty("description", out var descProp);
            var hasConfidence = root.TryGetProperty("confidence", out var confProp);

            if (!hasIncidentType && !hasSeverity && !hasTrafficJam && !hasDescription)
            {
                _logger.LogWarning("Local AI Service trả về JSON rỗng hoặc thiếu dữ liệu: {Content}. Chuyển sang fallback/cloud.", content);
                return null;
            }

            var incidentType = hasIncidentType ? itProp.GetString() : "none";
            var severityLevel = hasSeverity && slProp.TryGetInt32(out var slVal) ? slVal : 0;
            var description = hasDescription ? descProp.GetString() : string.Empty;
            // Không tự ý điền 0.95 khi thiếu trường confidence
            var confidence = hasConfidence && confProp.TryGetDouble(out var cVal) ? cVal : 0.0;

            TrafficJamStatus trafficJamStatus;
            string trafficJamEvidence;
            if (hasTrafficJam && (tjProp.ValueKind == JsonValueKind.True || tjProp.ValueKind == JsonValueKind.False))
            {
                var trafficJam = tjProp.GetBoolean();
                trafficJamStatus = trafficJam ? TrafficJamStatus.Suspected : TrafficJamStatus.NoEvidence;
                trafficJamEvidence = trafficJam
                    ? "Qwen-VL Local nhận định có dấu hiệu cản trở hoặc ùn tắc giao thông."
                    : "Qwen-VL Local không phát hiện dấu hiệu ùn tắc trên ảnh.";
            }
            else
            {
                trafficJamStatus = TrafficJamStatus.Unverified;
                trafficJamEvidence = "Qwen-VL Local không trả về trường đánh giá ùn tắc (thiếu dữ liệu).";
            }

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
                incidentType, severityLevel, trafficJamStatus, stopwatch.ElapsedMilliseconds);

            return new AiClassificationResultDto
            {
                IncidentType = incidentType,
                SeverityScore = severityLevel,
                IsTrafficJam = false, // AI không tự ý gán Confirmed
                TrafficJamStatus = trafficJamStatus,
                TrafficJamEvidence = trafficJamEvidence,
                HazardTags = tags,
                Summary = description,
                ConfidenceScore = confidence,
                IsSuccess = true,
                ModelName = "Qwen2.5-VL-3B-LoRA (Local GPU)",
                RawResponse = content,
                ProcessingDurationMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Local AI Service ({Url}) không khả dụng: {Reason}. Chuyển sang dịch vụ AI đám mây/mô phỏng.",
                localUrl, ex is TaskCanceledException ? "quá thời gian chờ" : ex.Message);
            return null;
        }
    }

    private static bool TryGetResponseField(JsonElement root, out JsonElement value, params string[] names)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
                if (root.TryGetProperty(name, out value)) return true;
            foreach (var property in root.EnumerateObject())
                if (names.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
        }
        value = default;
        return false;
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
