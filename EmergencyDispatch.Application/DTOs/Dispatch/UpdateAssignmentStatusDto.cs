using System.ComponentModel.DataAnnotations;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Dispatch;

/// <summary>
/// Cập nhật tiến độ của lượt điều phối (Accepted -> EnRoute -> OnScene)
/// </summary>
public class UpdateAssignmentStatusDto
{
    [Required(ErrorMessage = "Trạng thái mới là bắt buộc.")]
    public DispatchAssignmentStatus Status { get; set; }

    [MaxLength(1000, ErrorMessage = "Ghi chú không được vượt quá 1000 ký tự.")]
    public string? Notes { get; set; }
}
