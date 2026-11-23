using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EmergencyDispatch.Infrastructure.Services;

/// <summary>Kết quả đọc lại câu trả lời thô của TriageVL</summary>
public sealed record LocalAiOutput(
    string IncidentType,
    int SeverityLevel,
    bool? TrafficJam,
    List<string>? Tags,
    string? Description,
    string? DescriptionEn,
    string ParseStatus);

/// <summary>
/// Đọc lại raw_response khi ai_service.py bản cũ trả "unparseable" (cùng luật với extract_json bản mới):
/// nhiều object JSON liền nhau → object đầu quyết định loại / mức độ (cũng là chỗ tính xác suất token),
/// object sau chỉ bổ sung trường còn thiếu (VD description_en); JSON bị cắt → lấy loại + mức độ nếu đã sinh xong.
/// Ca lỗi gốc: SC-261123-0390, model viết JSON thứ 2 chỉ để chứa description_en
/// </summary>
public static class LocalAiOutputParser
{
    private static readonly string[] ResultKeys = { "incident_type", "severity_level", "traffic_jam", "tags", "description", "description_en" };

    public static LocalAiOutput? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var objects = DecodeObjects(text);
        if (objects.Count > 0)
        {
            var merged = new Dictionary<string, JsonElement>();
            foreach (var obj in objects)
            {
                foreach (var property in obj.EnumerateObject())
                {
                    if (!merged.TryGetValue(property.Name, out var existing) || IsEmpty(existing))
                        merged[property.Name] = property.Value;
                }
            }
            return Build(merged, objects.Count == 1 ? "ok" : "merged");
        }
        return Salvage(text);
    }

    private static List<JsonElement> DecodeObjects(string text)
    {
        var result = new List<JsonElement>();
        var i = text.IndexOf('{');
        while (i >= 0)
        {
            var bytes = Encoding.UTF8.GetBytes(text[i..]);
            try
            {
                var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { AllowMultipleValues = true, AllowTrailingCommas = true });
                using var doc = JsonDocument.ParseValue(ref reader);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && ResultKeys.Any(k => doc.RootElement.TryGetProperty(k, out _)))
                {
                    result.Add(doc.RootElement.Clone());
                }
                var consumedChars = Encoding.UTF8.GetCharCount(bytes, 0, (int)reader.BytesConsumed);
                i = text.IndexOf('{', i + Math.Max(1, consumedChars));
            }
            catch (JsonException)
            {
                i = text.IndexOf('{', i + 1);
            }
        }
        return result;
    }

    private static LocalAiOutput? Salvage(string text)
    {
        var type = Regex.Match(text, "\"incident_type\"\\s*:\\s*\"(fire|accident|none)\"");
        var severity = Regex.Match(text, "\"severity_level\"\\s*:\\s*\"?([0-3])\\b");
        if (!type.Success || !severity.Success) return null;

        var jam = Regex.Match(text, "\"traffic_jam\"\\s*:\\s*(true|false)");
        var tagsMatch = Regex.Match(text, "\"tags\"\\s*:\\s*\\[([^\\]]*)\\]");
        var tags = tagsMatch.Success
            ? Regex.Matches(tagsMatch.Groups[1].Value, "\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList()
            : null;
        return new LocalAiOutput(type.Groups[1].Value, int.Parse(severity.Groups[1].Value),
            jam.Success ? jam.Groups[1].Value == "true" : null, tags,
            SalvageString(text, "description"), SalvageString(text, "description_en"), "salvaged");
    }

    private static string? SalvageString(string text, string key)
    {
        var match = Regex.Match(text, "\"" + key + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        if (!match.Success) return null;
        try
        {
            return JsonSerializer.Deserialize<string>("\"" + match.Groups[1].Value + "\"");
        }
        catch (JsonException)
        {
            return match.Groups[1].Value;
        }
    }

    private static LocalAiOutput Build(Dictionary<string, JsonElement> values, string status)
    {
        var type = values.TryGetValue("incident_type", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()!.Trim().ToLowerInvariant()
            : "none";
        if (type is not ("fire" or "accident" or "none")) type = "none";

        var severity = 0;
        if (values.TryGetValue("severity_level", out var s))
        {
            if (s.ValueKind == JsonValueKind.Number && s.TryGetInt32(out var n)) severity = n;
            else if (s.ValueKind == JsonValueKind.String && int.TryParse(s.GetString(), out var parsed)) severity = parsed;
        }

        bool? jam = null;
        if (values.TryGetValue("traffic_jam", out var j))
        {
            if (j.ValueKind is JsonValueKind.True or JsonValueKind.False) jam = j.GetBoolean();
            else if (j.ValueKind == JsonValueKind.String && bool.TryParse(j.GetString(), out var b)) jam = b;
        }

        List<string>? tags = null;
        if (values.TryGetValue("tags", out var tg) && tg.ValueKind == JsonValueKind.Array)
        {
            tags = tg.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        }

        return new LocalAiOutput(type, Math.Clamp(severity, 0, 3), jam, tags, Text(values, "description"), Text(values, "description_en"), status);
    }

    private static string? Text(Dictionary<string, JsonElement> values, string key)
        => values.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;

    private static bool IsEmpty(JsonElement value)
        => value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
           || (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))
           || (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0);
}
