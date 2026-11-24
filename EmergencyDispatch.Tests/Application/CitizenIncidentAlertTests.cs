using System.Text.Json;
using EmergencyDispatch.Application.DTOs.Notification;
using EmergencyDispatch.Application.DTOs.Settings;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Localization;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EmergencyDispatch.Tests.Application;

public class CitizenIncidentAlertTests
{
    private static readonly DateTime Now = new(2026, 11, 23, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Since = Now.AddMinutes(-10);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }

    private static Incident Incident(bool human = false) => new()
    {
        Status = human ? IncidentStatus.Verified : IncidentStatus.InProgress,
        EventType = IncidentEventType.TrafficAccident, Severity = SeverityLevel.Level2,
        LocationAddress = "Đường D1, Khu Công nghệ cao", Latitude = 10.84, Longitude = 106.80,
        VerifiedAt = human ? Now.AddMinutes(-1) : null, VerifiedByUserId = human ? Guid.NewGuid() : null,
        Title = "PRIVATE TITLE", Description = "PRIVATE DESCRIPTION", ReporterName = "PRIVATE REPORTER", ReporterPhone = "0901234567"
    };

    private static DispatchAssignment Acceptance(UserRole actor = UserRole.RescueStaff) => new()
    {
        Status = DispatchAssignmentStatus.Accepted, AcceptedAt = Now.AddMinutes(-1), AssignedByUserId = null,
        StatusHistories = new List<AssignmentStatusHistory>
        {
            new() { NewStatus = DispatchAssignmentStatus.Accepted, ChangedByUserId = Guid.NewGuid(), ChangedByUser = new User { Role = actor } }
        }
    };

    private static Incident AiAccepted()
    {
        var incident = Incident(); incident.AutoDispatchState = AutoDispatchState.Dispatched;
        incident.DispatchAssignments.Add(Acceptance()); return incident;
    }

    [Fact]
    public void OperatorConfirmationDoesNotRequireAiOrStaffAcceptance()
    {
        var i = Incident(true);
        Assert.Null(i.AiClassification); Assert.Empty(i.DispatchAssignments);
        Assert.Equal("OperatorVerified", CitizenIncidentAlertPolicy.Trigger(i, Since)?.Kind);
    }

    [Fact]
    public void HighAiConfidenceAloneDoesNotPublish()
    {
        var i = Incident(); i.Status = IncidentStatus.AiProcessed;
        i.AiClassification = new() { IsSuccess = true, ConfidenceScore = 0.999, ConfidenceSource = "logprob" };
        Assert.Null(CitizenIncidentAlertPolicy.Trigger(i, Since));
    }

    [Fact]
    public void AiDispatchedWithoutStaffAcceptanceDoesNotPublish()
    {
        var i = Incident(); i.Status = IncidentStatus.Dispatched; i.AutoDispatchState = AutoDispatchState.Dispatched;
        i.DispatchAssignments.Add(new() { Status = DispatchAssignmentStatus.Dispatched });
        Assert.Null(CitizenIncidentAlertPolicy.Trigger(i, Since));
    }

    [Theory]
    [InlineData(DispatchAssignmentStatus.Accepted)]
    [InlineData(DispatchAssignmentStatus.EnRoute)]
    [InlineData(DispatchAssignmentStatus.OnScene)]
    public void AiAssignedAndStaffAcceptedAllowsAnnouncement(DispatchAssignmentStatus status)
    {
        var i = AiAccepted(); i.DispatchAssignments.First().Status = status;
        Assert.Equal("AiDispatchedStaffAccepted", CitizenIncidentAlertPolicy.Trigger(i, Since)?.Kind);
    }

    [Theory]
    [InlineData(UserRole.Operator)]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Citizen)]
    public void AcceptanceBySomeoneOtherThanStaffDoesNotSatisfyAiBranch(UserRole actor)
    {
        var i = Incident(); i.AutoDispatchState = AutoDispatchState.Dispatched; i.DispatchAssignments.Add(Acceptance(actor));
        Assert.Null(CitizenIncidentAlertPolicy.Trigger(i, Since));
    }

    [Theory]
    [InlineData(IncidentStatus.Pending)]
    [InlineData(IncidentStatus.AiProcessing)]
    [InlineData(IncidentStatus.AiProcessed)]
    [InlineData(IncidentStatus.Cancelled)]
    [InlineData(IncidentStatus.Completed)]
    [InlineData(IncidentStatus.Linked)]
    public void IneligibleIncidentStatusesNeverPublish(IncidentStatus status)
    {
        var i = AiAccepted(); i.Status = status;
        Assert.Null(CitizenIncidentAlertPolicy.Trigger(i, Since));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("deleted")]
    [InlineData("level0")]
    [InlineData("none")]
    [InlineData("old-acceptance")]
    [InlineData("no-acceptance-history")]
    [InlineData("manual-assignment")]
    [InlineData("rejected")]
    [InlineData("cancelled-assignment")]
    public void NonConfirmedOrStaleEvidenceIsExcluded(string reason)
    {
        var i = AiAccepted(); var a = i.DispatchAssignments.First();
        if (reason == "duplicate") i.DuplicateOfIncidentId = Guid.NewGuid();
        if (reason == "deleted") i.IsDeleted = true;
        if (reason == "level0") i.Severity = SeverityLevel.Unclassified;
        if (reason == "none") i.EventType = IncidentEventType.None;
        if (reason == "old-acceptance") a.AcceptedAt = Since.AddSeconds(-1);
        if (reason == "no-acceptance-history") a.StatusHistories.Clear();
        if (reason == "manual-assignment") a.AssignedByUserId = Guid.NewGuid();
        if (reason == "rejected") a.Status = DispatchAssignmentStatus.Rejected;
        if (reason == "cancelled-assignment") a.Status = DispatchAssignmentStatus.Cancelled;
        Assert.Null(CitizenIncidentAlertPolicy.Trigger(i, Since));
    }

    [Fact]
    public void DoesNotBackfillOldHumanConfirmationsWhenEnabling()
    {
        var i = Incident(true); i.VerifiedAt = Since.AddSeconds(-1);
        Assert.Null(CitizenIncidentAlertPolicy.Trigger(i, Since));
    }

    [Fact]
    public void AreaIsSanitizedAndDoesNotUsePrivateDescription()
    {
        var i = Incident(true); i.LocationAddress = "<b>Đường D1</b> 0901234567 contact@example.test https://example.test/a";
        var area = CitizenIncidentAlertPolicy.Area(i);
        Assert.Equal("Đường D1", area.Vi); Assert.Equal(area.Vi, area.En);
        i.LocationAddress = " ";
        Assert.Contains("10.840", CitizenIncidentAlertPolicy.Area(i).Vi);
        Assert.DoesNotContain("PRIVATE", CitizenIncidentAlertPolicy.Area(i).Vi);
    }

    private sealed class Fixture
    {
        public readonly Mock<ICitizenIncidentAlertRepository> Alerts = new();
        public readonly Mock<IIncidentRepository> Incidents = new();
        public readonly Mock<IUserRepository> Users = new();
        public readonly Mock<ISystemSettingsService> Settings = new();
        public readonly Mock<IPushNotificationSender> Push = new();
        public readonly Mock<IRealtimeNotifier> Realtime = new();
        public readonly CitizenIncidentAlertSettingsDto Config = new() { Enabled = true, EnabledFromUtc = Since };
        public readonly User Citizen = new() { Role = UserRole.Citizen, Status = UserStatus.Active, UiLanguage = "vi", FcmToken = "test-device-token" };
        public Incident Incident = CitizenIncidentAlertTests.Incident(true);
        public CitizenIncidentAlert? Published;
        public List<Notification> Inbox = new();
        public List<CitizenIncidentAlertDelivery> Deliveries = new();
        public bool Committed;
        public CitizenIncidentAlertService Service;

        public Fixture()
        {
            Settings.Setup(s => s.GetCitizenIncidentAlertSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Config);
            Alerts.Setup(a => a.GetCandidateIdsAsync(Since, 100, It.IsAny<CancellationToken>())).ReturnsAsync(() => new[] { Incident.Id });
            Alerts.Setup(a => a.GetIncidentSnapshotAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Incident);
            Alerts.Setup(a => a.GetByIncidentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Published);
            Alerts.Setup(a => a.AddAsync(It.IsAny<CitizenIncidentAlert>(), It.IsAny<IReadOnlyList<Notification>>(), It.IsAny<IReadOnlyList<CitizenIncidentAlertDelivery>>(), It.IsAny<CancellationToken>()))
                .Callback((CitizenIncidentAlert alert, IReadOnlyList<Notification> inbox, IReadOnlyList<CitizenIncidentAlertDelivery> deliveries, CancellationToken _) =>
                { Published = alert; Inbox = inbox.ToList(); Deliveries = deliveries.ToList(); }).Returns(Task.CompletedTask);
            Users.Setup(u => u.GetActiveByRolesAsync(It.IsAny<IReadOnlyCollection<UserRole>>())).ReturnsAsync(new[] { Citizen });
            Users.Setup(u => u.GetByIdAsync(Citizen.Id)).ReturnsAsync(Citizen);
            var transaction = new Mock<IIncidentLockScope>();
            transaction.Setup(t => t.CommitAsync(It.IsAny<CancellationToken>())).Callback(() => Committed = true).Returns(Task.CompletedTask);
            Incidents.Setup(i => i.BeginLockedTransactionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(transaction.Object);
            Push.Setup(p => p.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PushSendResult { Success = true, MessageId = "provider-message" });
            Service = new(Alerts.Object, Incidents.Object, Users.Object, Settings.Object, Push.Object, Realtime.Object,
                NullLogger<CitizenIncidentAlertService>.Instance, new Clock());
        }

        public async Task<CitizenIncidentAlertDelivery> PrepareDelivery()
        {
            await Service.PublishPendingAsync();
            var delivery = Assert.Single(Deliveries); delivery.LeaseId = Guid.NewGuid(); delivery.Attempts = 1;
            Alerts.Setup(a => a.GetAlertAsync(delivery.AlertId, It.IsAny<CancellationToken>())).ReturnsAsync(() => Published);
            Alerts.Setup(a => a.GetNotificationAsync(delivery.NotificationId, It.IsAny<CancellationToken>())).ReturnsAsync(() => Inbox.Single());
            Alerts.SetupSequence(a => a.TryClaimAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(delivery).ReturnsAsync((CitizenIncidentAlertDelivery?)null);
            return delivery;
        }
    }

    [Fact]
    public async Task PublishesOnceAndCommitsInboxBeforeAnyPush()
    {
        var f = new Fixture();
        Assert.Equal(1, await f.Service.PublishPendingAsync()); Assert.True(f.Committed);
        Assert.Equal(0, await f.Service.PublishPendingAsync());
        var n = Assert.Single(f.Inbox);
        Assert.Equal("Có sự cố tại Đường D1, Khu Công nghệ cao.", n.Message);
        Assert.Equal("An incident has been reported at Đường D1, Khu Công nghệ cao.", n.MessageEn);
        Assert.Null(n.IncidentId); Assert.DoesNotContain("PRIVATE", n.Message); Assert.DoesNotContain("0901234567", n.Message);
        Assert.DoesNotContain("incidentId", n.DataJson!); Assert.Contains("publicAlertId", n.DataJson!);
        Assert.Equal(n.Id, Assert.Single(f.Deliveries).NotificationId);
        f.Push.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AiAndHumanConfirmationStillProduceOnlyOneAnnouncement()
    {
        var f = new Fixture { Incident = AiAccepted() };
        Assert.Equal(1, await f.Service.PublishPendingAsync());
        f.Incident.VerifiedByUserId = Guid.NewGuid(); f.Incident.VerifiedAt = Now;
        Assert.Equal(0, await f.Service.PublishPendingAsync()); Assert.Single(f.Deliveries);
    }

    [Fact]
    public async Task OnlyActiveNonDeletedCitizensReceiveTheAnnouncement()
    {
        var f = new Fixture();
        f.Users.Setup(u => u.GetActiveByRolesAsync(It.IsAny<IReadOnlyCollection<UserRole>>())).ReturnsAsync(new[]
        {
            f.Citizen, f.Citizen, new User { Role = UserRole.Citizen, Status = UserStatus.Inactive },
            new User { Role = UserRole.Citizen, Status = UserStatus.Suspended }, new User { Role = UserRole.Citizen, IsDeleted = true },
            new User { Role = UserRole.Admin }, new User { Role = UserRole.Operator }, new User { Role = UserRole.RescueStaff }
        });
        await f.Service.PublishPendingAsync();
        Assert.Equal(f.Citizen.Id, Assert.Single(f.Inbox).UserId);
    }

    [Fact]
    public async Task CandidateCancelledBeforeIncidentLockDoesNotPublish()
    {
        var f = new Fixture();
        f.Incidents.Setup(i => i.BeginLockedTransactionAsync(f.Incident.Id, It.IsAny<CancellationToken>()))
            .Callback(() => f.Incident.Status = IncidentStatus.Cancelled).ReturnsAsync((IIncidentLockScope)null!);
        Assert.Equal(0, await f.Service.PublishPendingAsync()); Assert.Empty(f.Inbox);
    }

    [Fact]
    public async Task DisabledFeatureDoesNotScanOrDeliver()
    {
        var f = new Fixture(); f.Config.Enabled = false;
        Assert.Equal(0, await f.Service.PublishPendingAsync()); Assert.Equal(0, await f.Service.DeliverPendingAsync());
        f.Alerts.VerifyNoOtherCalls(); f.Push.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PushIsAreaOnlyBilingualAndDoesNotUseTheStaffSiren()
    {
        var f = new Fixture(); var delivery = await f.PrepareDelivery(); f.Citizen.UiLanguage = "en";
        Assert.Equal(1, await f.Service.DeliverPendingAsync());
        f.Push.Verify(p => p.SendAsync("test-device-token", "INCIDENT NOTICE", "An incident has been reported at Đường D1, Khu Công nghệ cao.",
            It.Is<IReadOnlyDictionary<string, string>>(d => d["notificationId"] == delivery.NotificationId.ToString()
                && d["publicAlertId"] == delivery.AlertId.ToString() && !d.ContainsKey("incidentId")), false, It.IsAny<CancellationToken>()), Times.Once);
        f.Alerts.Verify(a => a.FinishAttemptAsync(delivery.Id, delivery.LeaseId!.Value, Now, true, true, true, "provider-message", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoDeviceTokenKeepsInboxAndUsesRealtimeWithoutFailing()
    {
        var f = new Fixture(); var delivery = await f.PrepareDelivery(); f.Citizen.FcmToken = null;
        Assert.Equal(1, await f.Service.DeliverPendingAsync()); Assert.Single(f.Inbox);
        f.Push.VerifyNoOtherCalls();
        f.Realtime.Verify(r => r.NotificationCreatedAsync(f.Citizen.Id, It.IsAny<NotificationDto>(), It.IsAny<CancellationToken>()), Times.Once);
        f.Alerts.Verify(a => a.FinishAttemptAsync(delivery.Id, delivery.LeaseId!.Value, Now, true, true, null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TemporaryPushFailureIsStoredForRetry()
    {
        var f = new Fixture(); var delivery = await f.PrepareDelivery();
        f.Push.Setup(p => p.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PushSendResult { Error = "TEMPORARY_PROVIDER_FAILURE" });
        await f.Service.DeliverPendingAsync();
        f.Alerts.Verify(a => a.FinishAttemptAsync(delivery.Id, delivery.LeaseId!.Value, Now, false, true, false, null, "TEMPORARY_PROVIDER_FAILURE", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(f.Inbox);
    }

    [Fact]
    public async Task RealtimeFailureDoesNotBlockPush()
    {
        var f = new Fixture(); var delivery = await f.PrepareDelivery();
        f.Realtime.Setup(r => r.NotificationCreatedAsync(It.IsAny<Guid>(), It.IsAny<NotificationDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Realtime unavailable"));
        await f.Service.DeliverPendingAsync();
        f.Push.Verify(p => p.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), false, It.IsAny<CancellationToken>()), Times.Once);
        f.Alerts.Verify(a => a.FinishAttemptAsync(delivery.Id, delivery.LeaseId!.Value, Now, true, false, true, "provider-message", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RealtimeFailureWithoutTokenSchedulesRetry()
    {
        var f = new Fixture(); var delivery = await f.PrepareDelivery(); f.Citizen.FcmToken = null;
        f.Realtime.Setup(r => r.NotificationCreatedAsync(It.IsAny<Guid>(), It.IsAny<NotificationDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Realtime unavailable"));
        await f.Service.DeliverPendingAsync();
        f.Push.VerifyNoOtherCalls(); Assert.Single(f.Inbox);
        f.Alerts.Verify(a => a.FinishAttemptAsync(delivery.Id, delivery.LeaseId!.Value, Now, false, false, null, null, "Temporary realtime delivery failure.", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidDeviceTokenIsClearedWithoutRepeatedPushRetries()
    {
        var f = new Fixture(); var delivery = await f.PrepareDelivery();
        f.Push.Setup(p => p.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PushSendResult { TokenInvalid = true, Error = "INVALID_DEVICE_TOKEN" });
        await f.Service.DeliverPendingAsync();
        f.Alerts.Verify(a => a.ClearInvalidTokenAsync(f.Citizen.Id, "test-device-token", It.IsAny<CancellationToken>()), Times.Once);
        f.Alerts.Verify(a => a.FinishAttemptAsync(delivery.Id, delivery.LeaseId!.Value, Now, true, true, false, null, "INVALID_DEVICE_TOKEN", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("suspended")]
    [InlineData("deleted")]
    [InlineData("not-citizen")]
    [InlineData("incident-cancelled")]
    public async Task EligibilityIsRecheckedBeforeSending(string reason)
    {
        var f = new Fixture(); await f.PrepareDelivery();
        if (reason == "suspended") f.Citizen.Status = UserStatus.Suspended;
        if (reason == "deleted") f.Citizen.IsDeleted = true;
        if (reason == "not-citizen") f.Citizen.Role = UserRole.RescueStaff;
        if (reason == "incident-cancelled") f.Incident.Status = IncidentStatus.Cancelled;
        await f.Service.DeliverPendingAsync(); f.Push.VerifyNoOtherCalls(); f.Realtime.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExistingRealtimeAttemptDoesNotSendAnotherRealtimeNotificationOnPushRetry()
    {
        var f = new Fixture(); var delivery = await f.PrepareDelivery(); delivery.RealtimeAttemptedAt = Now.AddMinutes(-1);
        await f.Service.DeliverPendingAsync();
        f.Realtime.VerifyNoOtherCalls();
        f.Push.Verify(p => p.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CitizenCannotReadAnotherRecipientsAnnouncement()
    {
        var f = new Fixture(); var other = Guid.NewGuid();
        var result = await f.Service.GetMineAsync(Guid.NewGuid(), other);
        Assert.Equal("NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task CitizenDtoContainsOnlyAreaMessageAndTime()
    {
        var f = new Fixture(); await f.Service.PublishPendingAsync();
        f.Alerts.Setup(a => a.GetForUserAsync(f.Published!.Id, f.Citizen.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => f.Published);
        var response = await f.Service.GetMineAsync(f.Published!.Id, f.Citizen.Id);
        Assert.True(response.Success);
        var names = JsonSerializer.SerializeToElement(response.Data).EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "Area", "CreatedAt", "Id", "Message" }, names);
    }
}
