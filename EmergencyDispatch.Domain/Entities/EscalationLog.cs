namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Nhật ký kích hoạt quy tắc leo thang sự cố
/// </summary>
public class EscalationLog : BaseEntity
{
    public Guid IncidentId { get; set; }
    public Incident? Incident { get; set; }

    public Guid EscalationRuleId { get; set; }
    public EscalationRule? EscalationRule { get; set; }

    public int EscalationLevel { get; set; } = 1;
    public string? NotifiedRoles { get; set; }
    public string? NotifiedUserIds { get; set; }
    public string? ActionTaken { get; set; }

    public DateTime TriggerTime { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public long? ResolutionTimeMs { get; set; }
}
