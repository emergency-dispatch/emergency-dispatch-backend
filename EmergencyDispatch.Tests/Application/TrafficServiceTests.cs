using System.Linq.Expressions;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.DTOs.Notification;
using EmergencyDispatch.Application.DTOs.Traffic;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EmergencyDispatch.Tests.Application;

/// <summary>Traffic hotspots: report, assign a unit to clear congestion, unassign, resolve, expire.</summary>
public class TrafficServiceTests
{
    private readonly Mock<ITrafficHotspotRepository> _repo = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IRealtimeNotifier> _rt = new();
    private readonly Mock<IRescueUnitRepository> _units = new();
    private readonly Mock<IDispatchAssignmentRepository> _assign = new();
    private readonly Mock<IStationRepository> _stations = new();
    private readonly Mock<ICurrentUserContext> _me = new();
    private readonly Mock<ISystemAuditService> _audit = new();
    private readonly Mock<INotificationService> _notify = new();
    private readonly Station _q7 = new() { Name = "Q7", Latitude = 10.732842, Longitude = 106.719688, CoverageRadiusKm = 10, IsActive = true };
    private readonly Guid _opId = Guid.NewGuid();
    private readonly TrafficService _svc;

    public TrafficServiceTests()
    {
        _repo.Setup(r => r.AddAsync(It.IsAny<TrafficHotspot>())).ReturnsAsync((TrafficHotspot h) => h);
        _stations.Setup(s => s.GetAllAsync(It.IsAny<Expression<Func<Station, bool>>?>(), It.IsAny<Func<IQueryable<Station>, IQueryable<Station>>?>()))
            .ReturnsAsync(new[] { _q7 });
        _repo.Setup(r => r.TryMarkAssignedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _units.Setup(u => u.TryReserveForDispatchAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        AsOperator(_q7.Id);
        _svc = new TrafficService(_repo.Object, _users.Object, _rt.Object, NullLogger<TrafficService>.Instance, _units.Object,
            _assign.Object, _stations.Object, _me.Object, _audit.Object, _notify.Object);
    }

    private void AsOperator(Guid? station)
    {
        _me.SetupGet(m => m.Role).Returns(UserRole.Operator);
        _me.SetupGet(m => m.StationId).Returns(station);
    }

    private User Actor(UserRole role, Guid? station)
    {
        var u = new User { Role = role, StationId = station, FullName = role.ToString() };
        _users.Setup(x => x.GetByIdAsync(u.Id)).ReturnsAsync(u);
        return u;
    }

    private TrafficHotspot Hotspot(TrafficHotspotStatus status = TrafficHotspotStatus.Active, Guid? station = null, RescueUnit? unit = null, int expiresInMin = 30)
    {
        var h = new TrafficHotspot
        {
            Title = "Kẹt xe cổng D1", LocationAddress = "Cổng D1", Status = status, IsActive = status is TrafficHotspotStatus.Active or TrafficHotspotStatus.Assigned,
            ResponsibleStationId = station ?? _q7.Id, ExpiresAt = DateTime.UtcNow.AddMinutes(expiresInMin),
            AssignedRescueUnit = unit, AssignedRescueUnitId = unit?.Id
        };
        _repo.Setup(r => r.GetWithDetailsAsync(h.Id, It.IsAny<CancellationToken>())).ReturnsAsync(h);
        return h;
    }

    private RescueUnit Unit(RescueUnitStatus status = RescueUnitStatus.Available, Guid? station = null, RescueUnitType type = RescueUnitType.PolicePatrol)
    {
        var u = new RescueUnit { PlateNumber = "51A-TT1", UnitType = type, Status = status, StationId = station ?? _q7.Id };
        _units.Setup(x => x.GetByIdWithDetailsAsync(u.Id)).ReturnsAsync(u);
        return u;
    }

    // ===================== Report =====================
    private static ReportTrafficHotspotDto Dto(int ttl = 45) => new()
    {
        Title = " Kẹt xe ", LocationAddress = " Nguyễn Văn Linh ", Latitude = 10.7335, Longitude = 106.7192,
        CongestionLevel = TrafficCongestionLevel.Moderate, TtlMinutes = ttl
    };

    [Theory]
    [InlineData(1, 10)]
    [InlineData(45, 45)]
    [InlineData(999, 180)]
    public async Task Report_TtlIsClampedBetween10And180(int ttl, int expected)
    {
        TrafficHotspot? saved = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<TrafficHotspot>())).Callback<TrafficHotspot>(h => saved = h).ReturnsAsync((TrafficHotspot h) => h);

        await _svc.ReportHotspotAsync(Dto(ttl));

        Assert.InRange((saved!.ExpiresAt - saved.CreatedAt).TotalMinutes, expected - 0.01, expected + 0.01);
        Assert.Equal("Kẹt xe", saved.Title);
        Assert.Equal(TrafficHotspotStatus.Active, saved.Status);
    }

    [Fact]
    public async Task Report_AssignsNearestStation_AndBroadcasts()
    {
        var r = await _svc.ReportHotspotAsync(Dto());

        Assert.Equal(_q7.Id, r.Data!.ResponsibleStationId);
        Assert.Equal("Q7", r.Data.ResponsibleStationName);
        Assert.Equal("Người dân / Công nhân", r.Data.ReporterName);
        _rt.Verify(n => n.TrafficHotspotReportedAsync(It.IsAny<TrafficHotspotResponseDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Report_LoggedInUser_FillsNameAndPhoneFromProfile()
    {
        var u = new User { FullName = "Công nhân A", PhoneNumber = "0911111111" };
        _users.Setup(x => x.GetByIdAsync(u.Id)).ReturnsAsync(u);
        TrafficHotspot? saved = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<TrafficHotspot>())).Callback<TrafficHotspot>(h => saved = h).ReturnsAsync((TrafficHotspot h) => h);

        await _svc.ReportHotspotAsync(Dto(), u.Id);

        Assert.Equal("Công nhân A", saved!.ReporterName);
        Assert.Equal("0911111111", saved.ReporterPhone);
    }

    // ===================== Assign =====================
    [Fact]
    public async Task Assign_AvailableUnitOfOwnStation_ReservesUnitAndNotifiesStaff()
    {
        var h = Hotspot();
        var u = Unit();

        var r = await _svc.AssignUnitAsync(h.Id, new AssignTrafficHotspotDto { RescueUnitId = u.Id, Notes = "Phân luồng container" }, _opId);

        Assert.True(r.Success);
        Assert.Equal(TrafficHotspotStatus.Assigned, r.Data!.Status);
        Assert.Equal(u.Id, r.Data.AssignedRescueUnitId);
        Assert.Equal("51A-TT1", r.Data.AssignedUnitPlate);
        Assert.Equal(RescueUnitStatus.Dispatched, u.Status);
        _units.Verify(x => x.TryReserveForDispatchAsync(u.Id, It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(x => x.TryMarkAssignedAsync(h.Id, u.Id, _opId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _rt.Verify(n => n.TrafficTaskChangedAsync(It.Is<TrafficTaskEventDto>(e => e.Action == "Assigned" && e.UnitStationId == _q7.Id && e.Notes == "Phân luồng container"), It.IsAny<CancellationToken>()), Times.Once);
        _rt.Verify(n => n.RescueUnitStatusChangedAsync(It.Is<RescueUnitStatusEventDto>(e => e.Status == RescueUnitStatus.Dispatched), It.IsAny<CancellationToken>()), Times.Once);
        _notify.Verify(n => n.NotifyStationStaffAsync(_q7.Id, It.Is<NotificationRequest>(q => q.Type == NotificationType.TrafficTask && q.HighPriority), It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.LogAsync(SystemAuditAction.TrafficTaskAssigned, "TrafficHotspot", h.Id, It.IsAny<string?>(), It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(RescueUnitType.PolicePatrol)]
    [InlineData(RescueUnitType.Ambulance)]
    public async Task Assign_AnyAvailableUnitTypeIsAccepted(RescueUnitType type)
    {
        var h = Hotspot();

        Assert.True((await _svc.AssignUnitAsync(h.Id, new AssignTrafficHotspotDto { RescueUnitId = Unit(type: type).Id }, _opId)).Success);
    }

    [Fact]
    public async Task Assign_AlreadyAssigned_IsConflict()
    {
        var h = Hotspot(TrafficHotspotStatus.Assigned, unit: Unit(RescueUnitStatus.Dispatched));

        var r = await _svc.AssignUnitAsync(h.Id, new AssignTrafficHotspotDto { RescueUnitId = Unit().Id }, _opId);

        Assert.Equal(ApiErrorCodes.Conflict, r.ErrorCode);
        _units.Verify(x => x.TryReserveForDispatchAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(TrafficHotspotStatus.Resolved, 30)]
    [InlineData(TrafficHotspotStatus.Expired, 30)]
    [InlineData(TrafficHotspotStatus.Active, -1)]
    public async Task Assign_ResolvedExpiredOrPastTtl_Fails(TrafficHotspotStatus status, int expiresInMin)
    {
        var h = Hotspot(status, expiresInMin: expiresInMin);

        var r = await _svc.AssignUnitAsync(h.Id, new AssignTrafficHotspotDto { RescueUnitId = Unit().Id }, _opId);

        Assert.False(r.Success);
        _units.Verify(x => x.TryReserveForDispatchAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Assign_HotspotOfOtherStation_IsForbidden()
    {
        var h = Hotspot(station: Guid.NewGuid());

        Assert.Equal(ApiErrorCodes.Forbidden, (await _svc.AssignUnitAsync(h.Id, new AssignTrafficHotspotDto { RescueUnitId = Unit().Id }, _opId)).ErrorCode);
    }

    [Fact]
    public async Task Assign_UnitOfOtherStation_IsForbidden_ButAdminMayUseAnyStation()
    {
        var h = Hotspot();
        var foreign = Unit(station: Guid.NewGuid());

        Assert.Equal(ApiErrorCodes.Forbidden, (await _svc.AssignUnitAsync(h.Id, new AssignTrafficHotspotDto { RescueUnitId = foreign.Id }, _opId)).ErrorCode);

        _me.SetupGet(m => m.Role).Returns(UserRole.Admin);
        Assert.True((await _svc.AssignUnitAsync(h.Id, new AssignTrafficHotspotDto { RescueUnitId = foreign.Id }, _opId)).Success);
    }

    [Fact]
    public async Task Assign_UnknownHotspotOrUnit_IsNotFound()
    {
        var h = Hotspot();

        Assert.Equal(ApiErrorCodes.NotFound, (await _svc.AssignUnitAsync(Guid.NewGuid(), new AssignTrafficHotspotDto { RescueUnitId = Unit().Id }, _opId)).ErrorCode);
        Assert.Equal(ApiErrorCodes.NotFound, (await _svc.AssignUnitAsync(h.Id, new AssignTrafficHotspotDto { RescueUnitId = Guid.NewGuid() }, _opId)).ErrorCode);
    }

    [Theory]
    [InlineData(RescueUnitStatus.Dispatched)]
    [InlineData(RescueUnitStatus.CoolDown)]
    [InlineData(RescueUnitStatus.Maintenance)]
    public async Task Assign_UnitNotAvailable_IsConflict(RescueUnitStatus status)
    {
        var h = Hotspot();

        Assert.Equal(ApiErrorCodes.Conflict, (await _svc.AssignUnitAsync(h.Id, new AssignTrafficHotspotDto { RescueUnitId = Unit(status).Id }, _opId)).ErrorCode);
    }

    [Fact]
    public async Task Assign_UnitOnIncidentOrOtherHotspot_IsConflict()
    {
        var h = Hotspot();
        var onIncident = Unit();
        var onHotspot = Unit();
        _assign.Setup(a => a.GetActiveAssignmentByUnitIdAsync(onIncident.Id)).ReturnsAsync(new DispatchAssignment());
        _repo.Setup(r => r.IsUnitOnHotspotAsync(onHotspot.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Assert.Equal(ApiErrorCodes.Conflict, (await _svc.AssignUnitAsync(h.Id, new AssignTrafficHotspotDto { RescueUnitId = onIncident.Id }, _opId)).ErrorCode);
        Assert.Equal(ApiErrorCodes.Conflict, (await _svc.AssignUnitAsync(h.Id, new AssignTrafficHotspotDto { RescueUnitId = onHotspot.Id }, _opId)).ErrorCode);
    }

    [Fact]
    public async Task Assign_UnitReservedByAnotherDispatchMeanwhile_IsConflict()
    {
        var h = Hotspot();
        var u = Unit();
        _units.Setup(x => x.TryReserveForDispatchAsync(u.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        Assert.Equal(ApiErrorCodes.Conflict, (await _svc.AssignUnitAsync(h.Id, new AssignTrafficHotspotDto { RescueUnitId = u.Id }, _opId)).ErrorCode);
        _repo.Verify(x => x.TryMarkAssignedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Assign_HotspotChangedMeanwhile_ReleasesUnitAndReturnsConflict()
    {
        var h = Hotspot();
        var u = Unit();
        _repo.Setup(x => x.TryMarkAssignedAsync(h.Id, u.Id, _opId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var r = await _svc.AssignUnitAsync(h.Id, new AssignTrafficHotspotDto { RescueUnitId = u.Id }, _opId);

        Assert.Equal(ApiErrorCodes.Conflict, r.ErrorCode);
        _units.Verify(x => x.ReleaseReservationAsync(u.Id, It.IsAny<CancellationToken>()), Times.Once);
        _rt.Verify(n => n.TrafficTaskChangedAsync(It.IsAny<TrafficTaskEventDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ===================== Unassign =====================
    [Theory]
    [InlineData("")]
    [InlineData("  abc  ")]
    public async Task Unassign_ReasonShorterThanFive_Fails(string reason)
    {
        var h = Hotspot(TrafficHotspotStatus.Assigned, unit: Unit(RescueUnitStatus.Dispatched));

        Assert.False((await _svc.UnassignUnitAsync(h.Id, new UnassignTrafficHotspotDto { Reason = reason }, _opId)).Success);
        Assert.Equal(TrafficHotspotStatus.Assigned, h.Status);
    }

    [Fact]
    public async Task Unassign_ReleasesUnitAndReturnsHotspotToActive()
    {
        var u = Unit(RescueUnitStatus.Dispatched);
        var h = Hotspot(TrafficHotspotStatus.Assigned, unit: u);

        var r = await _svc.UnassignUnitAsync(h.Id, new UnassignTrafficHotspotDto { Reason = "Cần xe cho sự cố cháy" }, _opId);

        Assert.True(r.Success);
        Assert.Equal(TrafficHotspotStatus.Active, h.Status);
        Assert.Null(h.AssignedRescueUnitId);
        Assert.Equal(RescueUnitStatus.Available, u.Status);
        _units.Verify(x => x.ReleaseReservationAsync(u.Id, It.IsAny<CancellationToken>()), Times.Once);
        _rt.Verify(n => n.TrafficTaskChangedAsync(It.Is<TrafficTaskEventDto>(e => e.Action == "Unassigned"), It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.LogAsync(SystemAuditAction.TrafficTaskUnassigned, It.IsAny<string>(), h.Id, It.IsAny<string?>(), It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unassign_NotAssigned_Fails()
    {
        var h = Hotspot();

        Assert.False((await _svc.UnassignUnitAsync(h.Id, new UnassignTrafficHotspotDto { Reason = "lý do đủ dài" }, _opId)).Success);
        _units.Verify(x => x.ReleaseReservationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unassign_OtherStation_IsForbidden()
    {
        var h = Hotspot(TrafficHotspotStatus.Assigned, station: Guid.NewGuid(), unit: Unit(RescueUnitStatus.Dispatched));

        Assert.Equal(ApiErrorCodes.Forbidden, (await _svc.UnassignUnitAsync(h.Id, new UnassignTrafficHotspotDto { Reason = "lý do đủ dài" }, _opId)).ErrorCode);
    }

    // ===================== Resolve =====================
    [Fact]
    public async Task Resolve_AssignedByStaffOfUnitStation_ReleasesUnitAndSavesNote()
    {
        var u = Unit(RescueUnitStatus.Dispatched);
        var h = Hotspot(TrafficHotspotStatus.Assigned, unit: u);
        var staff = Actor(UserRole.RescueStaff, _q7.Id);

        var r = await _svc.ResolveHotspotAsync(h.Id, staff.Id, " Đã phân luồng xong ");

        Assert.True(r.Success);
        Assert.Equal(TrafficHotspotStatus.Resolved, h.Status);
        Assert.False(h.IsActive);
        Assert.Equal("Đã phân luồng xong", h.ResolutionNote);
        Assert.Equal(staff.Id, h.ResolvedByUserId);
        Assert.Equal(RescueUnitStatus.Available, u.Status);
        _units.Verify(x => x.ReleaseReservationAsync(u.Id, It.IsAny<CancellationToken>()), Times.Once);
        _rt.Verify(n => n.TrafficTaskChangedAsync(It.Is<TrafficTaskEventDto>(e => e.Action == "Resolved"), It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.LogAsync(SystemAuditAction.TrafficResolved, It.IsAny<string>(), h.Id, It.IsAny<string?>(), It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Resolve_AssignedByStaffOfOtherStation_IsForbidden()
    {
        var h = Hotspot(TrafficHotspotStatus.Assigned, unit: Unit(RescueUnitStatus.Dispatched));
        var outsider = Actor(UserRole.RescueStaff, Guid.NewGuid());

        Assert.Equal(ApiErrorCodes.Forbidden, (await _svc.ResolveHotspotAsync(h.Id, outsider.Id)).ErrorCode);
        Assert.Equal(TrafficHotspotStatus.Assigned, h.Status);
    }

    [Fact]
    public async Task Resolve_UnassignedHotspot_AnyStaffMayResolve_AsBefore()
    {
        var h = Hotspot();
        var staff = Actor(UserRole.RescueStaff, Guid.NewGuid());

        Assert.True((await _svc.ResolveHotspotAsync(h.Id, staff.Id)).Success);
        _units.Verify(x => x.ReleaseReservationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_OperatorOfOtherStation_IsForbidden()
    {
        var h = Hotspot(station: Guid.NewGuid());
        var op = Actor(UserRole.Operator, _q7.Id);

        Assert.Equal(ApiErrorCodes.Forbidden, (await _svc.ResolveHotspotAsync(h.Id, op.Id)).ErrorCode);
    }

    [Theory]
    [InlineData(TrafficHotspotStatus.Resolved, 30)]
    [InlineData(TrafficHotspotStatus.Active, -1)]
    public async Task Resolve_AlreadyClosedOrExpired_Fails(TrafficHotspotStatus status, int expiresInMin)
    {
        var h = Hotspot(status, expiresInMin: expiresInMin);

        Assert.False((await _svc.ResolveHotspotAsync(h.Id, Actor(UserRole.Operator, _q7.Id).Id)).Success);
    }

    [Fact]
    public async Task Resolve_AssignedPastTtl_IsStillAllowed()
    {
        var h = Hotspot(TrafficHotspotStatus.Assigned, unit: Unit(RescueUnitStatus.Dispatched), expiresInMin: -30);

        Assert.True((await _svc.ResolveHotspotAsync(h.Id, Actor(UserRole.Operator, _q7.Id).Id)).Success);
    }

    // ===================== Entity / cleanup =====================
    [Theory]
    [InlineData(TrafficHotspotStatus.Active, 5, true)]
    [InlineData(TrafficHotspotStatus.Active, -5, false)]
    [InlineData(TrafficHotspotStatus.Assigned, -5, true)]
    [InlineData(TrafficHotspotStatus.Resolved, 5, false)]
    [InlineData(TrafficHotspotStatus.Expired, 5, false)]
    public void IsOpenAt_AssignedHotspotsNeverExpire(TrafficHotspotStatus status, int expiresInMin, bool open)
    {
        var now = DateTime.UtcNow;

        Assert.Equal(open, new TrafficHotspot { Status = status, ExpiresAt = now.AddMinutes(expiresInMin) }.IsOpenAt(now));
    }

    [Fact]
    public void ResponseDto_AssignedHotspot_ShowsNoRemainingMinutes()
    {
        var dto = new TrafficHotspotResponseDto { Status = TrafficHotspotStatus.Assigned, ExpiresAt = DateTime.UtcNow.AddMinutes(20) };

        Assert.Equal(0, dto.RemainingMinutes);
    }

    [Fact]
    public async Task Cleanup_ReturnsRepositoryCount()
    {
        _repo.Setup(r => r.DeactivateExpiredHotspotsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(3);

        Assert.Equal(3, await _svc.CleanupExpiredHotspotsAsync());
    }

    [Fact]
    public async Task GetById_Unknown_IsNotFound()
    {
        Assert.Equal(ApiErrorCodes.NotFound, (await _svc.GetByIdAsync(Guid.NewGuid())).ErrorCode);
    }
}
