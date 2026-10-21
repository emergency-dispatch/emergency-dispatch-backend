using System.Text.Json;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Notification;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Application.Services;

/// <summary>
/// Lưu thông báo trong ứng dụng, đẩy realtime tới đúng người dùng và gửi push FCM (FR-CIT-04, FR-RS-01, BR-21)
/// </summary>
public class NotificationService : INotificationService
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IPushNotificationSender _pushSender;
    private readonly IRealtimeNotifier _realtimeNotifier;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        INotificationRepository notificationRepository,
        IUserRepository userRepository,
        IPushNotificationSender pushSender,
        IRealtimeNotifier realtimeNotifier,
        ILogger<NotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _userRepository = userRepository;
        _pushSender = pushSender;
        _realtimeNotifier = realtimeNotifier;
        _logger = logger;
    }

    public async Task NotifyUsersAsync(IReadOnlyCollection<Guid> userIds, NotificationRequest request, CancellationToken cancellationToken = default)
    {
        var users = new List<User>();
        foreach (var id in userIds.Distinct())
        {
            var user = await _userRepository.GetByIdAsync(id);
            if (user != null && user.Status == UserStatus.Active) users.Add(user);
        }

        await SendAsync(users, request, cancellationToken);
    }

    public async Task NotifyRolesAsync(IReadOnlyCollection<UserRole> roles, NotificationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var users = await _userRepository.GetActiveByRolesAsync(roles);
            await SendAsync(users, request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Không gửi được thông báo cho nhóm vai trò {Roles}", string.Join(",", roles));
        }
    }

    public async Task NotifyStationStaffAsync(Guid stationId, NotificationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var staff = (await _userRepository.GetByStationWithTeamAsync(stationId))
                .Where(u => u.Role == UserRole.RescueStaff && u.Status == UserStatus.Active)
                .ToList();
            await SendAsync(staff, request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Không gửi được thông báo cho nhân sự trạm {StationId}", stationId);
        }
    }

    public async Task<PaginatedResultDto<NotificationDto>> GetMyNotificationsAsync(
        Guid userId, bool unreadOnly, int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (items, total) = await _notificationRepository.GetByUserAsync(userId, unreadOnly, pageIndex, pageSize, cancellationToken);
        return new PaginatedResultDto<NotificationDto>(items.Select(Map), total, pageIndex, pageSize);
    }

    public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default)
        => _notificationRepository.CountUnreadAsync(userId, cancellationToken);

    public Task<bool> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default)
        => _notificationRepository.MarkReadAsync(userId, notificationId, DateTime.UtcNow, cancellationToken);

    public Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default)
        => _notificationRepository.MarkAllReadAsync(userId, DateTime.UtcNow, cancellationToken);

    private async Task SendAsync(IReadOnlyCollection<User> users, NotificationRequest request, CancellationToken cancellationToken)
    {
        if (users.Count == 0) return;

        try
        {
            var dataJson = request.Data == null ? null : JsonSerializer.Serialize(request.Data);
            var notifications = users.Select(u => new Notification
            {
                Id = Guid.NewGuid(),
                UserId = u.Id,
                IncidentId = request.IncidentId,
                Type = request.Type,
                Title = request.Title,
                Message = request.Message,
                DataJson = dataJson
            }).ToList();

            // Push FCM tới thiết bị đã đăng ký token
            var pushData = new Dictionary<string, string>
            {
                ["type"] = request.Type.ToString(),
                ["incidentId"] = request.IncidentId?.ToString() ?? string.Empty
            };
            foreach (var notification in notifications)
            {
                var token = users.First(u => u.Id == notification.UserId).FcmToken;
                if (string.IsNullOrWhiteSpace(token)) continue;

                pushData["notificationId"] = notification.Id.ToString();
                var result = await _pushSender.SendAsync(token, request.Title, request.Message, pushData, request.HighPriority, cancellationToken);
                notification.FcmSentSuccess = result.Success;
                notification.FcmMessageId = result.MessageId;
            }

            await _notificationRepository.AddRangeAsync(notifications, cancellationToken);
            await _notificationRepository.SaveChangesAsync(cancellationToken);

            foreach (var notification in notifications)
            {
                await _realtimeNotifier.NotificationCreatedAsync(notification.UserId, Map(notification), cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Không gửi được thông báo '{Title}'", request.Title);
        }
    }

    private static NotificationDto Map(Notification n) => new()
    {
        Id = n.Id,
        Type = n.Type,
        Title = n.Title,
        Message = n.Message,
        IncidentId = n.IncidentId,
        DataJson = n.DataJson,
        IsRead = n.IsRead,
        ReadAt = n.ReadAt,
        CreatedAt = n.CreatedAt
    };
}
