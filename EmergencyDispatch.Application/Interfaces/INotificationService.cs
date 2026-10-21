using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Notification;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.Interfaces;

/// <summary>
/// Thông báo trong ứng dụng + realtime + push FCM (FR-CIT-04, FR-RS-01, BR-21). Lỗi gửi chỉ được log, không làm hỏng nghiệp vụ.
/// </summary>
public interface INotificationService
{
    Task NotifyUsersAsync(IReadOnlyCollection<Guid> userIds, NotificationRequest request, CancellationToken cancellationToken = default);
    Task NotifyRolesAsync(IReadOnlyCollection<UserRole> roles, NotificationRequest request, CancellationToken cancellationToken = default);
    Task NotifyStationStaffAsync(Guid stationId, NotificationRequest request, CancellationToken cancellationToken = default);

    Task<PaginatedResultDto<NotificationDto>> GetMyNotificationsAsync(Guid userId, bool unreadOnly, int pageIndex, int pageSize, CancellationToken cancellationToken = default);
    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default);
    Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Gửi push notification tới thiết bị di động (Firebase Cloud Messaging)
/// </summary>
public interface IPushNotificationSender
{
    Task<PushSendResult> SendAsync(string deviceToken, string title, string body, IReadOnlyDictionary<string, string> data, bool highPriority, CancellationToken cancellationToken = default);
}
