using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.DTOs.Notification;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EmergencyDispatch.Tests.Application;

public class NotificationServiceTests
{
    private readonly Mock<INotificationRepository> _repo = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPushNotificationSender> _push = new();
    private readonly Mock<IRealtimeNotifier> _rt = new();
    private readonly List<Notification> _saved = new();
    private readonly NotificationService _svc;

    public NotificationServiceTests()
    {
        _repo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<Notification>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<Notification>, CancellationToken>((n, _) => _saved.AddRange(n));
        _push.Setup(p => p.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PushSendResult { Success = true, MessageId = "m1" });
        _svc = new NotificationService(_repo.Object, _users.Object, _push.Object, _rt.Object, NullLogger<NotificationService>.Instance);
    }

    private User U(UserRole role = UserRole.Operator, UserStatus status = UserStatus.Active, string? fcm = "tok")
    {
        var u = new User { Role = role, Status = status, FcmToken = fcm };
        _users.Setup(x => x.GetByIdAsync(u.Id)).ReturnsAsync(u);
        return u;
    }

    private static NotificationRequest Req(bool high = true) => new()
    {
        Type = NotificationType.Escalation, Title = "T", Message = "M", IncidentId = Guid.NewGuid(), HighPriority = high, Data = new { a = 1 }
    };

    [Fact]
    public async Task NotifyUsers_SavesOnePerActiveUser_PushesAndBroadcasts()
    {
        var a = U();
        var b = U(fcm: null);
        var suspended = U(status: UserStatus.Suspended);
        var req = Req();

        await _svc.NotifyUsersAsync(new[] { a.Id, a.Id, b.Id, suspended.Id, Guid.NewGuid() }, req);

        Assert.Equal(new[] { a.Id, b.Id }.OrderBy(x => x), _saved.Select(n => n.UserId).OrderBy(x => x));
        Assert.All(_saved, n => Assert.Equal("{\"a\":1}", n.DataJson));
        _push.Verify(p => p.SendAsync("tok", "T", "M", It.Is<IReadOnlyDictionary<string, string>>(d => d["type"] == "Escalation" && d["incidentId"] == req.IncidentId.ToString()),
            true, It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(_saved.Single(n => n.UserId == a.Id).FcmSentSuccess);
        Assert.Equal("m1", _saved.Single(n => n.UserId == a.Id).FcmMessageId);
        _rt.Verify(r => r.NotificationCreatedAsync(It.IsAny<Guid>(), It.IsAny<NotificationDto>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task NotifyUsers_NoRecipients_DoesNothing()
    {
        await _svc.NotifyUsersAsync(new[] { Guid.NewGuid() }, Req());

        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NotifyUsers_PushFailure_DoesNotThrow_AndRecordsFailure()
    {
        var a = U();
        _push.Setup(p => p.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PushSendResult { Success = false, Error = "invalid token" });

        await _svc.NotifyUsersAsync(new[] { a.Id }, Req());

        Assert.False(_saved.Single().FcmSentSuccess);
    }

    [Fact]
    public async Task NotifyUsers_RepositoryError_IsSwallowed()
    {
        var a = U();
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

        var ex = await Record.ExceptionAsync(() => _svc.NotifyUsersAsync(new[] { a.Id }, Req()));

        Assert.Null(ex);
    }

    [Fact]
    public async Task NotifyRoles_ErrorFromRepository_IsSwallowed()
    {
        _users.Setup(u => u.GetActiveByRolesAsync(It.IsAny<IReadOnlyCollection<UserRole>>())).ThrowsAsync(new InvalidOperationException());

        Assert.Null(await Record.ExceptionAsync(() => _svc.NotifyRolesAsync(new[] { UserRole.Admin }, Req())));
    }

    [Fact]
    public async Task NotifyStationStaff_OnlyActiveRescueStaff()
    {
        var station = Guid.NewGuid();
        var staff = new User { Role = UserRole.RescueStaff, Status = UserStatus.Active };
        var op = new User { Role = UserRole.Operator, Status = UserStatus.Active };
        var inactive = new User { Role = UserRole.RescueStaff, Status = UserStatus.Suspended };
        _users.Setup(u => u.GetByStationWithTeamAsync(station)).ReturnsAsync(new List<User> { staff, op, inactive });

        await _svc.NotifyStationStaffAsync(station, Req());

        Assert.Equal(staff.Id, _saved.Single().UserId);
    }

    [Fact]
    public async Task MarkRead_PublishesUnreadCountOnlyWhenChanged()
    {
        var user = Guid.NewGuid();
        var id = Guid.NewGuid();
        _repo.Setup(r => r.MarkReadAsync(user, id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repo.Setup(r => r.CountUnreadAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync(4);

        Assert.True(await _svc.MarkReadAsync(user, id));
        Assert.False(await _svc.MarkReadAsync(user, Guid.NewGuid()));

        _rt.Verify(r => r.NotificationReadAsync(user, It.Is<NotificationReadEventDto>(e => e.UnreadCount == 4 && !e.All && e.NotificationIds.Single() == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkAllRead_PublishesAllFlag()
    {
        var user = Guid.NewGuid();
        _repo.Setup(r => r.MarkAllReadAsync(user, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(3);

        Assert.Equal(3, await _svc.MarkAllReadAsync(user));
        _rt.Verify(r => r.NotificationReadAsync(user, It.Is<NotificationReadEventDto>(e => e.All), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMyNotifications_ClampsPageSize()
    {
        var user = Guid.NewGuid();
        _repo.Setup(r => r.GetByUserAsync(user, true, 1, 100, It.IsAny<CancellationToken>())).ReturnsAsync((new List<Notification>(), 0));

        await _svc.GetMyNotificationsAsync(user, true, 1, 1000);

        _repo.Verify(r => r.GetByUserAsync(user, true, 1, 100, It.IsAny<CancellationToken>()), Times.Once);
    }
}
