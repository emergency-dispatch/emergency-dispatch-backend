namespace EmergencyDispatch.Application.DTOs.Incident;

public class AcknowledgeIncidentDto
{
    public int? ExpectedAssignmentVersion { get; set; }
}

public class StationAcknowledgementDto
{
    public Guid IncidentId { get; set; }
    public Guid? ResponsibleStationId { get; set; }
    public int AssignmentVersion { get; set; }
    public DateTime? AssignedAt { get; set; }
    public DateTime? ResponseDueAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public Guid? AcknowledgedByUserId { get; set; }
}

public class IncidentStationTransferDto
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public Guid? FromStationId { get; set; }
    public Guid ToStationId { get; set; }
    public string? FromStationName { get; set; }
    public string ToStationName { get; set; } = string.Empty;
    public int AssignmentVersion { get; set; }
    public bool IsAutomatic { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public Guid? ActorUserId { get; set; }
    public string? ActorName { get; set; }
    public DateTime TransferredAt { get; set; }
    public DateTime? PreviousAssignedAt { get; set; }
    public DateTime? PreviousAcknowledgedAt { get; set; }
    public Guid? PreviousAcknowledgedByUserId { get; set; }
    public string SelectionMethod { get; set; } = string.Empty;
    public double? SelectionDistanceKm { get; set; }
}
