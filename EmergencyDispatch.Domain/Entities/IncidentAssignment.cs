using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Phân công nhiệm vụ cứu hộ cho nhân viên và phương tiện
/// </summary>
public class IncidentAssignment : BaseEntity
{
    public Guid IncidentId { get; set; }
    public Incident? Incident { get; set; }

    public Guid StaffId { get; set; }
    public User? Staff { get; set; }

    public Guid VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public Guid AssignedByUserId { get; set; }
    public User? AssignedByUser { get; set; }

    public AssignmentStatus Status { get; set; } = AssignmentStatus.Pending;
    public RejectionReason? RejectionReason { get; set; }
    public string? RejectionNotes { get; set; }

    public string? DispatchAlgorithm { get; set; }
    public double? DistanceKm { get; set; }
    public int? EstimatedArrivalMinutes { get; set; }

    public DateTime? AcceptedAt { get; set; }
    public DateTime? EnRouteAt { get; set; }
    public DateTime? OnSceneAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Navigation properties
    public CompletionReport? CompletionReport { get; set; }
    public ICollection<AssignmentStatusHistory> StatusHistories { get; set; } = new List<AssignmentStatusHistory>();
}
