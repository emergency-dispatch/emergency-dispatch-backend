using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Nhật ký kiểm toán chi tiết từng hành động tác động lên sự cố
/// </summary>
public class IncidentAuditTrail : BaseEntity
{
    public Guid IncidentId { get; set; }
    public Incident? Incident { get; set; }

    public Guid? ActorId { get; set; }
    public User? Actor { get; set; }

    public string? ActorRole { get; set; }
    public AuditAction Action { get; set; }

    public string? OldStateJson { get; set; }
    public string? NewStateJson { get; set; }

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Description { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
