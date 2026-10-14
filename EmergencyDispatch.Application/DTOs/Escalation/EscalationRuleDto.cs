using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Escalation;

/// <summary>
/// DTO thông tin cấu hình quy tắc leo thang tự động
/// </summary>
public class EscalationRuleDto
{
    public Guid Id { get; set; }
    public SeverityLevel MinSeverityLevel { get; set; }
    public int TimeoutMinutes { get; set; }
    public string NotifyRoles { get; set; } = string.Empty;
    public int ExpandRadiusPercent { get; set; }
    public bool IsActive { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
