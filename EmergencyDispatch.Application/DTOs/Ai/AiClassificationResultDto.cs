using System.Text.Json.Serialization;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Ai;

/// <summary>
/// DTO chứa kết quả phân tích sự cố từ mô hình Vision-Language AI
/// </summary>
public class AiClassificationResultDto
{
    /// <summary>
    /// Danh sách các nhãn mối nguy hiểm phát hiện được (vd: "fire", "heavy_smoke", "structural_damage", "traffic_accident")
    /// </summary>
    [JsonPropertyName("hazardTags")]
    public List<string> HazardTags { get; set; } = new();

    /// <summary>
    /// Điểm mức độ nghiêm trọng (0: Reject / Unclassified, 1: Nhẹ, 2: Trung bình, 3: Khẩn cấp cao nhất)
    /// </summary>
    [JsonPropertyName("severityScore")]
    public int SeverityScore { get; set; }

    /// <summary>
    /// Loại sự cố nhận diện (fire, accident, none)
    /// </summary>
    [JsonPropertyName("incidentType")]
    public string? IncidentType { get; set; }

    /// <summary>
    /// Phát hiện có ùn tắc giao thông tại hiện trường hay không
    /// </summary>
    [JsonPropertyName("isTrafficJam")]
    public bool IsTrafficJam { get; set; }

    /// <summary>
    /// Nhánh sự kiện chuẩn hóa (FR-SYS-01): FireExplosion / TrafficAccident / None
    /// </summary>
    [JsonPropertyName("eventType")]
    public IncidentEventType EventType => ParseEventType(IncidentType);

    /// <summary>
    /// Chuẩn hóa nhãn loại sự cố tự do của mô hình ("fire", "accident", "FireExplosion"...) về enum
    /// </summary>
    public static IncidentEventType ParseEventType(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "fire" or "fireexplosion" or "fire_explosion" or "explosion" => IncidentEventType.FireExplosion,
        "accident" or "trafficaccident" or "traffic_accident" or "crash" => IncidentEventType.TrafficAccident,
        _ => IncidentEventType.None
    };

    /// <summary>
    /// Nhãn chuẩn lưu vào incidentType khi map ngược từ DB
    /// </summary>
    public static string ToIncidentTypeLabel(IncidentEventType eventType) => eventType switch
    {
        IncidentEventType.FireExplosion => "fire",
        IncidentEventType.TrafficAccident => "accident",
        _ => "none"
    };

    /// <summary>
    /// Enum SeverityLevel tương ứng (chuẩn 0-3; điểm ngoài khoảng được kẹp về 0 hoặc Level3)
    /// </summary>
    [JsonIgnore]
    public SeverityLevel SeverityLevel => (SeverityLevel)Math.Clamp(SeverityScore, 0, 3);

    /// <summary>
    /// Tóm tắt nhanh nhận định của AI về hiện trường sự cố
    /// </summary>
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    /// <summary>
    /// Ước tính mức độ tự tin của AI (0.0 đến 1.0)
    /// </summary>
    [JsonPropertyName("confidenceScore")]
    public double? ConfidenceScore { get; set; }

    /// <summary>
    /// Cờ đánh dấu phân loại thành công hay kích hoạt cơ chế Fallback
    /// </summary>
    public bool IsSuccess { get; set; } = true;

    /// <summary>
    /// Tên mô hình AI được sử dụng
    /// </summary>
    public string? ModelName { get; set; }

    /// <summary>
    /// Chuỗi JSON phản hồi thô từ mô hình (hỗ trợ debug và nghiên cứu)
    /// </summary>
    public string? RawResponse { get; set; }

    /// <summary>
    /// Chi tiết lỗi khi xảy ra sự cố gọi API hoặc parse dữ liệu
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Thời gian AI xử lý tính bằng mili-giây
    /// </summary>
    public long ProcessingDurationMs { get; set; }

    /// <summary>
    /// Tạo kết quả Fallback an toàn khi AI gặp sự cố (Timeout, 5xx, JSON lỗi)
    /// </summary>
    public static AiClassificationResultDto CreateFallback(string errorMessage, string? rawResponse = null, long durationMs = 0)
    {
        return new AiClassificationResultDto
        {
            HazardTags = new List<string> { "Unclassified", "NeedsHumanReview" },
            SeverityScore = (int)SeverityLevel.Unclassified,
            Summary = "Không thể phân loại tự động bởi AI. Sự cố đã được gắn cờ để Điều phối viên (Operator) đánh giá thủ công.",
            ConfidenceScore = 0.0,
            IsSuccess = false,
            ErrorMessage = errorMessage,
            RawResponse = rawResponse,
            ProcessingDurationMs = durationMs
        };
    }
}
