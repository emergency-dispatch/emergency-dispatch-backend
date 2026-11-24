namespace EmergencyDispatch.Domain.Entities;

/// <summary>One area-only citizen announcement per canonical incident / Một cảnh báo khu vực cho mỗi sự cố gốc.</summary>
public class CitizenIncidentAlert : BaseEntity
{
    public Guid IncidentId { get; set; }
    public Incident Incident { get; set; } = null!;
    public string AreaVi { get; set; } = string.Empty;
    public string AreaEn { get; set; } = string.Empty;
    // Internal evidence; not returned by the citizen API.
    public string TriggerKind { get; set; } = string.Empty;
    public DateTime TriggeredAt { get; set; }
}

/// <summary>Durable per-recipient dispatch; the inbox notification is committed before delivery.</summary>
public class CitizenIncidentAlertDelivery : BaseEntity
{
    public Guid AlertId { get; set; }
    public CitizenIncidentAlert Alert { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid NotificationId { get; set; }
    public Notification Notification { get; set; } = null!;
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public Guid? LeaseId { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? RealtimeAttemptedAt { get; set; }
    public string? LastError { get; set; }
}
