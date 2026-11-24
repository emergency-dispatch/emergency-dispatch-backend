using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using EmergencyDispatch.API.Controllers;
using EmergencyDispatch.API.Hubs;
using EmergencyDispatch.API.Realtime;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using EmergencyDispatch.Domain.Models;
using EmergencyDispatch.Domain.Policies;
using EmergencyDispatch.Infrastructure.BackgroundJobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EmergencyDispatch.Tests.Application;

public class CitizenMapTests
{
    private static readonly DateTime Now = new(2026, 11, 24, 0, 0, 0, DateTimeKind.Utc);
    private static Incident Human() => new()
    {
        Status = IncidentStatus.Verified, Severity = SeverityLevel.Level2, EventType = IncidentEventType.TrafficAccident,
        VerifiedByUserId = Guid.NewGuid(), VerifiedAt = Now, Latitude = 10.84, Longitude = 106.80
    };
    private static Incident Ai(UserRole actor = UserRole.RescueStaff)
    {
        var i = Human(); i.VerifiedByUserId = null; i.AutoDispatchState = AutoDispatchState.Dispatched;
        i.DispatchAssignments.Add(new()
        {
            AcceptedAt = Now, Status = DispatchAssignmentStatus.Accepted,
            StatusHistories = new List<AssignmentStatusHistory>
            {
                new() { NewStatus = DispatchAssignmentStatus.Accepted, ChangedByUserId = Guid.NewGuid(), ChangedByUser = new User { Role = actor } }
            }
        });
        return i;
    }
    private static bool Visible(Incident i) => CitizenIncidentVisibility.ConfirmedActiveSince(CitizenIncidentVisibility.AllTimeUtc).Compile()(i);

    [Fact]
    public void OperatorVerifiedWithoutAiOrAssignmentsIsVisible() => Assert.True(Visible(Human()));

    [Theory]
    [InlineData(DispatchAssignmentStatus.Accepted)]
    [InlineData(DispatchAssignmentStatus.EnRoute)]
    [InlineData(DispatchAssignmentStatus.OnScene)]
    public void AiNeedsStaffAcceptance(DispatchAssignmentStatus status)
    {
        var i = Ai(); i.DispatchAssignments.Single().Status = status;
        Assert.True(Visible(i));
    }

    [Theory]
    [InlineData(UserRole.Operator)]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Citizen)]
    public void AiAcceptanceByOtherRolesIsNotEnough(UserRole role) => Assert.False(Visible(Ai(role)));

    [Theory]
    [InlineData(IncidentStatus.Pending)]
    [InlineData(IncidentStatus.AiProcessing)]
    [InlineData(IncidentStatus.AiProcessed)]
    [InlineData(IncidentStatus.Completed)]
    [InlineData(IncidentStatus.Cancelled)]
    [InlineData(IncidentStatus.Linked)]
    public void UnconfirmedOrEndedStatusIsHidden(IncidentStatus status)
    {
        var i = Human(); i.Status = status; Assert.False(Visible(i));
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("duplicate")]
    [InlineData("level0")]
    [InlineData("none")]
    [InlineData("unverified")]
    public void OtherPrivateOrInvalidIncidentsAreHidden(string reason)
    {
        var i = Human();
        if (reason == "deleted") i.IsDeleted = true;
        if (reason == "duplicate") i.DuplicateOfIncidentId = Guid.NewGuid();
        if (reason == "level0") i.Severity = SeverityLevel.Unclassified;
        if (reason == "none") i.EventType = IncidentEventType.None;
        if (reason == "unverified") i.VerifiedAt = null;
        Assert.False(Visible(i));
    }

    [Theory]
    [InlineData("not-dispatched")]
    [InlineData("not-accepted")]
    [InlineData("manual")]
    [InlineData("no-history")]
    [InlineData("cancelled")]
    [InlineData("deleted")]
    public void AiConfidenceOrDispatchAloneDoesNotPublish(string reason)
    {
        var i = Ai(); var a = i.DispatchAssignments.Single();
        if (reason == "not-dispatched") i.AutoDispatchState = AutoDispatchState.NotEligible;
        if (reason == "not-accepted") a.AcceptedAt = null;
        if (reason == "manual") a.AssignedByUserId = Guid.NewGuid();
        if (reason == "no-history") a.StatusHistories.Clear();
        if (reason == "cancelled") a.Status = DispatchAssignmentStatus.Cancelled;
        if (reason == "deleted") a.IsDeleted = true;
        Assert.False(Visible(i));
    }

    [Fact]
    public void MapAndNotificationConfirmationRulesAgree()
    {
        var items = new[] { Human(), Ai(), Ai(UserRole.Operator), new Incident(), Human(), Ai() };
        items[4].Status = IncidentStatus.Completed; items[5].DispatchAssignments.Single().Status = DispatchAssignmentStatus.Rejected;
        foreach (var i in items)
            Assert.Equal(CitizenIncidentAlertPolicy.Trigger(i, CitizenIncidentVisibility.AllTimeUtc) != null, Visible(i));
    }

    private sealed class Fixture
    {
        public readonly Mock<ICitizenMapRepository> Repo = new();
        public readonly Mock<IUserRepository> Users = new();
        public readonly User Citizen = new() { Role = UserRole.Citizen, Status = UserStatus.Active };
        public readonly CitizenMapIncident Row = new(Guid.NewGuid(), 10.84, 106.8,
            "<b>Road D1</b> 0901234567 https://example.invalid/private", IncidentEventType.TrafficAccident,
            SeverityLevel.Level2, IncidentStatus.InProgress, Now, null);
        public readonly CitizenMapService Service;
        public Fixture()
        {
            Users.Setup(u => u.GetByIdAsync(Citizen.Id)).ReturnsAsync(Citizen);
            Repo.Setup(r => r.GetAsync(It.IsAny<CitizenMapBounds?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((new[] { Row }, 1));
            Repo.Setup(r => r.GetAsync(Row.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Row);
            Service = new CitizenMapService(Repo.Object, Users.Object);
        }
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("suspended")]
    [InlineData("deleted")]
    [InlineData("staff")]
    [InlineData("operator")]
    [InlineData("admin")]
    [InlineData("unknown")]
    public async Task OnlyActiveCitizensMayReadMapOrSubscribe(string reason)
    {
        var f = new Fixture();
        if (reason == "inactive") f.Citizen.Status = UserStatus.Inactive;
        if (reason == "suspended") f.Citizen.Status = UserStatus.Suspended;
        if (reason == "deleted") f.Citizen.IsDeleted = true;
        if (reason == "staff") f.Citizen.Role = UserRole.RescueStaff;
        if (reason == "operator") f.Citizen.Role = UserRole.Operator;
        if (reason == "admin") f.Citizen.Role = UserRole.Admin;
        var userId = reason == "unknown" ? Guid.NewGuid() : f.Citizen.Id;
        Assert.False(await f.Service.CanAccessAsync(userId));
        Assert.Equal("FORBIDDEN", (await f.Service.GetAsync(userId, new CitizenMapQueryDto())).ErrorCode);
        Assert.Equal("FORBIDDEN", (await f.Service.GetAsync(userId, f.Row.Id)).ErrorCode);
        Assert.Equal("FORBIDDEN", (await f.Service.GetByAlertAsync(userId, Guid.NewGuid())).ErrorCode);
        f.Repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MarkerExposesOnlyApprovedFieldsAndSanitizedArea()
    {
        var f = new Fixture(); var result = await f.Service.GetAsync(f.Citizen.Id, new CitizenMapQueryDto());
        var row = Assert.Single(result.Data!.Items); Assert.Equal("Road D1", row.Area);
        Assert.DoesNotContain("0901234567", row.Message); Assert.DoesNotContain("private", row.Message);
        Assert.Equal(f.Row.Latitude, row.Latitude); Assert.Equal(f.Row.Longitude, row.Longitude);
        var fields = JsonSerializer.SerializeToElement(row).EnumerateObject().Select(p => p.Name).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "Area", "EventType", "Id", "Latitude", "Longitude", "Message", "ReportedAt", "Severity", "Status", "UpdatedAt" }, fields);
        f.Repo.Verify(r => r.GetAsync(null, 1, 100, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkerMessageSupportsEnglish()
    {
        var culture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            var f = new Fixture(); var result = await f.Service.GetAsync(f.Citizen.Id, f.Row.Id);
            Assert.Equal("An incident has been reported at Road D1.", result.Data!.Message);
        }
        finally { CultureInfo.CurrentUICulture = culture; }
    }

    [Fact]
    public async Task NonPublicOrEndedMarkerReturnsNotFound()
    {
        var f = new Fixture(); Assert.Equal("NOT_FOUND", (await f.Service.GetAsync(f.Citizen.Id, Guid.NewGuid())).ErrorCode);
    }

    [Fact]
    public async Task ExistingNotificationCanLocateItsCurrentPublicMarkerWithoutOpeningPrivateDetail()
    {
        var f = new Fixture(); var alertId = Guid.NewGuid();
        f.Repo.Setup(r => r.GetByAlertAsync(alertId, It.IsAny<CancellationToken>())).ReturnsAsync(f.Row);
        var result = await f.Service.GetByAlertAsync(f.Citizen.Id, alertId);
        Assert.Equal(f.Row.Id, result.Data!.Id); Assert.Equal("Road D1", result.Data.Area);
        Assert.Equal("NOT_FOUND", (await f.Service.GetByAlertAsync(f.Citizen.Id, Guid.NewGuid())).ErrorCode);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 0)]
    [InlineData(1, 501)]
    [InlineData(int.MaxValue, 500)]
    public async Task InvalidPagingDoesNotQueryTheDatabase(int page, int size)
    {
        var f = new Fixture(); Assert.False((await f.Service.GetAsync(f.Citizen.Id, new CitizenMapQueryDto { PageIndex = page, PageSize = size })).Success);
        f.Repo.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(10d, null, null, null)]
    [InlineData(-91d, 0d, 20d, 10d)]
    [InlineData(10d, 0d, 91d, 10d)]
    [InlineData(20d, 0d, 10d, 10d)]
    [InlineData(10d, -181d, 20d, 10d)]
    [InlineData(10d, 0d, 20d, 181d)]
    [InlineData(double.NaN, 0d, 20d, 10d)]
    [InlineData(10d, 0d, double.PositiveInfinity, 10d)]
    public async Task InvalidBoundsDoNotQueryTheDatabase(double? south, double? west, double? north, double? east)
    {
        var f = new Fixture(); Assert.False((await f.Service.GetAsync(f.Citizen.Id,
            new CitizenMapQueryDto { South = south, West = west, North = north, East = east })).Success);
        f.Repo.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(-90, -180, 90, 180)]
    [InlineData(10, 106, 11, 107)]
    [InlineData(-10, 170, 10, -170)]
    public async Task BoundsIncludingDateLineArePassedToRepository(double south, double west, double north, double east)
    {
        var f = new Fixture(); Assert.True((await f.Service.GetAsync(f.Citizen.Id,
            new CitizenMapQueryDto { South = south, West = west, North = north, East = east, PageIndex = 2, PageSize = 500 })).Success);
        f.Repo.Verify(r => r.GetAsync(new CitizenMapBounds(south, west, north, east), 2, 500, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SignalRRefreshGoesOnlyToMapSubscribersAndContainsNoIncidentData()
    {
        var hub = new Mock<IHubContext<IncidentHub>>(); var clients = new Mock<IHubClients>(); var proxy = new Mock<IClientProxy>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);
        clients.Setup(c => c.Group(RealtimeContract.CitizenMapGroup)).Returns(proxy.Object);
        var change = new CitizenMapChangedDto("revision", Now);
        await new SignalRCitizenMapNotifier(hub.Object).ChangedAsync(change);
        proxy.Verify(p => p.SendCoreAsync(RealtimeContract.ReceiveCitizenMapChanged,
            It.Is<object?[]>(args => args.Length == 1 && Equals(args[0], change)), It.IsAny<CancellationToken>()), Times.Once);
        clients.Verify(c => c.Group(RealtimeContract.CitizenMapGroup), Times.Once); clients.VerifyNoOtherCalls();
        Assert.Equal(new[] { "ChangedAt", "Revision" }, JsonSerializer.SerializeToElement(change).EnumerateObject().Select(p => p.Name).OrderBy(x => x));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HubSubscriptionRequiresCurrentAccountAuthorization(bool allowed)
    {
        var userId = Guid.NewGuid(); var map = new Mock<ICitizenMapService>(); var context = new Mock<HubCallerContext>();
        var groups = new Mock<IGroupManager>();
        context.SetupGet(c => c.ConnectionId).Returns("map-client");
        context.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "test")));
        map.Setup(m => m.CanAccessAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(allowed);
        var hub = new IncidentHub(Mock.Of<IIncidentService>(), NullLogger<IncidentHub>.Instance, map.Object) { Context = context.Object, Groups = groups.Object };
        if (allowed)
        {
            await hub.SubscribeCitizenMap();
            groups.Verify(g => g.AddToGroupAsync("map-client", RealtimeContract.CitizenMapGroup, It.IsAny<CancellationToken>()), Times.Once);
            await hub.UnsubscribeCitizenMap();
            groups.Verify(g => g.RemoveFromGroupAsync("map-client", RealtimeContract.CitizenMapGroup, It.IsAny<CancellationToken>()), Times.Once);
        }
        else { await Assert.ThrowsAsync<HubException>(() => hub.SubscribeCitizenMap()); groups.VerifyNoOtherCalls(); }
    }

    [Fact]
    public void HttpAndHubEntryPointsRequireCitizenRole()
    {
        Assert.Equal("Citizen", Assert.Single(typeof(CitizenMapController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
        foreach (var method in new[] { nameof(IncidentHub.SubscribeCitizenMap), nameof(IncidentHub.UnsubscribeCitizenMap) })
            Assert.Equal("Citizen", Assert.Single(typeof(IncidentHub).GetMethod(method)!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
    }

    [Fact]
    public async Task ControllerMapsForbiddenAndNotFoundResponses()
    {
        var f = new Fixture(); var controller = new CitizenMapController(f.Service)
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, f.Citizen.Id.ToString()) }, "test")) } }
        };
        Assert.IsType<OkObjectResult>(await controller.Get(new CitizenMapQueryDto(), default));
        Assert.IsType<NotFoundObjectResult>(await controller.GetOne(Guid.NewGuid(), default));
        Assert.IsType<NotFoundObjectResult>(await controller.GetByAlert(Guid.NewGuid(), default));
        f.Citizen.Status = UserStatus.Suspended;
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.Get(new CitizenMapQueryDto(), default)).StatusCode);
    }

    private sealed class FastClock : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => System.CreateTimer(callback, state, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20));
    }

    [Fact]
    public async Task WorkerSendsOnlyChangedSnapshotsIncludingRemovalToEmptyMap()
    {
        var repo = new Mock<ICitizenMapRepository>(); var notifier = new Mock<ICitizenMapNotifier>();
        var revisions = new[] { "first", "first", "changed", "empty" }; var reads = 0;
        repo.Setup(r => r.GetRevisionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => revisions[Math.Min(Interlocked.Increment(ref reads) - 1, revisions.Length - 1)]);
        var sent = new List<string>(); var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        notifier.Setup(n => n.ChangedAsync(It.IsAny<CitizenMapChangedDto>(), It.IsAny<CancellationToken>()))
            .Callback((CitizenMapChangedDto e, CancellationToken _) => { sent.Add(e.Revision); if (sent.Count == 3) done.TrySetResult(); })
            .Returns(Task.CompletedTask);
        using var provider = new ServiceCollection().AddSingleton(repo.Object).AddSingleton(notifier.Object).BuildServiceProvider();
        using var worker = new CitizenMapWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<CitizenMapWorker>.Instance, new FastClock());
        await worker.StartAsync(default);
        try { await done.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { await worker.StopAsync(default); }
        Assert.Equal(new[] { "first", "changed", "empty" }, sent);
    }

    [Fact]
    public async Task WorkerRetriesSameRevisionWhenSignalRSendFails()
    {
        var repo = new Mock<ICitizenMapRepository>(); var notifier = new Mock<ICitizenMapNotifier>();
        repo.Setup(r => r.GetRevisionAsync(It.IsAny<CancellationToken>())).ReturnsAsync("unchanged");
        var calls = 0; var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        notifier.Setup(n => n.ChangedAsync(It.IsAny<CitizenMapChangedDto>(), It.IsAny<CancellationToken>()))
            .Returns((CitizenMapChangedDto change, CancellationToken _) =>
            {
                Assert.Equal("unchanged", change.Revision);
                if (Interlocked.Increment(ref calls) == 1) throw new IOException("Temporary SignalR failure");
                done.TrySetResult(); return Task.CompletedTask;
            });
        using var provider = new ServiceCollection().AddSingleton(repo.Object).AddSingleton(notifier.Object).BuildServiceProvider();
        using var worker = new CitizenMapWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<CitizenMapWorker>.Instance, new FastClock());
        await worker.StartAsync(default);
        try { await done.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { await worker.StopAsync(default); }
        Assert.Equal(2, calls);
    }
}
