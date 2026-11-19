namespace EmergencyDispatch.Domain.Entities;

/// <summary>Immutable station handover history / Lịch sử bàn giao trạm của sự cố.</summary>
public class IncidentStationTransfer : BaseEntity
{
    public Guid IncidentId { get; set; }
    public Incident Incident { get; set; } = null!;
    public Guid? FromStationId { get; set; }
    public Guid ToStationId { get; set; }
    public string? FromStationName { get; set; }
    public string ToStationName { get; set; } = string.Empty;
    public Guid ChainId { get; set; }
    public int AssignmentVersion { get; set; }
    public bool IsAutomatic { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public Guid? ActorUserId { get; set; }
    public string? ActorName { get; set; }
    public DateTime? PreviousAssignedAt { get; set; }
    public DateTime? PreviousAcknowledgedAt { get; set; }
    public Guid? PreviousAcknowledgedByUserId { get; set; }
    // Durable processing marker; ownership history itself remains unchanged.
    // Dấu xử lý bền vững để không mất lượt thử sau khi backend khởi động lại.
    public DateTime? AutoDispatchAttemptedAt { get; set; }
    public string SelectionMethod { get; set; } = string.Empty;
    public double? SelectionDistanceKm { get; set; }
}
