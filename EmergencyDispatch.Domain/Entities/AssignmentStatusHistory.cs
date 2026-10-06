using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Lịch sử thay đổi trạng thái phân công nhiệm vụ
/// </summary>
public class AssignmentStatusHistory : BaseEntity
{
    public Guid AssignmentId { get; set; }
    public IncidentAssignment? Assignment { get; set; }

    public AssignmentStatus OldStatus { get; set; }
    public AssignmentStatus NewStatus { get; set; }

    public Guid? ChangedByUserId { get; set; }
    public User? ChangedByUser { get; set; }

    public string? Notes { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
