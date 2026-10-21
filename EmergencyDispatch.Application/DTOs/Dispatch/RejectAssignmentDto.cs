using System.ComponentModel.DataAnnotations;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Dispatch;

/// <summary>
/// Đội cứu hộ từ chối lệnh điều phối kèm lý do bắt buộc (FR-RS-01, BR-22)
/// </summary>
public class RejectAssignmentDto
{
    [Required(ErrorMessage = "Lý do từ chối là bắt buộc.")]
    public RejectionReason Reason { get; set; }

    /// <summary>
    /// Ghi chú thêm — bắt buộc tối thiểu 5 ký tự khi Reason = Other
    /// </summary>
    [MaxLength(1000, ErrorMessage = "Ghi chú không được vượt quá 1000 ký tự.")]
    public string? Notes { get; set; }
}
