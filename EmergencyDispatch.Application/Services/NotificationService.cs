using System.Text.Json;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Notification;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Localization;
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

    public async Task<IReadOnlyList<Guid>> GetCrewUserIdsAsync(Guid stationId, Guid? teamId, CancellationToken cancellationToken = default)
    {
        var stationUsers = await _userRepository.GetByStationWithTeamAsync(stationId) ?? new List<User>();
        return CrewScope.CrewOf(stationUsers, teamId).Select(u => u.Id).ToList();
    }

    public async Task NotifyCrewAsync(Guid stationId, Guid? teamId, NotificationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var stationUsers = await _userRepository.GetByStationWithTeamAsync(stationId) ?? new List<User>();
            await SendAsync(CrewScope.CrewOf(stationUsers, teamId).ToList(), request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Không gửi được thông báo cho tổ {TeamId} (trạm {StationId})", teamId, stationId);
        }
    }

    public async Task<PaginatedResultDto<NotificationDto>> GetMyNotificationsAsync(
        Guid userId, bool unreadOnly, int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (items, total) = await _notificationRepository.GetByUserAsync(userId, unreadOnly, pageIndex, pageSize, cancellationToken);
        var english = LocalizationHelper.IsEnglish;
        return new PaginatedResultDto<NotificationDto>(items.Select(n => Map(n, english)), total, pageIndex, pageSize);
    }

    public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default)
        => _notificationRepository.CountUnreadAsync(userId, cancellationToken);

    public async Task<bool> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default)
    {
        var marked = await _notificationRepository.MarkReadAsync(userId, notificationId, DateTime.UtcNow, cancellationToken);
        if (marked)
        {
            await PublishReadAsync(userId, new List<Guid> { notificationId }, false, cancellationToken);
        }
        return marked;
    }

    public async Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var count = await _notificationRepository.MarkAllReadAsync(userId, DateTime.UtcNow, cancellationToken);
        if (count > 0)
        {
            await PublishReadAsync(userId, new List<Guid>(), true, cancellationToken);
        }
        return count;
    }

    /// <summary>
    /// Đồng bộ số thông báo chưa đọc sang các thiết bị khác của cùng người dùng
    /// </summary>
    private async Task PublishReadAsync(Guid userId, List<Guid> ids, bool all, CancellationToken cancellationToken)
    {
        await _realtimeNotifier.NotificationReadAsync(userId, new EmergencyDispatch.Application.DTOs.Incident.NotificationReadEventDto
        {
            NotificationIds = ids,
            All = all,
            UnreadCount = await GetUnreadCountAsync(userId, cancellationToken)
        }, cancellationToken);
    }

    private async Task SendAsync(IReadOnlyCollection<User> users, NotificationRequest request, CancellationToken cancellationToken)
    {
        if (users.Count == 0) return;

        try
        {
            var dataJson = request.Data == null ? null : JsonSerializer.Serialize(request.Data);
            // Dựng cả hai ngôn ngữ một lần; mỗi người nhận xem / nhận push theo ngôn ngữ giao diện của mình
            var titleVi = request.Title.Vi;
            var messageVi = request.Message.Vi;
            var titleEn = request.Title.En;
            var messageEn = request.Message.En;
            var notifications = users.Select(u => new Notification
            {
                Id = Guid.NewGuid(),
                UserId = u.Id,
                IncidentId = request.IncidentId,
                Type = request.Type,
                Title = titleVi,
                Message = messageVi,
                TitleEn = titleEn,
                MessageEn = messageEn,
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
                var recipient = users.First(u => u.Id == notification.UserId);
                var token = recipient.FcmToken;
                if (string.IsNullOrWhiteSpace(token)) continue;

                pushData["notificationId"] = notification.Id.ToString();
                var english = IsEnglishUser(recipient);
                var result = await _pushSender.SendAsync(token,
                    english ? titleEn : titleVi, english ? messageEn : messageVi,
                    pushData, request.HighPriority, cancellationToken);
                notification.FcmSentSuccess = result.Success;
                notification.FcmMessageId = result.MessageId;
                if (result.TokenInvalid && recipient.FcmToken == token)
                {
                    await ClearDeadTokenAsync(recipient.Id, token);
                }
            }

            await _notificationRepository.AddRangeAsync(notifications, cancellationToken);
            await _notificationRepository.SaveChangesAsync(cancellationToken);

            foreach (var notification in notifications)
            {
                var english = IsEnglishUser(users.First(u => u.Id == notification.UserId));
                await _realtimeNotifier.NotificationCreatedAsync(notification.UserId, Map(notification, english), cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Không gửi được thông báo '{Title}'", request.Title.Vi);
        }
    }

    /// <summary>
    /// Xóa token FCM đã chết (đọc lại user để không ghi đè token mới nếu thiết bị vừa đăng ký lại)
    /// </summary>
    private async Task ClearDeadTokenAsync(Guid userId, string deadToken)
    {
        try
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null || user.FcmToken != deadToken) return;
            user.FcmToken = null;
            user.UpdatedAt = DateTime.UtcNow;
            await _userRepository.UpdateAsync(user);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Không xóa được FCM token hết hiệu lực của {UserId}", userId);
        }
    }

    private static bool IsEnglishUser(User user)
        => user.UiLanguage?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// english = true → bản tiếng Anh nếu có (thông báo cũ chưa có bản tiếng Anh thì giữ tiếng Việt)
    /// </summary>
    private static NotificationDto Map(Notification n, bool english) => new()
    {
        Id = n.Id,
        Type = n.Type,
        Title = english && !string.IsNullOrEmpty(n.TitleEn) ? n.TitleEn : n.Title,
        Message = english && !string.IsNullOrEmpty(n.MessageEn) ? n.MessageEn : n.Message,
        IncidentId = n.IncidentId,
        DataJson = n.DataJson,
        IsRead = n.IsRead,
        ReadAt = n.ReadAt,
        CreatedAt = n.CreatedAt
    };
}
