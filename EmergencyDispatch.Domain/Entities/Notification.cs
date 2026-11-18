using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Thông báo gửi tới người dùng trong ứng dụng và push notification qua Firebase
/// </summary>
public class Notification : BaseEntity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid? IncidentId { get; set; }
    public Incident? Incident { get; set; }

    public NotificationType Type { get; set; }
    /// <summary>
    /// Bản tiếng Việt (mặc định)
    /// </summary>
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Bản tiếng Anh dựng lúc tạo (null với thông báo cũ → dùng bản tiếng Việt)
    /// </summary>
    public string? TitleEn { get; set; }
    public string? MessageEn { get; set; }
    public string? DataJson { get; set; }

    public bool IsRead { get; set; } = false;
    public DateTime? ReadAt { get; set; }

    public string? FcmMessageId { get; set; }
    public bool? FcmSentSuccess { get; set; } = false;
}
