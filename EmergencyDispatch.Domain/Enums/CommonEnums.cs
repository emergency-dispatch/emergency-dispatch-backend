namespace EmergencyDispatch.Domain.Enums;

/// <summary>
/// Định dạng media đính kèm; giữ nguyên Photo=0, Video=1 để tương thích client cũ.
/// </summary>
public enum MediaType
{
    Photo = 0,
    Video = 1,
    Audio = 2
}

/// <summary>
/// Phân loại thông báo hệ thống
/// </summary>
public enum NotificationType
{
    NewIncident = 0,
    JobAssigned = 1,
    StatusUpdate = 2,
    Escalation = 3,
    System = 4,
    /// <summary>Được giao / rút khỏi nhiệm vụ phân luồng, giải tỏa điểm nghẽn giao thông</summary>
    TrafficTask = 5,
    /// <summary>Area-only announcement after human verification or staff acceptance of an AI dispatch.</summary>
    CitizenIncidentAlert = 6
}
