using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Kết quả phân tích và đánh giá rủi ro từ mô hình Vision-Language AI (Qwen2.5-VL)
/// </summary>
public class AiClassification : BaseEntity
{
    public Guid IncidentId { get; set; }
    public Incident? Incident { get; set; }

    /// <summary>
    /// Mức độ nghiêm trọng do AI đề xuất (Level 0: Unclassified / Fallback, Level 1-5)
    /// </summary>
    public SeverityLevel SeverityScore { get; set; } = SeverityLevel.Unclassified;

    /// <summary>
    /// Danh sách nhãn nguy cơ phát hiện được (vd: "fire", "heavy_smoke", "structural_damage")
    /// </summary>
    public List<string> HazardTags { get; set; } = new();

    /// <summary>
    /// Tóm tắt nhanh nhận định của AI về hiện trường sự cố
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Độ tự tin ước lượng của AI (0.0 đến 1.0)
    /// </summary>
    public double? ConfidenceScore { get; set; }

    /// <summary>
    /// Tên mô hình AI được sử dụng (vd: "qwen/qwen-2.5-vl-72b-instruct:free")
    /// </summary>
    public string? ModelName { get; set; }

    /// <summary>
    /// Phản hồi JSON gốc từ LLM (Dùng để kiểm thử, đánh giá F1-score và nghiên cứu khoa học)
    /// </summary>
    public string? RawResponse { get; set; }

    /// <summary>
    /// Đánh dấu phân loại thành công từ AI (true) hay rơi vào cơ chế Fallback do lỗi/timeout (false)
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Chi tiết lỗi khi xảy ra sự cố gọi API hoặc parse kết quả
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Thời gian AI xử lý tính bằng mili-giây (latency benchmark)
    /// </summary>
    public long? ProcessingDurationMs { get; set; }
}
