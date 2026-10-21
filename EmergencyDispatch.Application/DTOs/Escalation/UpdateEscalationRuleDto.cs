using System.ComponentModel.DataAnnotations;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Escalation;

/// <summary>
/// Yêu cầu cập nhật quy tắc leo thang tự động
/// </summary>
public class UpdateEscalationRuleDto
{
    [Range(1, 3, ErrorMessage = "Mức độ leo thang tối thiểu phải từ 1 đến 3")]
    public SeverityLevel? MinSeverityLevel { get; set; }

    [Range(1, 60, ErrorMessage = "Thời gian timeout phải từ 1 đến 60 phút")]
    public int? TimeoutMinutes { get; set; }

    public string? NotifyRoles { get; set; }

    [Range(0, 200, ErrorMessage = "Tỷ lệ mở rộng bán kính từ 0% đến 200%")]
    public int? ExpandRadiusPercent { get; set; }

    public bool? IsActive { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }
}
