using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Notification;

/// <summary>
/// Thông báo trong ứng dụng (đồng thời gửi realtime qua SignalR "ReceiveNotification" và push FCM nếu có token)
/// </summary>
public class NotificationDto
{
    public Guid Id { get; set; }
    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid? IncidentId { get; set; }
    public string? DataJson { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Yêu cầu tạo thông báo nội bộ cho một nhóm người dùng
/// </summary>
public class NotificationRequest
{
    public NotificationType Type { get; set; } = NotificationType.System;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid? IncidentId { get; set; }
    public object? Data { get; set; }

    /// <summary>
    /// Push FCM ưu tiên cao kèm âm thanh còi hú (kênh EMERGENCY_DISPATCH) — dùng cho lệnh điều phối và leo thang
    /// </summary>
    public bool HighPriority { get; set; }
}

/// <summary>
/// Kết quả gửi push cho một thiết bị
/// </summary>
public class PushSendResult
{
    public bool Success { get; set; }
    public string? MessageId { get; set; }
    public string? Error { get; set; }
}
