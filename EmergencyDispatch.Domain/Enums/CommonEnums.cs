namespace EmergencyDispatch.Domain.Enums;

/// <summary>
/// Định dạng phương tiện truyền thông đính kèm (hình ảnh hoặc video)
/// </summary>
public enum MediaType
{
    Photo = 1,
    Video = 2
}

/// <summary>
/// Phân loại thông báo hệ thống
/// </summary>
public enum NotificationType
{
    NewIncident = 1,
    JobAssigned = 2,
    StatusUpdate = 3,
    EscalationAlert = 4,
    System = 5
}
