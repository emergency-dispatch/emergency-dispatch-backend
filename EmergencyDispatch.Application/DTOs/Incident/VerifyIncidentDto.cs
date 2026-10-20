using System.ComponentModel.DataAnnotations;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Incident;

/// <summary>
/// DTO để Điều phối viên (Operator) xác minh và điều chỉnh đánh giá của AI (Human-in-the-loop)
/// </summary>
public class VerifyIncidentDto
{
    /// <summary>
    /// Mức độ nghiêm trọng xác nhận (Level 1 - 3). Operator có thể giữ nguyên gợi ý của AI hoặc điều chỉnh lại.
    /// Sự cố không phải khẩn cấp (Level 0 / Reject) thì dùng API hủy sự cố thay vì xác minh.
    /// </summary>
    [Required(ErrorMessage = "Mức độ nghiêm trọng là bắt buộc khi xác minh")]
    [Range(1, 3, ErrorMessage = "Mức độ nghiêm trọng phải từ 1 đến 3")]
    public SeverityLevel ConfirmedSeverity { get; set; }

    /// <summary>
    /// Tiêu đề chuẩn hóa sau khi xác minh
    /// </summary>
    [MaxLength(250)]
    public string? AdjustedTitle { get; set; }

    /// <summary>
    /// Ghi chú chuyên môn hoặc chỉ dẫn thêm cho đội cứu hộ
    /// </summary>
    [MaxLength(1000)]
    public string? OperatorNotes { get; set; }
}
