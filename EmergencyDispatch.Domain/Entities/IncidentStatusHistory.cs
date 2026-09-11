using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Lịch sử thay đổi trạng thái của sự cố khẩn cấp
/// </summary>
public class IncidentStatusHistory : BaseEntity
{
    public Guid IncidentId { get; set; }
    public Incident? Incident { get; set; }

    public IncidentStatus OldStatus { get; set; }
    public IncidentStatus NewStatus { get; set; }

    public Guid? ChangedByUserId { get; set; }
    public User? ChangedByUser { get; set; }

    public string? ChangedByRole { get; set; }
    public string? Notes { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
