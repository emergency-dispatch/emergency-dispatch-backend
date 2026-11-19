using System.Linq.Expressions;
using EmergencyDispatch.Application.DTOs.Incident;
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

public class StationTransferTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 3, 0, 0, DateTimeKind.Utc);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }

    private sealed class Fixture
    {
        public readonly Mock<IIncidentRepository> Incidents = new();
        public readonly Mock<IIncidentStationTransferRepository> Transfers = new();
        public readonly Mock<IStationRepository> Stations = new();
        public readonly Mock<IUserRepository> Users = new();
        public readonly Mock<ISystemSettingsService> Settings = new();
        public readonly Mock<IRealtimeNotifier> Realtime = new();
        public readonly Mock<INotificationService> Notifications = new();
        public readonly Mock<IRoutingService> Routing = new();
        public readonly Mock<ICurrentUserContext> Context = new();
        public readonly Station A = new() { Name = "A", Latitude = 10, Longitude = 106 };
        public readonly Station B = new() { Name = "B", Latitude = 10.1, Longitude = 106 };
        public readonly Station C = new() { Name = "C", Latitude = 10.2, Longitude = 106 };
        public readonly List<User> Operators = new();
        public readonly User Admin = new() { Role = UserRole.Admin, Status = UserStatus.Active };
        public readonly List<IncidentStationTransfer> History = new();
        public readonly StationTransferSettingsDto Config = new() { Enabled = true };
        public Incident Incident;
        public StationTransferService Service;
        public bool Committed;

        public Fixture()
        {
            Incident = new Incident { Title = "SOS", Status = IncidentStatus.Pending, ResponsibleStationId = A.Id,
                CreatedAt = Now.AddHours(-1), Latitude = 10, Longitude = 106, StationAssignedAt = Now.AddMinutes(-11),
                StationResponseDueAt = Now.AddMinutes(-1), StationAssignmentVersion = 1, StationTransferChainId = Guid.NewGuid() };
            Operators.AddRange(new[] { Operator(A), Operator(B), Operator(C) });
            Incidents.Setup(r => r.GetIncidentWithDetailsAsync(Incident.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => Incident);
            var transaction = new Mock<IIncidentLockScope>();
            transaction.Setup(t => t.CommitAsync(It.IsAny<CancellationToken>())).Callback(() => Committed = true).Returns(Task.CompletedTask);
            Incidents.Setup(r => r.BeginLockedTransactionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(transaction.Object);
            Stations.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Guid id) => new[] { A, B, C }.FirstOrDefault(s => s.Id == id));
            Stations.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<Station, bool>>>(), It.IsAny<Func<IQueryable<Station>, IQueryable<Station>>?>()))
                .ReturnsAsync(() => new[] { A, B, C });
            Users.Setup(r => r.GetActiveByRolesAsync(It.IsAny<IReadOnlyCollection<UserRole>>()))
                .ReturnsAsync((IReadOnlyCollection<UserRole> roles) => roles.Contains(UserRole.Operator) ? Operators : new List<User> { Admin });
            Users.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Guid id) => Operators.Append(Admin).FirstOrDefault(u => u.Id == id));
            Settings.Setup(s => s.GetStationTransferSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Config);
            Settings.Setup(s => s.GetEscalationSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new EscalationSettingsDto());
            Transfers.Setup(r => r.GetCandidateIdsAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => new[] { Incident.Id });
            Transfers.Setup(r => r.GetHistoryAsync(Incident.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => History);
            Transfers.Setup(r => r.AddAsync(It.IsAny<IncidentStationTransfer>(), It.IsAny<CancellationToken>()))
                .Callback((IncidentStationTransfer h, CancellationToken _) => History.Add(h)).Returns(Task.CompletedTask);
            Routing.Setup(r => r.GetLegsToAsync(It.IsAny<IReadOnlyList<(double Lat, double Lng)>>(), 10, 106, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RoadLeg?[] { new(8, 10), new(4, 15) });
            Context.SetupGet(c => c.Role).Returns(UserRole.Admin);
            Realtime.Setup(r => r.IncidentTransferredAsync(It.IsAny<IncidentTransferredDto>(), It.IsAny<IncidentResponseDto>(), It.IsAny<CancellationToken>()))
                .Callback(() => Assert.True(Committed)).Returns(Task.CompletedTask);
            Service = new(Incidents.Object, Transfers.Object, Stations.Object, Users.Object, Settings.Object,
                Routing.Object, Context.Object, Realtime.Object, Notifications.Object, NullLogger<StationTransferService>.Instance, new Clock());
        }
        public static User Operator(Station station) => new() { Role = UserRole.Operator, StationId = station.Id, Status = UserStatus.Active };
    }

    [Fact]
    public async Task Expired_ChoosesShortestRoad_AndPreservesIncidentAndEvidence()
    {
        var f = new Fixture();
        f.Incident.MediaItems.Add(new IncidentMedia { MediaUrl = "photo.jpg" });
        var id = f.Incident.Id; var created = f.Incident.CreatedAt;
        Assert.Equal(1, await f.Service.ProcessPendingAsync());
        Assert.Equal(f.C.Id, f.Incident.ResponsibleStationId); // C is farther by coordinates, shorter by road.
        Assert.Equal(id, f.Incident.Id); Assert.Equal(created, f.Incident.CreatedAt);
        Assert.Single(f.Incident.MediaItems); Assert.Equal(IncidentStatus.Pending, f.Incident.Status);
        var h = Assert.Single(f.History);
        Assert.Equal("NoResponse", h.ReasonCode); Assert.Equal("RoadDistance", h.SelectionMethod); Assert.Equal(4, h.SelectionDistanceKm);
        Assert.Equal(2, f.Incident.StationAssignmentVersion); Assert.Equal(1, f.Incident.AutomaticStationTransferCount);
        Assert.Equal(Now.AddMinutes(10), f.Incident.StationResponseDueAt);
        f.Transfers.Verify(r => r.MoveLinkedReportsAsync(id, f.C.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false, "NoOperator")]
    [InlineData(true, "StationUnavailable")]
    public async Task CapacityFailure_DoesNotWaitForDeadline(bool unavailable, string reason)
    {
        var f = new Fixture(); f.Incident.StationResponseDueAt = Now.AddHours(1);
        if (unavailable) f.A.IsActive = false; else f.Operators.RemoveAll(u => u.StationId == f.A.Id);
        Assert.Equal(1, await f.Service.ProcessPendingAsync());
        Assert.Equal(reason, Assert.Single(f.History).ReasonCode);
    }

    [Theory]
    [InlineData(DispatchAssignmentStatus.Dispatched)]
    [InlineData(DispatchAssignmentStatus.Accepted)]
    [InlineData(DispatchAssignmentStatus.EnRoute)]
    [InlineData(DispatchAssignmentStatus.OnScene)]
    [InlineData(DispatchAssignmentStatus.Completed)]
    public async Task NeverAutomaticallyTransfersAnyDispatchedOrCompletedMission(DispatchAssignmentStatus status)
    {
        var f = new Fixture(); f.A.IsActive = false;
        f.Incident.DispatchAssignments.Add(new DispatchAssignment { Status = status });
        Assert.Equal(0, await f.Service.ProcessPendingAsync()); Assert.Empty(f.History);
    }

    [Theory]
    [InlineData(IncidentStatus.AiProcessing)]
    [InlineData(IncidentStatus.Linked)]
    [InlineData(IncidentStatus.Completed)]
    [InlineData(IncidentStatus.Cancelled)]
    public async Task DoesNotMoveClosedLinkedOrAnalysingIncident(IncidentStatus status)
    {
        var f = new Fixture(); f.Incident.Status = status;
        Assert.Equal(0, await f.Service.ProcessPendingAsync()); Assert.Empty(f.History);
    }

    [Fact]
    public async Task AcknowledgedIncidentStays_UnlessStationBecomesUnavailable()
    {
        var f = new Fixture(); f.Incident.StationAcknowledgedAt = Now.AddMinutes(-1);
        Assert.Equal(0, await f.Service.ProcessPendingAsync());
        f.A.IsActive = false;
        Assert.Equal(1, await f.Service.ProcessPendingAsync());
        Assert.NotNull(Assert.Single(f.History).PreviousAcknowledgedAt);
        Assert.Null(f.Incident.StationAcknowledgedAt);
    }

    [Fact]
    public async Task LiveSoftLock_PreventsTimeoutTransfer()
    {
        var f = new Fixture(); f.Incident.LockedByOperatorId = f.Operators[0].Id; f.Incident.LockExpiresAt = Now.AddMinutes(1);
        Assert.Equal(0, await f.Service.ProcessPendingAsync()); Assert.Empty(f.History);
    }

    [Fact]
    public async Task ConcurrentAcknowledgementWhileRouting_CancelsAutomaticTransfer()
    {
        var f = new Fixture();
        f.Routing.Setup(r => r.GetLegsToAsync(It.IsAny<IReadOnlyList<(double Lat, double Lng)>>(), 10, 106, It.IsAny<CancellationToken>()))
            .Callback(() => StationTransferLifecycle.Acknowledge(f.Incident, f.Operators[0].Id, Now))
            .ReturnsAsync(new RoadLeg?[] { new(1, 2), new(2, 3) });
        Assert.Equal(0, await f.Service.ProcessPendingAsync()); Assert.Empty(f.History);
    }

    [Fact]
    public async Task ConcurrentDispatchWhileRouting_CancelsAutomaticTransfer()
    {
        var f = new Fixture();
        f.Routing.Setup(r => r.GetLegsToAsync(It.IsAny<IReadOnlyList<(double Lat, double Lng)>>(), 10, 106, It.IsAny<CancellationToken>()))
            .Callback(() => f.Incident.DispatchAssignments.Add(new DispatchAssignment { Status = DispatchAssignmentStatus.Dispatched }))
            .ReturnsAsync(new RoadLeg?[] { new(1, 2), new(2, 3) });
        Assert.Equal(0, await f.Service.ProcessPendingAsync()); Assert.Empty(f.History);
    }

    [Fact]
    public async Task ConcurrentManualTransferWhileRouting_DoesNotOverwriteNewStation()
    {
        var f = new Fixture();
        f.Routing.Setup(r => r.GetLegsToAsync(It.IsAny<IReadOnlyList<(double Lat, double Lng)>>(), 10, 106, It.IsAny<CancellationToken>()))
            .Callback(() => { f.Incident.ResponsibleStationId = f.B.Id; f.Incident.StationAssignmentVersion++; })
            .ReturnsAsync(new RoadLeg?[] { new(1, 2), new(2, 3) });
        Assert.Equal(0, await f.Service.ProcessPendingAsync()); Assert.Empty(f.History); Assert.Equal(f.B.Id, f.Incident.ResponsibleStationId);
    }

    [Fact]
    public async Task SettingsDisabledDuringRouting_PreventsTransfer()
    {
        var f = new Fixture();
        f.Routing.Setup(r => r.GetLegsToAsync(It.IsAny<IReadOnlyList<(double Lat, double Lng)>>(), 10, 106, It.IsAny<CancellationToken>()))
            .Callback(() => f.Config.Enabled = false).ReturnsAsync(new RoadLeg?[] { new(1, 2), new(2, 3) });
        Assert.Equal(0, await f.Service.ProcessPendingAsync()); Assert.Empty(f.History);
    }

    [Fact]
    public async Task NoTarget_AlertsOnlyOnce_AndCanRecoverWhenOperatorReturns()
    {
        var f = new Fixture(); var targetOperator = f.Operators[1]; f.Operators.RemoveRange(1, 2);
        Assert.Equal(0, await f.Service.ProcessPendingAsync());
        Assert.Equal(0, await f.Service.ProcessPendingAsync());
        Assert.Equal("NoTarget", f.Incident.StationTransferAlertCode);
        f.Notifications.Verify(n => n.NotifyRolesAsync(It.IsAny<IReadOnlyCollection<UserRole>>(), It.IsAny<EmergencyDispatch.Application.DTOs.Notification.NotificationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        f.Operators.Add(targetOperator);
        Assert.Equal(1, await f.Service.ProcessPendingAsync()); Assert.Null(f.Incident.StationTransferAlertCode);
    }

    [Fact]
    public async Task TransferLimit_IsEnforced_WithoutRepeatedAlerts()
    {
        var f = new Fixture(); f.Incident.AutomaticStationTransferCount = 2;
        Assert.Equal(0, await f.Service.ProcessPendingAsync()); Assert.Empty(f.History);
        Assert.Equal("TransferLimit", f.Incident.StationTransferAlertCode);
        f.Routing.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NeverReturnsToPreviouslyVisitedStationInSameChain()
    {
        var f = new Fixture(); f.Incident.ResponsibleStationId = f.B.Id;
        f.History.Add(new() { IncidentId = f.Incident.Id, FromStationId = f.A.Id, ToStationId = f.B.Id,
            IsAutomatic = true, ChainId = f.Incident.StationTransferChainId!.Value, AssignmentVersion = 2 });
        Assert.Equal(1, await f.Service.ProcessPendingAsync()); Assert.Equal(f.C.Id, f.Incident.ResponsibleStationId);
        f.Routing.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LegacyIncidentIsNotEnrolledByBackgroundScan()
    {
        var f = new Fixture(); f.Incident.StationAssignedAt = null;
        Assert.Equal(0, await f.Service.ProcessPendingAsync()); Assert.Empty(f.History);
    }

    [Fact]
    public async Task DisabledFeatureDoesNotScanOrTransfer()
    {
        var f = new Fixture(); f.Config.Enabled = false;
        Assert.Equal(0, await f.Service.ProcessPendingAsync()); f.Transfers.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExplicitAcknowledgement_IsIdempotent_AndChecksStationAndVersion()
    {
        var f = new Fixture();
        var denied = await f.Service.AcknowledgeAsync(f.Incident.Id, f.Operators[1].Id, 1);
        Assert.Equal("FORBIDDEN", denied.ErrorCode);
        Assert.Equal("CONFLICT", (await f.Service.AcknowledgeAsync(f.Incident.Id, f.Operators[0].Id, 0)).ErrorCode);
        Assert.True((await f.Service.AcknowledgeAsync(f.Incident.Id, f.Operators[0].Id, 1)).Success);
        Assert.True((await f.Service.AcknowledgeAsync(f.Incident.Id, f.Admin.Id, 1)).Success);
        Assert.Equal(f.Operators[0].Id, f.Incident.StationAcknowledgedByUserId);
        f.Incidents.Verify(r => r.AddAuditTrailAsync(It.Is<IncidentAuditTrail>(a => a.Action == AuditAction.StationAcknowledged), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ManualHandover_ResetsChainAndAcknowledgement_PreservesMissions()
    {
        var f = new Fixture(); var chain = f.Incident.StationTransferChainId;
        f.Incident.StationAcknowledgedAt = Now.AddMinutes(-1); f.Incident.AutomaticStationTransferCount = 2;
        f.Incident.Status = IncidentStatus.InProgress;
        var assignment = new DispatchAssignment { Status = DispatchAssignmentStatus.OnScene };
        f.Incident.DispatchAssignments.Add(assignment);
        var dto = new TransferIncidentDto { StationId = f.B.Id, Reason = "Station is unavailable", ExpectedAssignmentVersion = 1 };
        Assert.Equal("CONFLICT", (await f.Service.TransferAsync(f.Incident.Id, dto, f.Admin.Id)).ErrorCode);
        dto.ConfirmActiveAssignments = true;
        Assert.Equal("CONFLICT", (await f.Service.TransferAsync(f.Incident.Id, dto, f.Operators[0].Id)).ErrorCode);
        Assert.True((await f.Service.TransferAsync(f.Incident.Id, dto, f.Admin.Id)).Success);
        Assert.Equal(DispatchAssignmentStatus.OnScene, assignment.Status);
        Assert.Equal(IncidentStatus.InProgress, f.Incident.Status);
        Assert.NotEqual(chain, f.Incident.StationTransferChainId);
        Assert.Equal(0, f.Incident.AutomaticStationTransferCount); Assert.Null(f.Incident.StationAcknowledgedAt);
        Assert.False(Assert.Single(f.History).IsAutomatic);
    }

    [Fact]
    public async Task ManualTransferRejectsTargetWithoutOperatorAndStaleVersion()
    {
        var f = new Fixture(); f.Operators.RemoveAll(u => u.StationId == f.B.Id);
        var dto = new TransferIncidentDto { StationId = f.B.Id, Reason = "Manual transfer", ExpectedAssignmentVersion = 1 };
        Assert.False((await f.Service.TransferAsync(f.Incident.Id, dto, f.Admin.Id)).Success);
        dto.ExpectedAssignmentVersion = 0;
        Assert.Equal("CONFLICT", (await f.Service.TransferAsync(f.Incident.Id, dto, f.Admin.Id)).ErrorCode);
        Assert.Empty(f.History);
    }

    [Fact]
    public async Task AiTimeoutIsArmedOnlyAfterAnalysisAndDispatchAttempt()
    {
        var f = new Fixture(); f.Incident.StationAssignedAt = null; f.Incident.StationResponseDueAt = null;
        f.Incident.Status = IncidentStatus.AiProcessing;
        await f.Service.InitializeAsync(f.Incident, false);
        Assert.Null(f.Incident.StationResponseDueAt);
        await f.Service.MarkReadyForOperatorAsync(f.Incident.Id); Assert.Null(f.Incident.StationResponseDueAt);
        f.Incident.Status = IncidentStatus.AiProcessed;
        await f.Service.MarkReadyForOperatorAsync(f.Incident.Id); Assert.Equal(Now.AddMinutes(10), f.Incident.StationResponseDueAt);
    }

    [Fact]
    public void DayAndNightUseConfiguredEscalationMultiplier()
    {
        Assert.Equal(Now.AddMinutes(10), StationTransferLifecycle.Deadline(Now, new(), new()));
        var night = new DateTime(2026, 11, 18, 16, 0, 0, DateTimeKind.Utc); // 23:00 VN
        Assert.Equal(night.AddMinutes(5), StationTransferLifecycle.Deadline(night, new(), new()));
    }

    [Theory]
    [InlineData(double.NaN, 2)]
    [InlineData(double.PositiveInfinity, 2)]
    [InlineData(0.9, 2)]
    [InlineData(11, 2)]
    [InlineData(2, 0)]
    [InlineData(2, 6)]
    public void InvalidSettingsRejected(double multiplier, int max)
        => Assert.NotEmpty(SystemSettingsService.ValidateStationTransfer(new() { ResponseTimeoutMultiplier = multiplier, MaxAutomaticTransfers = max }));

    [Fact]
    public void AllNewMessagesHaveEnglishTranslations()
    {
        foreach (var field in typeof(StationTransferMessages).GetFields())
        {
            var message = Assert.IsType<string>(field.GetValue(null));
            Assert.NotEqual(message, LocalizationHelper.LocalizeForLanguage(message, "en"));
        }
    }
}
