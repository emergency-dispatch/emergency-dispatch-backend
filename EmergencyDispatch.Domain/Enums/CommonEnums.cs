namespace EmergencyDispatch.Domain.Enums;

/// <summary>
/// Định dạng phương tiện truyền thông đính kèm (hình ảnh hoặc video)
/// </summary>
public enum MediaType
{
    Photo = 0,
    Video = 1
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
    System = 4
}
