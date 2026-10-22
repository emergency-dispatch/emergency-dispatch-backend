using System.ComponentModel.DataAnnotations;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Escalation;

/// <summary>
/// Cập nhật một phần quy tắc leo thang (trường null = giữ nguyên).
/// Ngưỡng thời gian và bán kính sửa tại PUT /api/settings/escalation.
/// </summary>
public class UpdateEscalationRuleDto
{
    [Range(1, 3, ErrorMessage = "Mức độ leo thang tối thiểu phải từ 1 đến 3")]
    public SeverityLevel? MinSeverityLevel { get; set; }

    /// <summary>
    /// Mảng JSON các vai trò nhận cảnh báo, chỉ gồm "Admin", "Operator", "RescueStaff"
    /// </summary>
    public string? NotifyRoles { get; set; }

    public bool? IsActive { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }
}
