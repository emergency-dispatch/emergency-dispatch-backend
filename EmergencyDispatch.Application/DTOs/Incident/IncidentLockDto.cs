namespace EmergencyDispatch.Application.DTOs.Incident;

/// <summary>
/// Kết quả giữ khóa mềm sự cố (FR-OP-02, BR-15)
/// </summary>
public class IncidentLockDto
{
    public Guid IncidentId { get; set; }
    public Guid LockedByOperatorId { get; set; }
    public string LockedByOperatorName { get; set; } = string.Empty;
    public DateTime LockExpiresAt { get; set; }
}
