using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Escalation;

/// <summary>
/// DTO thông tin nhật ký kích hoạt leo thang
/// </summary>
public class EscalationLogDto
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public string? IncidentTitle { get; set; }
    public SeverityLevel IncidentSeverity { get; set; }
    public IncidentStatus IncidentStatus { get; set; }
    public Guid EscalationRuleId { get; set; }
    public int EscalationLevel { get; set; }
    public string? NotifiedRoles { get; set; }
    public string? NotifiedUserIds { get; set; }
    public string? ActionTaken { get; set; }
    public DateTime TriggerTime { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public long? ResolutionTimeMs { get; set; }
}
