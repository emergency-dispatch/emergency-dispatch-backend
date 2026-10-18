using System.ComponentModel.DataAnnotations;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Escalation;

/// <summary>
/// Yêu cầu tạo mới quy tắc leo thang tự động
/// </summary>
public class CreateEscalationRuleDto
{
    [Required]
    public SeverityLevel MinSeverityLevel { get; set; } = SeverityLevel.Level3;

    [Range(1, 60, ErrorMessage = "Thời gian timeout phải từ 1 đến 60 phút")]
    public int TimeoutMinutes { get; set; } = 3;

    public string NotifyRoles { get; set; } = "[\"Admin\",\"Operator\"]";

    [Range(0, 200, ErrorMessage = "Tỷ lệ mở rộng bán kính từ 0% đến 200%")]
    public int ExpandRadiusPercent { get; set; } = 50;

    public bool IsActive { get; set; } = true;

    [MaxLength(500)]
    public string? Description { get; set; }
}
