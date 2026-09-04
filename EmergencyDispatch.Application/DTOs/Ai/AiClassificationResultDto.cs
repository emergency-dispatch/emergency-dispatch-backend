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
    /// Điểm mức độ nghiêm trọng (0: Unclassified, 1: Rất thấp, ..., 5: Cực độ khẩn cấp)
    /// </summary>
    [JsonPropertyName("severityScore")]
    public int SeverityScore { get; set; }

    /// <summary>
    /// Enum SeverityLevel tương ứng
    /// </summary>
    [JsonIgnore]
    public SeverityLevel SeverityLevel => (SeverityLevel)Math.Clamp(SeverityScore, 0, 5);

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
