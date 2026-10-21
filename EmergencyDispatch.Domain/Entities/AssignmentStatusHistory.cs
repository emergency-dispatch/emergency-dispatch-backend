using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Lịch sử thay đổi trạng thái của một lượt phân công điều động xe (bất biến, phục vụ BR-22 và kiểm toán)
/// </summary>
public class AssignmentStatusHistory : BaseEntity
{
    public Guid AssignmentId { get; set; }
    public DispatchAssignment? Assignment { get; set; }

    public DispatchAssignmentStatus OldStatus { get; set; }
    public DispatchAssignmentStatus NewStatus { get; set; }

    public Guid? ChangedByUserId { get; set; }
    public User? ChangedByUser { get; set; }

    /// <summary>
    /// Lý do từ chối nhiệm vụ (chỉ có khi NewStatus = Rejected)
    /// </summary>
    public RejectionReason? RejectionReason { get; set; }

    public string? Notes { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
