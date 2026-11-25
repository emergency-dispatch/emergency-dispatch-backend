using EmergencyDispatch.Application.DTOs.Notification;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Dispatch;
using EmergencyDispatch.Application.DTOs.Settings;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Exceptions;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EmergencyDispatch.Tests.Application;

public class DispatchServiceTests
{
    private readonly Mock<IDispatchAssignmentRepository> _assign = new();
    private readonly Mock<IRescueUnitRepository> _units = new();
    private readonly Mock<IIncidentRepository> _incidents = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IRealtimeNotifier> _rt = new();
    private readonly Mock<ISystemSettingsService> _settings = new();
    private readonly Mock<INotificationService> _notify = new();
    private readonly Mock<IRoutingService> _routing = new();
    private readonly Mock<ICurrentUserContext> _me = new();
    private readonly Mock<IEscalationRepository> _esc = new();
    private readonly Mock<IIncidentFollowUpService> _followUp = new();
    private readonly Mock<IFieldRequestService> _fieldRequests = new();
    private readonly Mock<IIncidentLockScope> _lock = new();
    private readonly Mock<IStationTeamRepository> _teams = new();
    private readonly List<StationTeam> _crews = new();
    private readonly List<RescueUnit> _fleet = new();
    private readonly List<DispatchAssignment> _active = new();
    private readonly Dictionary<Guid, Incident> _incidentById = new();
    private readonly Guid _stationA = Guid.NewGuid();
    private readonly Guid _stationB = Guid.NewGuid();
    private readonly Guid _operatorId = Guid.NewGuid();
    private readonly DispatchService _svc;

    // Incident at District 7 (10.7335, 106.7192)
    private const double Lat = 10.7335, Lng = 106.7192;

    public DispatchServiceTests()
    {
        _settings.Setup(s => s.GetDispatchSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new DispatchSettingsDto());
        _settings.Setup(s => s.GetEscalationSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new EscalationSettingsDto());
        _units.Setup(u => u.GetAllWithStationAsync()).ReturnsAsync(() => _fleet);
        _assign.Setup(a => a.GetAllActiveAsync()).ReturnsAsync(() => _active);
        _routing.Setup(r => r.GetLegsToAsync(It.IsAny<IReadOnlyList<(double, double)>>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<RoadLeg?>?)null);
        _units.Setup(u => u.TryReserveForDispatchAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _units.Setup(u => u.GetByIdWithDetailsAsync(It.IsAny<Guid>())).ReturnsAsync((Guid id) => _fleet.FirstOrDefault(f => f.Id == id));
        _users.Setup(u => u.GetActiveByRolesAsync(It.IsAny<IReadOnlyCollection<UserRole>>())).ReturnsAsync(new List<User>());
        _incidents.Setup(i => i.BeginLockedTransactionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(_lock.Object);
        _teams.Setup(t => t.GetByStationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<StationTeam>());
        // Giao theo tổ: số nhân viên cứu hộ đang hoạt động của các tổ trong _crews
        _teams.Setup(t => t.GetDispatchInfoAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) => _crews
                .Where(c => ids.Contains(c.Id))
                .Select(c => new TeamDispatchInfo(c.Id, c.Name, c.Members.Count(m => m.Staff is { Role: UserRole.RescueStaff, Status: UserStatus.Active, IsDeleted: false })))
                .ToList());
        // Đọc lại lượt điều phối "từ DB" khi xét đóng sự cố = danh sách lượt hiện có của sự cố
        _assign.Setup(a => a.GetFreshByIncidentIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _incidentById.TryGetValue(id, out var i)
                ? i.DispatchAssignments.ToList()
                : new List<DispatchAssignment>());
        // Bản xem (kể cả xe / tổ đã xóa mềm) dùng cùng dữ liệu với bản chi tiết trong test
        _assign.Setup(a => a.GetForViewAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((Guid id, CancellationToken _) => _assign.Object.GetByIdWithDetailsAsync(id));
        _assign.Setup(a => a.GetByIncidentForViewAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(async (Guid id, CancellationToken _) => await _assign.Object.GetByIncidentIdAsync(id));
        _me.SetupGet(m => m.Role).Returns(UserRole.Operator);
        _me.SetupGet(m => m.StationId).Returns(_stationA);
        _svc = new DispatchService(_assign.Object, _units.Object, _incidents.Object, _users.Object, _rt.Object, _settings.Object,
            _notify.Object, NullLogger<DispatchService>.Instance, _routing.Object, _me.Object, _esc.Object, _followUp.Object, _fieldRequests.Object,
            TestHelpers.Config(("Cloudinary:CloudName", "demo")), _teams.Object);
    }

    /// <summary>
    /// Xe mới; mặc định thuộc một tổ riêng có 1 nhân viên cứu hộ (sự cố giao theo tổ). teamed = false → xe chưa gán tổ
    /// </summary>
    private RescueUnit Unit(string type, double kmNorth, RescueUnitStatus status = RescueUnitStatus.Available, Guid? station = null, bool teamed = true)
    {
        var u = new RescueUnit
        {
            PlateNumber = $"51A-{_fleet.Count:000}",
            UnitType = type,
            Status = status,
            StationId = station ?? _stationA,
            CurrentLat = Lat + kmNorth / 111.2,
            CurrentLng = Lng
        };
        _fleet.Add(u);
        if (teamed) CrewOf(u, Staffer(u.StationId));
        return u;
    }

    private static User Staffer(Guid stationId) => new()
    {
        FullName = "Nhân viên", Role = UserRole.RescueStaff, Status = UserStatus.Active, StationId = stationId
    };

    private Incident Incident(SeverityLevel sev = SeverityLevel.Level2, IncidentEventType type = IncidentEventType.FireExplosion,
        IncidentStatus status = IncidentStatus.Verified, Action<Incident>? tweak = null)
    {
        var inc = new Incident
        {
            Title = "Cháy", Latitude = Lat, Longitude = Lng, Severity = sev, EventType = type, Status = status,
            ResponsibleStationId = _stationA, VerifiedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        };
        tweak?.Invoke(inc);
        _incidentById[inc.Id] = inc;
        _incidents.Setup(i => i.GetIncidentWithDetailsAsync(inc.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inc);
        _incidents.Setup(i => i.GetByIdAsync(inc.Id)).ReturnsAsync(inc);
        return inc;
    }

    // ===================== Recommendations =====================
    [Fact]
    public async Task Recommend_NearestFirst_OnlyAvailableMatchingTypes()
    {
        var inc = Incident();
        var far = Unit(RescueUnitType.FireTruck, 8);
        var near = Unit(RescueUnitType.FireTruck, 2);
        Unit(RescueUnitType.Ambulance, 1);                                   // wrong type for fire Level 2
        Unit(RescueUnitType.FireTruck, 1, RescueUnitStatus.Maintenance);     // not available

        var r = await _svc.GetRecommendationsAsync(inc.Id);

        Assert.True(r.Success);
        Assert.Equal(new[] { near.Id, far.Id }, r.Data!.Select(x => x.RescueUnitId));
        Assert.All(r.Data!, x => Assert.Equal(10, x.SearchRadiusKm));
        Assert.All(r.Data!, x => Assert.True(x.IsApproximate));
    }

    [Fact]
    public async Task Recommend_UnitWithActiveTask_IsExcluded()
    {
        var inc = Incident();
        var busy = Unit(RescueUnitType.FireTruck, 1);
        var free = Unit(RescueUnitType.FireTruck, 3);
        _active.Add(new DispatchAssignment { RescueUnitId = busy.Id, IncidentId = Guid.NewGuid(), Status = DispatchAssignmentStatus.EnRoute });

        var r = await _svc.GetRecommendationsAsync(inc.Id);

        Assert.Equal(new[] { free.Id }, r.Data!.Select(x => x.RescueUnitId));
    }

    [Fact]
    public async Task Recommend_ExpandsRadiusWhenNothingNearby()
    {
        var inc = Incident();
        var u = Unit(RescueUnitType.FireTruck, 12);

        var r = await _svc.GetRecommendationsAsync(inc.Id);

        Assert.Equal(u.Id, r.Data!.Single().RescueUnitId);
        Assert.Equal(15, r.Data![0].SearchRadiusKm);
        Assert.True(r.Data![0].RadiusExpanded);
    }

    [Fact]
    public async Task Recommend_EscalatedIncident_UsesMultipliedRadius()
    {
        var inc = Incident(status: IncidentStatus.Escalated);
        Unit(RescueUnitType.FireTruck, 3);

        var r = await _svc.GetRecommendationsAsync(inc.Id);

        Assert.Equal(20, r.Data![0].SearchRadiusKm);
    }

    [Fact]
    public async Task Recommend_NothingWithinMaxRadius_ReturnsEmptySuccess()
    {
        var inc = Incident();
        Unit(RescueUnitType.FireTruck, 40);

        var r = await _svc.GetRecommendationsAsync(inc.Id);

        Assert.True(r.Success);
        Assert.Empty(r.Data!);
    }

    [Fact]
    public async Task Recommend_LoadAware_PrefersLessLoadedStation()
    {
        var inc = Incident();
        var busyStation = Guid.NewGuid();
        var nearButLoaded = Unit(RescueUnitType.FireTruck, 2, station: busyStation);
        Unit(RescueUnitType.FireTruck, 2.1, RescueUnitStatus.Dispatched, busyStation);
        Unit(RescueUnitType.FireTruck, 2.2, RescueUnitStatus.OnScene, busyStation);
        var fartherIdle = Unit(RescueUnitType.FireTruck, 4, station: _stationA);

        var nearest = await _svc.GetRecommendationsAsync(inc.Id, DispatchAlgorithm.NearestAvailable);
        var loadAware = await _svc.GetRecommendationsAsync(inc.Id, DispatchAlgorithm.LoadAware);

        Assert.Equal(nearButLoaded.Id, nearest.Data![0].RescueUnitId);
        Assert.Equal(fartherIdle.Id, loadAware.Data![0].RescueUnitId);
        Assert.True(loadAware.Data!.First(x => x.RescueUnitId == nearButLoaded.Id).IsSupportUnit);
    }

    [Fact]
    public async Task Recommend_UsesRoadDistanceWhenRoutingAvailable()
    {
        var inc = Incident();
        var u = Unit(RescueUnitType.FireTruck, 2);
        _routing.Setup(r => r.GetLegsToAsync(It.IsAny<IReadOnlyList<(double, double)>>(), Lat, Lng, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RoadLeg?> { new(3.5, 7) });

        var x = (await _svc.GetRecommendationsAsync(inc.Id)).Data!.Single();

        Assert.Equal(3.5, x.RoadDistanceKm);
        Assert.Equal(9, x.EstimatedEtaMinutes);
        Assert.False(x.IsApproximate);
    }

    [Fact]
    public async Task Recommend_OtherStationIncident_IsForbidden()
    {
        var inc = Incident(tweak: i => i.ResponsibleStationId = _stationB);

        Assert.Equal(ApiErrorCodes.Forbidden, (await _svc.GetRecommendationsAsync(inc.Id)).ErrorCode);
    }

    // ===================== Assign =====================
    [Fact]
    public async Task Assign_Valid_CreatesAssignmentMarksUnitAndIncidentDispatched()
    {
        var inc = Incident();
        var u = Unit(RescueUnitType.FireTruck, 2);

        var r = await _svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = inc.Id, RescueUnitId = u.Id }, _operatorId);

        Assert.True(r.Success);
        Assert.Equal(IncidentStatus.Dispatched, inc.Status);
        Assert.Equal(RescueUnitStatus.Dispatched, u.Status);
        var a = inc.DispatchAssignments.Single();
        Assert.Equal(DispatchAssignmentStatus.Dispatched, a.Status);
        Assert.InRange(a.EstimatedArrivalMinutes ?? 0, 5.4, 5.5); // 2 km / 35 km/h + 2 min prep
        Assert.NotNull(inc.DispatchTimeMs);
        _rt.Verify(n => n.JobAssignedAsync(It.IsAny<DispatchAssignmentResponseDto>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        _notify.Verify(n => n.NotifyCrewAsync(_stationA, It.IsAny<Guid?>(), It.IsAny<EmergencyDispatch.Application.DTOs.Notification.NotificationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Assign_NoUnits_IsRejected()
    {
        Assert.False((await _svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = Guid.NewGuid() }, _operatorId)).Success);
    }

    [Theory]
    [InlineData(SeverityLevel.Unclassified, true)]
    [InlineData(SeverityLevel.Level2, false)]
    public async Task Assign_RequiresVerifiedAndClassified(SeverityLevel sev, bool verified)
    {
        var inc = Incident(sev, tweak: i => i.VerifiedAt = verified ? DateTime.UtcNow : null);
        var u = Unit(RescueUnitType.FireTruck, 2);

        Assert.False((await _svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = inc.Id, RescueUnitId = u.Id }, _operatorId)).Success);
        Assert.Equal(RescueUnitStatus.Available, u.Status);
    }

    [Theory]
    [InlineData(IncidentStatus.Completed)]
    [InlineData(IncidentStatus.Cancelled)]
    public async Task Assign_FinishedIncident_IsRejected(IncidentStatus status)
    {
        var inc = Incident(status: status);
        var u = Unit(RescueUnitType.FireTruck, 2);

        Assert.False((await _svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = inc.Id, RescueUnitId = u.Id }, _operatorId)).Success);
    }

    [Fact]
    public async Task Assign_Level2_AllowsOneUnit_UnlessOverrideWithReason()
    {
        var inc = Incident();
        var a = Unit(RescueUnitType.FireTruck, 2);
        var b = Unit(RescueUnitType.FireTruck, 3);

        var two = await _svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = inc.Id, RescueUnitIds = new() { a.Id, b.Id } }, _operatorId);
        var overrideNoReason = await _svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = inc.Id, RescueUnitIds = new() { a.Id, b.Id }, AllowExtraUnits = true }, _operatorId);
        var overrideWithReason = await _svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = inc.Id, RescueUnitIds = new() { a.Id, b.Id }, AllowExtraUnits = true, Notes = "Cháy lan rộng" }, _operatorId);

        Assert.False(two.Success);
        Assert.False(overrideNoReason.Success);
        Assert.True(overrideWithReason.Success);
        Assert.Equal(2, inc.DispatchAssignments.Count);
    }

    [Fact]
    public async Task Assign_Level2_AdminPlanOfTwoUnits_AllowsTwoWithoutOverride_ThirdNeedsReason()
    {
        // Admin cấu hình: cháy nổ Level 2 điều 2 xe chữa cháy → số xe chuẩn BR-14 của Level 2 là 2
        var catalog = new Mock<ITypeCatalogRepository>();
        catalog.Setup(c => c.GetUnitRulesAsync("FireExplosion", It.IsAny<CancellationToken>())).ReturnsAsync(new List<IncidentTypeUnitRule>
        {
            new() { UnitTypeCode = RescueUnitType.FireTruck, Severity = SeverityLevel.Level2, Quantity = 2, Priority = 1 }
        });
        var svc = new DispatchService(_assign.Object, _units.Object, _incidents.Object, _users.Object, _rt.Object, _settings.Object,
            _notify.Object, NullLogger<DispatchService>.Instance, _routing.Object, _me.Object, _esc.Object, _followUp.Object, _fieldRequests.Object,
            TestHelpers.Config(("Cloudinary:CloudName", "demo")), _teams.Object, catalog.Object);
        var inc = Incident();
        var a = Unit(RescueUnitType.FireTruck, 2);
        var b = Unit(RescueUnitType.FireTruck, 3);
        var c = Unit(RescueUnitType.FireTruck, 4);

        var two = await svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = inc.Id, RescueUnitIds = new() { a.Id, b.Id } }, _operatorId);
        var third = await svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = inc.Id, RescueUnitId = c.Id }, _operatorId);

        Assert.True(two.Success);
        Assert.False(third.Success);
        Assert.Equal(2, inc.DispatchAssignments.Count);
    }

    [Fact]
    public async Task Assign_Level3_AllowsUpToFiveUnitsWithoutOverride()
    {
        var inc = Incident(SeverityLevel.Level3);
        var ids = Enumerable.Range(1, 5).Select(i => Unit(RescueUnitType.FireTruck, i).Id).ToList();
        var sixth = Unit(RescueUnitType.FireTruck, 6);

        var five = await _svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = inc.Id, RescueUnitIds = ids }, _operatorId);
        var more = await _svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = inc.Id, RescueUnitId = sixth.Id, AllowExtraUnits = true, Notes = "Cần thêm xe" }, _operatorId);

        Assert.True(five.Success);
        Assert.False(more.Success);
    }

    [Fact]
    public async Task Assign_UnitAlreadyOnThisIncident_IsRejected()
    {
        var u = Unit(RescueUnitType.FireTruck, 2);
        var inc = Incident(SeverityLevel.Level3, tweak: i =>
            i.DispatchAssignments.Add(new DispatchAssignment { RescueUnitId = u.Id, Status = DispatchAssignmentStatus.EnRoute }));

        Assert.False((await _svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = inc.Id, RescueUnitId = u.Id }, _operatorId)).Success);
    }

    [Fact]
    public async Task Assign_ReservationLost_ReleasesEarlierReservationsAndReturnsConflict()
    {
        var inc = Incident(SeverityLevel.Level3);
        var a = Unit(RescueUnitType.FireTruck, 2);
        var b = Unit(RescueUnitType.FireTruck, 3);
        _units.Setup(u => u.TryReserveForDispatchAsync(b.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var r = await _svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = inc.Id, RescueUnitIds = new() { a.Id, b.Id } }, _operatorId);

        Assert.Equal(ApiErrorCodes.Conflict, r.ErrorCode);
        _units.Verify(u => u.ReleaseReservationAsync(a.Id, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(inc.DispatchAssignments);
    }

    [Fact]
    public async Task Assign_ConcurrentSaveConflict_ReleasesReservations()
    {
        var inc = Incident();
        var u = Unit(RescueUnitType.FireTruck, 2);
        _incidents.Setup(i => i.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new ConcurrencyConflictException("x"));

        var r = await _svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = inc.Id, RescueUnitId = u.Id }, _operatorId);

        Assert.Equal(ApiErrorCodes.Conflict, r.ErrorCode);
        _units.Verify(x => x.ReleaseReservationAsync(u.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Assign_EscalatedIncident_IsDeEscalated()
    {
        var inc = Incident(SeverityLevel.Level3, status: IncidentStatus.Escalated);
        var u = Unit(RescueUnitType.FireTruck, 2);

        await _svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = inc.Id, RescueUnitId = u.Id }, _operatorId);

        _esc.Verify(e => e.ResolveOpenLogsAsync(inc.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _incidents.Verify(i => i.AddAuditTrailAsync(It.Is<IncidentAuditTrail>(a => a.Action == AuditAction.DeEscalated), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Assign_LockedByOtherOperator_IsConflict()
    {
        var inc = Incident(tweak: i => { i.LockedByOperatorId = Guid.NewGuid(); i.LockExpiresAt = DateTime.UtcNow.AddMinutes(1); });
        var u = Unit(RescueUnitType.FireTruck, 2);

        Assert.Equal(ApiErrorCodes.Conflict, (await _svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = inc.Id, RescueUnitId = u.Id }, _operatorId)).ErrorCode);
    }

    // ===================== Status transitions =====================
    private (DispatchAssignment A, Incident I, RescueUnit U, User Staff) Mission(DispatchAssignmentStatus status, IncidentStatus incStatus = IncidentStatus.Dispatched)
    {
        var u = Unit(RescueUnitType.FireTruck, 2, RescueUnitStatus.Dispatched, teamed: false);
        var inc = Incident(status: incStatus);
        var a = new DispatchAssignment { IncidentId = inc.Id, Incident = inc, RescueUnitId = u.Id, RescueUnit = u, Status = status };
        inc.DispatchAssignments.Add(a);
        _assign.Setup(x => x.GetByIdWithDetailsAsync(a.Id)).ReturnsAsync(a);
        _assign.Setup(x => x.GetIncidentIdAsync(a.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inc.Id);
        _assign.Setup(x => x.GetByIncidentIdAsync(inc.Id)).ReturnsAsync(() => inc.DispatchAssignments.ToList());
        var staff = new User { Role = UserRole.RescueStaff, StationId = _stationA, FullName = "Staff" };
        _users.Setup(x => x.GetByIdAsync(staff.Id)).ReturnsAsync(staff);
        return (a, inc, u, staff);
    }

    [Fact]
    public async Task Status_FullHappyPath_UpdatesUnitAndIncident()
    {
        var (a, inc, u, staff) = Mission(DispatchAssignmentStatus.Dispatched);

        Assert.True((await _svc.UpdateAssignmentStatusAsync(a.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.Accepted }, staff.Id)).Success);
        Assert.Equal(IncidentStatus.InProgress, inc.Status);
        Assert.NotNull(a.AcceptedAt);

        Assert.True((await _svc.UpdateAssignmentStatusAsync(a.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.EnRoute }, staff.Id)).Success);
        Assert.NotNull(a.EnRouteAt);

        Assert.True((await _svc.UpdateAssignmentStatusAsync(a.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.OnScene }, staff.Id)).Success);
        Assert.Equal(RescueUnitStatus.OnScene, u.Status);
        Assert.NotNull(a.ArrivedAt);
    }

    [Fact]
    public async Task Status_SkippingSteps_IsRejected()
    {
        var (a, _, _, staff) = Mission(DispatchAssignmentStatus.Dispatched);

        var r = await _svc.UpdateAssignmentStatusAsync(a.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.OnScene }, staff.Id);

        Assert.False(r.Success);
        Assert.Equal(DispatchAssignmentStatus.Dispatched, a.Status);
    }

    [Theory]
    [InlineData(DispatchAssignmentStatus.Completed)]
    [InlineData(DispatchAssignmentStatus.Rejected)]
    public async Task Status_CompletedOrRejected_MustUseDedicatedApis(DispatchAssignmentStatus target)
    {
        var (a, _, _, staff) = Mission(DispatchAssignmentStatus.OnScene);

        Assert.False((await _svc.UpdateAssignmentStatusAsync(a.Id, new UpdateAssignmentStatusDto { Status = target }, staff.Id)).Success);
    }

    [Fact]
    public async Task Status_StaffOfOtherStation_IsForbidden()
    {
        var (a, _, _, _) = Mission(DispatchAssignmentStatus.Dispatched);
        var outsider = new User { Role = UserRole.RescueStaff, StationId = _stationB };
        _users.Setup(x => x.GetByIdAsync(outsider.Id)).ReturnsAsync(outsider);

        var r = await _svc.UpdateAssignmentStatusAsync(a.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.Accepted }, outsider.Id);

        Assert.Equal(ApiErrorCodes.Forbidden, r.ErrorCode);
    }

    [Fact]
    public async Task Status_StaffCannotCancel_CommandCenterCan()
    {
        var (a, _, u, staff) = Mission(DispatchAssignmentStatus.Accepted);
        var op = new User { Role = UserRole.Operator, StationId = _stationA };
        _users.Setup(x => x.GetByIdAsync(op.Id)).ReturnsAsync(op);

        var byStaff = await _svc.UpdateAssignmentStatusAsync(a.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.Cancelled }, staff.Id);
        var byOp = await _svc.UpdateAssignmentStatusAsync(a.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.Cancelled }, op.Id);

        Assert.Equal(ApiErrorCodes.Forbidden, byStaff.ErrorCode);
        Assert.True(byOp.Success);
        Assert.Equal(RescueUnitStatus.Available, u.Status);
    }

    // ===================== Phạm vi trạm của Operator (thu hồi / từ chối / nghiệm thu thay) =====================
    [Fact]
    public async Task CommandCenter_OperatorOfOtherStation_IsForbidden_OnRecallRejectResolve()
    {
        var (a, inc, u, _) = Mission(DispatchAssignmentStatus.Accepted);
        var opB = new User { Role = UserRole.Operator, StationId = _stationB, FullName = "Op B" };
        _users.Setup(x => x.GetByIdAsync(opB.Id)).ReturnsAsync(opB);

        var recall = await _svc.UpdateAssignmentStatusAsync(a.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.Cancelled }, opB.Id);
        var reject = await _svc.RejectAssignmentAsync(a.Id, new RejectAssignmentDto { Reason = RejectionReason.TooFar }, opB.Id);
        var resolve = await _svc.ResolveAssignmentAsync(a.Id, new ResolveDispatchDto { ResolutionSummary = "Đã xử lý xong", EvidenceMediaUrls = new() { "https://images.unsplash.com/x.jpg" } }, opB.Id);

        Assert.Equal(ApiErrorCodes.Forbidden, recall.ErrorCode);
        Assert.Equal(ApiErrorCodes.Forbidden, reject.ErrorCode);
        Assert.Equal(ApiErrorCodes.Forbidden, resolve.ErrorCode);
        Assert.Equal(DispatchAssignmentStatus.Accepted, a.Status);
        Assert.Equal(RescueUnitStatus.Dispatched, u.Status);
        Assert.Equal(IncidentStatus.Dispatched, inc.Status);
    }

    [Fact]
    public async Task CommandCenter_OperatorOfSupportUnitStation_CanRecall_AdminAlways()
    {
        var (a, _, u, _) = Mission(DispatchAssignmentStatus.Accepted);
        u.StationId = _stationB; // xe trạm B đi hỗ trợ sự cố trạm A
        var opB = new User { Role = UserRole.Operator, StationId = _stationB, FullName = "Op B" };
        _users.Setup(x => x.GetByIdAsync(opB.Id)).ReturnsAsync(opB);

        var r = await _svc.UpdateAssignmentStatusAsync(a.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.Cancelled }, opB.Id);
        Assert.True(r.Success, r.Message);

        var (a2, _, _, _) = Mission(DispatchAssignmentStatus.Accepted);
        var admin = new User { Role = UserRole.Admin, FullName = "Admin" };
        _users.Setup(x => x.GetByIdAsync(admin.Id)).ReturnsAsync(admin);
        Assert.True((await _svc.RejectAssignmentAsync(a2.Id, new RejectAssignmentDto { Reason = RejectionReason.TooFar }, admin.Id)).Success);
    }

    [Theory]
    [InlineData(UserRole.Admin, null, null, null, true)]
    [InlineData(UserRole.Operator, "A", null, "A", true)]   // sự cố chưa gán trạm
    [InlineData(UserRole.Operator, "A", "A", "B", true)]    // sự cố trạm mình
    [InlineData(UserRole.Operator, "B", "A", "B", true)]    // xe trạm mình đi hỗ trợ
    [InlineData(UserRole.Operator, "B", "A", "A", false)]
    [InlineData(UserRole.Operator, null, "A", "A", false)]  // chưa được phân trạm
    public void OperatorScope_CheckAssignment(UserRole role, string? actor, string? incidentStation, string? unitStation, bool allowed)
    {
        Guid? G(string? k) => k == null ? null : k == "A" ? _stationA : _stationB;
        Assert.Equal(allowed, OperatorScope.CheckAssignment(role, G(actor), G(incidentStation), G(unitStation)) == null);
    }

    // ===================== Quyền tổ theo tổ lưu trên lượt =====================
    [Fact]
    public async Task Crew_IsTeamStoredOnAssignment_NotCurrentTeamOfVehicle()
    {
        var (a, _, u, staff) = Mission(DispatchAssignmentStatus.Dispatched);
        var teamOld = Guid.NewGuid();
        var teamNew = Guid.NewGuid();
        a.StationTeamId = teamOld;
        u.StationTeamId = teamNew; // xe bị chuyển tổ khi đang làm nhiệm vụ (dữ liệu cũ)
        var newcomer = new User { Role = UserRole.RescueStaff, StationId = _stationA, FullName = "Tổ mới" };
        _users.Setup(x => x.GetByIdAsync(newcomer.Id)).ReturnsAsync(newcomer);
        _users.Setup(x => x.GetTeamIdAsync(staff.Id, It.IsAny<CancellationToken>())).ReturnsAsync(teamOld);
        _users.Setup(x => x.GetTeamIdAsync(newcomer.Id, It.IsAny<CancellationToken>())).ReturnsAsync(teamNew);

        var byNew = await _svc.UpdateAssignmentStatusAsync(a.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.Accepted }, newcomer.Id);
        var byOld = await _svc.UpdateAssignmentStatusAsync(a.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.Accepted }, staff.Id);

        Assert.Equal(ApiErrorCodes.Forbidden, byNew.ErrorCode);
        Assert.True(byOld.Success, byOld.Message);
        Assert.True(await _svc.IsCrewOfAssignmentAsync(a.Id, staff.Id));
        Assert.False(await _svc.IsCrewOfAssignmentAsync(a.Id, newcomer.Id));
    }

    [Fact]
    public async Task IsCrewOfAssignment_SubmitterOrCrew_True_CitizenFalse()
    {
        var (a, _, _, staff) = Mission(DispatchAssignmentStatus.Completed);
        var submitter = new User { Role = UserRole.RescueStaff, StationId = _stationB };
        _users.Setup(x => x.GetByIdAsync(submitter.Id)).ReturnsAsync(submitter);
        a.CompletionReport = new CompletionReport { SubmittedByUserId = submitter.Id };
        var citizen = new User { Role = UserRole.Citizen };
        _users.Setup(x => x.GetByIdAsync(citizen.Id)).ReturnsAsync(citizen);

        Assert.True(await _svc.IsCrewOfAssignmentAsync(a.Id, staff.Id));     // xe chưa gán tổ, cùng trạm
        Assert.True(await _svc.IsCrewOfAssignmentAsync(a.Id, submitter.Id)); // người nộp báo cáo
        Assert.False(await _svc.IsCrewOfAssignmentAsync(a.Id, citizen.Id));
    }

    [Fact]
    public async Task Reject_CancelsOpenFieldRequestsOfThatAssignment()
    {
        var (a, inc, _, staff) = Mission(DispatchAssignmentStatus.Accepted);

        await _svc.RejectAssignmentAsync(a.Id, new RejectAssignmentDto { Reason = RejectionReason.TooFar }, staff.Id);

        _fieldRequests.Verify(f => f.CancelOpenAsync(inc.Id, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(a.Id)), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Status_InactiveAssignment_IsRejected()
    {
        var (a, _, _, staff) = Mission(DispatchAssignmentStatus.Completed);

        Assert.False((await _svc.UpdateAssignmentStatusAsync(a.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.EnRoute }, staff.Id)).Success);
    }

    // ===================== Reject =====================
    [Fact]
    public async Task Reject_OtherWithoutNotes_IsRejected()
    {
        var (a, _, _, staff) = Mission(DispatchAssignmentStatus.Dispatched);

        Assert.False((await _svc.RejectAssignmentAsync(a.Id, new RejectAssignmentDto { Reason = RejectionReason.Other, Notes = "ab" }, staff.Id)).Success);
    }

    [Fact]
    public async Task Reject_Breakdown_PutsUnitInMaintenanceAndReturnsIncidentToVerified()
    {
        var (a, inc, u, staff) = Mission(DispatchAssignmentStatus.Accepted);

        var r = await _svc.RejectAssignmentAsync(a.Id, new RejectAssignmentDto { Reason = RejectionReason.VehicleBreakdown }, staff.Id);

        Assert.True(r.Success);
        Assert.Equal(DispatchAssignmentStatus.Rejected, a.Status);
        Assert.Equal(RescueUnitStatus.Maintenance, u.Status);
        Assert.Equal(IncidentStatus.Verified, inc.Status);
    }

    [Fact]
    public async Task Reject_LastTeam_OnEscalatedIncident_StaysEscalated_LogsAndAlertsHighPriority()
    {
        var (a, inc, _, staff) = Mission(DispatchAssignmentStatus.Dispatched);
        inc.Status = IncidentStatus.Escalated;
        var opOwn = new User { Role = UserRole.Operator, StationId = _stationA, Status = UserStatus.Active };
        var opOther = new User { Role = UserRole.Operator, StationId = _stationB, Status = UserStatus.Active };
        var admin = new User { Role = UserRole.Admin, Status = UserStatus.Active };
        _users.Setup(u => u.GetActiveByRolesAsync(It.IsAny<IReadOnlyCollection<UserRole>>())).ReturnsAsync(new List<User> { opOwn, opOther, admin });
        NotificationRequest? sent = null;
        IReadOnlyCollection<Guid>? recipients = null;
        _notify.Setup(n => n.NotifyUsersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.Is<NotificationRequest>(x => x.Title.Vi == "Đội cứu hộ từ chối nhiệm vụ"), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyCollection<Guid> ids, NotificationRequest n, CancellationToken _) => (recipients, sent) = (ids, n))
            .Returns(Task.CompletedTask);

        var r = await _svc.RejectAssignmentAsync(a.Id, new RejectAssignmentDto { Reason = RejectionReason.TooFar }, staff.Id);

        Assert.True(r.Success);
        // Chỉ trung tâm của sự cố (Operator trạm phụ trách + Admin), không báo Operator trạm khác
        Assert.Equal(new[] { opOwn.Id, admin.Id }.OrderBy(x => x), recipients!.OrderBy(x => x));
        _notify.Verify(n => n.NotifyRolesAsync(It.IsAny<IReadOnlyCollection<UserRole>>(), It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(IncidentStatus.Escalated, inc.Status);
        Assert.Equal(EmergencyDispatch.Application.Localization.LocalizationHelper.Localize("Đã từ chối nhiệm vụ. Trung tâm điều phối sẽ giao xe thay thế."), r.Message);
        Assert.True(sent!.HighPriority);
        Assert.Contains("leo thang", sent.Message.Vi);
        _incidents.Verify(i => i.AddAuditTrailAsync(It.Is<IncidentAuditTrail>(t => t.Description.Contains("vẫn đang leo thang")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reject_WritesAssignmentAndUnitInsideIncidentLock_PublishesUnitAfterCommit()
    {
        var (a, inc, u, staff) = Mission(DispatchAssignmentStatus.Accepted);
        var order = new List<string>();
        _units.Setup(x => x.SaveStatusAsync(u, It.IsAny<CancellationToken>())).Callback(() => order.Add("unit"));
        _lock.Setup(l => l.CommitAsync(It.IsAny<CancellationToken>())).Callback(() => order.Add("commit"));
        _rt.Setup(x => x.RescueUnitStatusChangedAsync(It.IsAny<EmergencyDispatch.Application.DTOs.Incident.RescueUnitStatusEventDto>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("publish"));

        await _svc.RejectAssignmentAsync(a.Id, new RejectAssignmentDto { Reason = RejectionReason.TooFar }, staff.Id);

        Assert.Equal(new[] { "unit", "commit", "publish" }, order.Take(3));
    }

    [Fact]
    public async Task Reject_OtherReason_MakesUnitAvailable_KeepsIncidentWhenOtherUnitsActive()
    {
        var (a, inc, u, staff) = Mission(DispatchAssignmentStatus.Dispatched);
        inc.DispatchAssignments.Add(new DispatchAssignment { Status = DispatchAssignmentStatus.EnRoute });

        await _svc.RejectAssignmentAsync(a.Id, new RejectAssignmentDto { Reason = RejectionReason.TooFar }, staff.Id);

        Assert.Equal(RescueUnitStatus.Available, u.Status);
        Assert.Equal(IncidentStatus.Dispatched, inc.Status);
    }

    [Fact]
    public async Task Reject_OnScene_IsNotAllowed()
    {
        var (a, _, _, staff) = Mission(DispatchAssignmentStatus.OnScene);

        Assert.False((await _svc.RejectAssignmentAsync(a.Id, new RejectAssignmentDto { Reason = RejectionReason.TooFar }, staff.Id)).Success);
    }

    // ===================== Resolve =====================
    private static ResolveDispatchDto Report(params string[] evidence) => new()
    {
        ResolutionSummary = "Đã dập tắt đám cháy",
        EvidenceMediaUrls = evidence.ToList(),
        VictimsAssistedCount = 1,
        SignatureUrl = Signature
    };

    private const string Signature = "https://res.cloudinary.com/demo/image/upload/sig.png";

    /// <summary>
    /// Tạo tổ phụ trách một xe; trả về tổ (đã có staff là thành viên nếu truyền vào)
    /// </summary>
    private StationTeam CrewOf(RescueUnit u, params User[] members) => CrewOfUnits(new[] { u }, members);

    /// <summary>
    /// Tạo tổ phụ trách nhiều xe (RescueUnit.StationTeamId)
    /// </summary>
    private StationTeam CrewOfUnits(RescueUnit[] units, params User[] members)
    {
        var team = new StationTeam { Name = $"Tổ {_crews.Count + 1}", StationId = units[0].StationId };
        foreach (var unit in units)
        {
            unit.StationTeamId = team.Id;
            team.RescueUnits.Add(unit);
        }
        foreach (var m in members)
        {
            team.Members.Add(new StationTeamMember { TeamId = team.Id, StaffId = m.Id, Staff = m });
            _users.Setup(x => x.GetTeamIdAsync(m.Id, It.IsAny<CancellationToken>())).ReturnsAsync(team.Id);
        }
        _crews.Add(team);
        _teams.Setup(t => t.GetByIdAsync(team.Id, It.IsAny<CancellationToken>())).ReturnsAsync(team);
        return team;
    }

    [Fact]
    public async Task Resolve_LastActiveUnit_CompletesIncidentAndStartsCoolDown()
    {
        var (a, inc, u, staff) = Mission(DispatchAssignmentStatus.OnScene, IncidentStatus.InProgress);

        var r = await _svc.ResolveAssignmentAsync(a.Id, Report("https://x/a.jpg"), staff.Id);

        Assert.True(r.Success);
        Assert.Equal(DispatchAssignmentStatus.Completed, a.Status);
        Assert.Equal(IncidentStatus.Completed, inc.Status);
        Assert.Equal(RescueUnitStatus.CoolDown, u.Status);
        Assert.InRange((u.CoolDownUntil!.Value - DateTime.UtcNow).TotalMinutes, 4.9, 5.1);
        _assign.Verify(x => x.AddCompletionReportAsync(It.Is<CompletionReport>(c => c.MediaItems.Count == 1 && c.VictimsAssistedCount == 1)), Times.Once);
        _lock.Verify(l => l.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Resolve_OtherUnitsStillActive_KeepsIncidentInProgress()
    {
        var (a, inc, _, staff) = Mission(DispatchAssignmentStatus.OnScene, IncidentStatus.InProgress);
        inc.DispatchAssignments.Add(new DispatchAssignment { Status = DispatchAssignmentStatus.EnRoute });

        await _svc.ResolveAssignmentAsync(a.Id, Report("https://x/a.jpg"), staff.Id);

        Assert.Equal(IncidentStatus.InProgress, inc.Status);
    }

    [Fact]
    public async Task Resolve_StoresSignatureWithSubmittingMemberAsSigner()
    {
        var (a, _, _, staff) = Mission(DispatchAssignmentStatus.OnScene, IncidentStatus.InProgress);
        var dto = Report("https://x/a.jpg");
        dto.SignatureUrl = $" {Signature} ";
        dto.HandoverContactName = "BS. Lan";
        CompletionReport? saved = null;
        _assign.Setup(x => x.AddCompletionReportAsync(It.IsAny<CompletionReport>())).Callback<CompletionReport>(c => saved = c);

        var r = await _svc.ResolveAssignmentAsync(a.Id, dto, staff.Id);

        Assert.True(r.Success);
        Assert.Equal(Signature, saved!.SignatureUrl);
        Assert.Equal(staff.FullName, saved.SignerName);
        Assert.Equal(staff.Id, saved.SubmittedByUserId);
        Assert.NotNull(saved.SignedAt);
        Assert.Equal(Signature, r.Data!.CompletionReport!.SignatureUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task Resolve_WithoutSignature_IsRejectedWithoutCompleting(string? signature)
    {
        var (a, inc, _, staff) = Mission(DispatchAssignmentStatus.OnScene, IncidentStatus.InProgress);
        var dto = Report("https://x/a.jpg");
        dto.SignatureUrl = signature;

        var r = await _svc.ResolveAssignmentAsync(a.Id, dto, staff.Id);

        Assert.False(r.Success);
        Assert.Equal(EmergencyDispatch.Application.Localization.LocalizationHelper.Localize(DispatchService.SignatureRequiredMessage), r.Message);
        Assert.Equal(DispatchAssignmentStatus.OnScene, a.Status);
        Assert.Equal(IncidentStatus.InProgress, inc.Status);
        _assign.Verify(x => x.AddCompletionReportAsync(It.IsAny<CompletionReport>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_AnyCrewMember_CompletesForWholeCrewAndNotifiesOthers()
    {
        var (a, inc, u, staff) = Mission(DispatchAssignmentStatus.OnScene, IncidentStatus.InProgress);
        var driver = new User { Role = UserRole.RescueStaff, StationId = _stationA, FullName = "Tài xế" };
        var medic = new User { Role = UserRole.RescueStaff, StationId = _stationA, FullName = "Y tá" };
        CrewOf(u, driver, staff, medic);
        IReadOnlyCollection<Guid>? notified = null;
        _notify.Setup(n => n.NotifyUsersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.Is<NotificationRequest>(r => r.Title.Vi == "TỔ ĐÃ NGHIỆM THU NHIỆM VỤ"), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<Guid>, NotificationRequest, CancellationToken>((ids, _, _) => notified = ids);

        var r = await _svc.ResolveAssignmentAsync(a.Id, Report("https://x/a.jpg"), staff.Id);

        Assert.True(r.Success);
        Assert.Equal(DispatchAssignmentStatus.Completed, a.Status);
        Assert.Equal(IncidentStatus.Completed, inc.Status);
        Assert.Equal(new[] { driver.Id, medic.Id }.OrderBy(x => x), notified!.OrderBy(x => x));
        _rt.Verify(x => x.AssignmentStatusChangedAsync(It.Is<DispatchAssignmentResponseDto>(d => d.Status == DispatchAssignmentStatus.Completed), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Resolve_StationStaffOutsideVehicleCrew_IsForbidden()
    {
        var (a, _, u, staff) = Mission(DispatchAssignmentStatus.OnScene, IncidentStatus.InProgress);
        CrewOf(u, new User { Role = UserRole.RescueStaff, StationId = _stationA });

        var r = await _svc.ResolveAssignmentAsync(a.Id, Report("https://x/a.jpg"), staff.Id);

        Assert.Equal(ApiErrorCodes.Forbidden, r.ErrorCode);
        Assert.Equal(DispatchAssignmentStatus.OnScene, a.Status);
    }

    [Fact]
    public async Task Resolve_VehicleWithoutCrew_StationStaffStillAllowed()
    {
        var (a, _, _, staff) = Mission(DispatchAssignmentStatus.OnScene, IncidentStatus.InProgress);

        Assert.True((await _svc.ResolveAssignmentAsync(a.Id, Report("https://x/a.jpg"), staff.Id)).Success);
        _notify.Verify(n => n.NotifyUsersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.Is<NotificationRequest>(r => r.Title.Vi == "TỔ ĐÃ NGHIỆM THU NHIỆM VỤ"), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_CrewWithTwoUnits_OneSignatureClosesAllItsUnits_OtherCrewStillPending()
    {
        // Tổ 1 có 2 xe: xe chữa cháy đã tới (OnScene), xe thang còn trên đường (EnRoute). Tổ 2 có 1 xe cấp cứu.
        var (a1, inc, u1, leader) = Mission(DispatchAssignmentStatus.OnScene, IncidentStatus.InProgress);
        DispatchAssignment AddUnit(string type, DispatchAssignmentStatus status, out RescueUnit unit)
        {
            unit = Unit(type, 3, RescueUnitStatus.Dispatched);
            var a = new DispatchAssignment { IncidentId = inc.Id, Incident = inc, RescueUnitId = unit.Id, RescueUnit = unit, Status = status };
            inc.DispatchAssignments.Add(a);
            _assign.Setup(x => x.GetByIdWithDetailsAsync(a.Id)).ReturnsAsync(a);
            _assign.Setup(x => x.GetIncidentIdAsync(a.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inc.Id);
            return a;
        }
        var a2 = AddUnit(RescueUnitType.LadderTruck, DispatchAssignmentStatus.EnRoute, out var u2);
        var a3 = AddUnit(RescueUnitType.Ambulance, DispatchAssignmentStatus.OnScene, out var u3);
        var crew1Member = new User { Role = UserRole.RescueStaff, StationId = _stationA, FullName = "Lái xe tổ 1" };
        var crew2Member = new User { Role = UserRole.RescueStaff, StationId = _stationA, FullName = "Y tá tổ 2" };
        _users.Setup(x => x.GetByIdAsync(crew1Member.Id)).ReturnsAsync(crew1Member);
        _users.Setup(x => x.GetByIdAsync(crew2Member.Id)).ReturnsAsync(crew2Member);
        var crew1 = CrewOfUnits(new[] { u1, u2 }, leader, crew1Member);
        var crew2 = CrewOf(u3, crew2Member);
        var reports = new List<CompletionReport>();
        _assign.Setup(x => x.AddCompletionReportAsync(It.IsAny<CompletionReport>())).Callback<CompletionReport>(reports.Add);

        // Đại diện tổ 1 ký một lần → cả 2 xe của tổ 1 hoàn tất (kể cả xe còn EnRoute), dùng chung một báo cáo
        var first = await _svc.ResolveAssignmentAsync(a1.Id, Report("https://x/a.jpg"), leader.Id);

        Assert.True(first.Success);
        Assert.Equal(DispatchAssignmentStatus.Completed, a1.Status);
        Assert.Equal(DispatchAssignmentStatus.Completed, a2.Status);
        Assert.Same(a1.CompletionReport, a2.CompletionReport);
        Assert.Equal(crew1.Id, reports.Single().StationTeamId);
        Assert.Equal(inc.Id, reports.Single().IncidentId);
        Assert.Equal(RescueUnitStatus.CoolDown, u2.Status);
        Assert.Equal(DispatchAssignmentStatus.OnScene, a3.Status);
        Assert.Equal(IncidentStatus.InProgress, inc.Status);
        Assert.Contains(u3.PlateNumber, first.Message);
        _rt.Verify(x => x.AssignmentStatusChangedAsync(It.Is<DispatchAssignmentResponseDto>(d => d.Status == DispatchAssignmentStatus.Completed), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        // Thành viên khác của tổ 1 bấm nộp trên xe thứ hai → đã nghiệm thu rồi
        Assert.Equal(ApiErrorCodes.Conflict, (await _svc.ResolveAssignmentAsync(a2.Id, Report("https://x/b.jpg"), crew1Member.Id)).ErrorCode);

        // Tổ 2 tự ký → sự cố đóng
        var last = await _svc.ResolveAssignmentAsync(a3.Id, Report("https://x/c.jpg"), crew2Member.Id);
        Assert.True(last.Success);
        Assert.Equal(IncidentStatus.Completed, inc.Status);
        Assert.Equal(new Guid?[] { crew1.Id, crew2.Id }, reports.Select(r => r.StationTeamId));
    }

    [Fact]
    public async Task Resolve_MultipleCrewsOnOneIncident_EachCrewMustSignOff_IncidentClosesAfterLast()
    {
        // Tổ 1 (xe chữa cháy) và Tổ 2 (xe cấp cứu) cùng xử lý một hiện trường
        var (a1, inc, u1, crew1Member) = Mission(DispatchAssignmentStatus.OnScene, IncidentStatus.InProgress);
        var u2 = Unit(RescueUnitType.Ambulance, 3, RescueUnitStatus.OnScene);
        var a2 = new DispatchAssignment { IncidentId = inc.Id, Incident = inc, RescueUnitId = u2.Id, RescueUnit = u2, Status = DispatchAssignmentStatus.OnScene };
        inc.DispatchAssignments.Add(a2);
        _assign.Setup(x => x.GetByIdWithDetailsAsync(a2.Id)).ReturnsAsync(a2);
        _assign.Setup(x => x.GetIncidentIdAsync(a2.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inc.Id);
        var crew2Member = new User { Role = UserRole.RescueStaff, StationId = _stationA, FullName = "Y tá tổ 2" };
        _users.Setup(x => x.GetByIdAsync(crew2Member.Id)).ReturnsAsync(crew2Member);
        CrewOf(u1, crew1Member);
        CrewOf(u2, crew2Member);
        var reports = new List<CompletionReport>();
        _assign.Setup(x => x.AddCompletionReportAsync(It.IsAny<CompletionReport>())).Callback<CompletionReport>(reports.Add);

        // Tổ 1 nộp: chỉ xe của tổ 1 hoàn tất, sự cố còn mở và báo đang chờ xe của tổ 2
        var first = await _svc.ResolveAssignmentAsync(a1.Id, Report("https://x/a.jpg"), crew1Member.Id);
        Assert.True(first.Success);
        Assert.Contains(u2.PlateNumber, first.Message);
        Assert.Equal(DispatchAssignmentStatus.Completed, a1.Status);
        Assert.Equal(DispatchAssignmentStatus.OnScene, a2.Status);
        Assert.Equal(IncidentStatus.InProgress, inc.Status);

        // Thành viên tổ 1 không nộp thay được tổ 2
        var crossCrew = await _svc.ResolveAssignmentAsync(a2.Id, Report("https://x/b.jpg"), crew1Member.Id);
        Assert.Equal(ApiErrorCodes.Forbidden, crossCrew.ErrorCode);
        Assert.Equal(DispatchAssignmentStatus.OnScene, a2.Status);

        // Tổ 2 tự chụp ảnh + ký → sự cố đóng
        var last = await _svc.ResolveAssignmentAsync(a2.Id, Report("https://x/c.jpg"), crew2Member.Id);
        Assert.True(last.Success);
        Assert.Equal(DispatchAssignmentStatus.Completed, a2.Status);
        Assert.Equal(IncidentStatus.Completed, inc.Status);

        Assert.Equal(2, reports.Count);
        Assert.Equal(new[] { crew1Member.FullName, crew2Member.FullName }, reports.Select(r => r.SignerName));
        Assert.All(reports, r => Assert.Equal(Signature, r.SignatureUrl));
    }

    [Fact]
    public async Task Resolve_SecondCrewMemberAfterCompletion_GetsConflictAndNothingChanges()
    {
        var (a, _, u, staff) = Mission(DispatchAssignmentStatus.OnScene, IncidentStatus.InProgress);
        var other = new User { Role = UserRole.RescueStaff, StationId = _stationA, FullName = "Khác" };
        _users.Setup(x => x.GetByIdAsync(other.Id)).ReturnsAsync(other);
        CrewOf(u, staff, other);

        Assert.True((await _svc.ResolveAssignmentAsync(a.Id, Report("https://x/a.jpg"), staff.Id)).Success);
        var second = await _svc.ResolveAssignmentAsync(a.Id, Report("https://x/b.jpg"), other.Id);

        Assert.Equal(ApiErrorCodes.Conflict, second.ErrorCode);
        _assign.Verify(x => x.AddCompletionReportAsync(It.IsAny<CompletionReport>()), Times.Once);
    }

    [Theory]
    [InlineData("data:image/png;base64,iVBORw0KGgo=")]
    [InlineData("https://evil.example.com/sig.png")]
    [InlineData("http://res.cloudinary.com/demo/image/upload/sig.png")]
    [InlineData("https://res.cloudinary.com/other-cloud/image/upload/sig.png")]
    public async Task Resolve_UntrustedSignatureUrl_IsRejectedWithoutCompleting(string url)
    {
        var (a, _, _, staff) = Mission(DispatchAssignmentStatus.OnScene, IncidentStatus.InProgress);
        var dto = Report("https://x/a.jpg");
        dto.SignatureUrl = url;

        var r = await _svc.ResolveAssignmentAsync(a.Id, dto, staff.Id);

        Assert.False(r.Success);
        Assert.Equal(DispatchAssignmentStatus.OnScene, a.Status);
        _assign.Verify(x => x.AddCompletionReportAsync(It.IsAny<CompletionReport>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_WithoutEvidence_IsRejected()
    {
        var (a, _, _, staff) = Mission(DispatchAssignmentStatus.OnScene);

        Assert.False((await _svc.ResolveAssignmentAsync(a.Id, Report(" "), staff.Id)).Success);
        Assert.Equal(DispatchAssignmentStatus.OnScene, a.Status);
    }

    [Fact]
    public async Task Resolve_BeforeOnScene_IsAllowed_SignOffDoesNotDependOnArrival()
    {
        // Chữ ký là bản nộp khi sự cố đã giải quyết, không phụ thuộc xe đã xác nhận OnScene hay chưa
        var (a, _, _, staff) = Mission(DispatchAssignmentStatus.EnRoute);

        Assert.True((await _svc.ResolveAssignmentAsync(a.Id, Report("https://x/a.jpg"), staff.Id)).Success);
        Assert.Equal(DispatchAssignmentStatus.Completed, a.Status);
    }

    [Fact]
    public async Task Resolve_UnknownAssignment_Fails()
    {
        Assert.False((await _svc.ResolveAssignmentAsync(Guid.NewGuid(), Report("https://x/a.jpg"), Guid.NewGuid())).Success);
    }

    // ===================== Field requests / linked reports hooks =====================
    [Fact]
    public async Task Assign_WithBackupFieldRequest_AllowsExtraUnitAndMarksFulfilled()
    {
        var inc = Incident(tweak: i => i.DispatchAssignments.Add(new DispatchAssignment { Status = DispatchAssignmentStatus.OnScene, RescueUnitId = Guid.NewGuid() }));
        var extra = Unit(RescueUnitType.Ambulance, 2);
        var requestId = Guid.NewGuid();
        _fieldRequests.Setup(f => f.ValidateForDispatchAsync(requestId, inc.Id, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var r = await _svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = inc.Id, RescueUnitId = extra.Id, FieldRequestId = requestId }, _operatorId);

        Assert.True(r.Success);
        _fieldRequests.Verify(f => f.MarkFulfilledByDispatchAsync(requestId, It.Is<IReadOnlyCollection<string>>(p => p.Contains(extra.PlateNumber)), _operatorId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Assign_WithInvalidFieldRequest_IsRejected()
    {
        var inc = Incident();
        var u = Unit(RescueUnitType.Ambulance, 2);
        var requestId = Guid.NewGuid();
        _fieldRequests.Setup(f => f.ValidateForDispatchAsync(requestId, inc.Id, It.IsAny<CancellationToken>())).ReturnsAsync("Yêu cầu đã đóng");

        var r = await _svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = inc.Id, RescueUnitId = u.Id, FieldRequestId = requestId }, _operatorId);

        Assert.False(r.Success);
        Assert.Empty(inc.DispatchAssignments);
        _fieldRequests.Verify(f => f.MarkFulfilledByDispatchAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_ClosingIncidentWithLinkedReports_ClosesThem()
    {
        var (a, inc, _, staff) = Mission(DispatchAssignmentStatus.OnScene, IncidentStatus.InProgress);
        inc.LinkedReportCount = 2;

        await _svc.ResolveAssignmentAsync(a.Id, Report("https://x/a.jpg"), staff.Id);

        _followUp.Verify(f => f.CloseLinkedReportsAsync(inc.Id, staff.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Resolve_NoLinkedReports_DoesNotCallFollowUp()
    {
        var (a, _, _, staff) = Mission(DispatchAssignmentStatus.OnScene, IncidentStatus.InProgress);
        await _svc.ResolveAssignmentAsync(a.Id, Report("https://x/a.jpg"), staff.Id);
        _followUp.Verify(f => f.CloseLinkedReportsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ===================== Phạm vi theo tổ (SR-02): tổ khác cùng trạm không thấy / không thao tác =====================
    private User Staff(string name = "Staff")
    {
        var staff = new User { Role = UserRole.RescueStaff, StationId = _stationA, FullName = name, Status = UserStatus.Active };
        _users.Setup(x => x.GetByIdAsync(staff.Id)).ReturnsAsync(staff);
        return staff;
    }

    private void CrewIds(Guid? teamId, params User[] crew)
        => _notify.Setup(n => n.GetCrewUserIdsAsync(_stationA, teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(crew.Select(c => c.Id).ToList());

    [Fact]
    public async Task Assign_TeamUnit_PushAndRealtimeOnlyToThatTeam()
    {
        var inc = Incident();
        var u1 = Unit(RescueUnitType.FireTruck, 2);
        var u2 = Unit(RescueUnitType.FireTruck, 3);
        var m1 = Staff("Tổ 1");
        var m2 = Staff("Tổ 2");
        var team1 = CrewOf(u1, m1);
        var team2 = CrewOf(u2, m2);
        CrewIds(team1.Id, m1);
        CrewIds(team2.Id, m2);
        IReadOnlyCollection<Guid>? realtimeCrew = null;
        _rt.Setup(x => x.JobAssignedAsync(It.IsAny<DispatchAssignmentResponseDto>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .Callback<DispatchAssignmentResponseDto, IReadOnlyCollection<Guid>, CancellationToken>((_, ids, _) => realtimeCrew = ids);

        var r = await _svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = inc.Id, RescueUnitId = u1.Id }, _operatorId);

        Assert.True(r.Success);
        Assert.Equal(new[] { m1.Id }, realtimeCrew);
        _notify.Verify(n => n.NotifyCrewAsync(_stationA, team1.Id, It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _notify.Verify(n => n.NotifyCrewAsync(It.IsAny<Guid>(), team2.Id, It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _notify.Verify(n => n.NotifyStationStaffAsync(It.IsAny<Guid>(), It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Assign_TwoTeams_EachTeamGetsOnlyItsOwnOrder()
    {
        var inc = Incident(SeverityLevel.Level3);
        var u1 = Unit(RescueUnitType.FireTruck, 2);
        var u2 = Unit(RescueUnitType.FireTruck, 3);
        var u3 = Unit(RescueUnitType.FireTruck, 4);
        var m1 = Staff("Tổ 1");
        var m2 = Staff("Tổ 2");
        var m3 = Staff("Tổ 3");
        var team1 = CrewOf(u1, m1);
        var team2 = CrewOf(u2, m2);
        var team3 = CrewOf(u3, m3);
        CrewIds(team1.Id, m1);
        CrewIds(team2.Id, m2);
        CrewIds(team3.Id, m3);
        var sent = new List<(Guid Unit, IReadOnlyCollection<Guid> Crew)>();
        _rt.Setup(x => x.JobAssignedAsync(It.IsAny<DispatchAssignmentResponseDto>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .Callback<DispatchAssignmentResponseDto, IReadOnlyCollection<Guid>, CancellationToken>((d, ids, _) => sent.Add((d.RescueUnitId, ids)));

        var r = await _svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = inc.Id, RescueUnitIds = new List<Guid> { u1.Id, u2.Id } }, _operatorId);

        Assert.True(r.Success);
        Assert.Equal(new[] { m1.Id }, sent.Single(s => s.Unit == u1.Id).Crew);
        Assert.Equal(new[] { m2.Id }, sent.Single(s => s.Unit == u2.Id).Crew);
        Assert.DoesNotContain(sent, s => s.Crew.Contains(m3.Id));
        _notify.Verify(n => n.NotifyCrewAsync(It.IsAny<Guid>(), team3.Id, It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MyAssignments_QueriesCrewScopeOfViewer_WithScopeAndLimit()
    {
        var staff = Staff("Tổ 1");
        var teamId = Guid.NewGuid();
        _users.Setup(x => x.GetTeamIdAsync(staff.Id, It.IsAny<CancellationToken>())).ReturnsAsync(teamId);
        var inc = Incident(status: IncidentStatus.Dispatched);
        var unit = Unit(RescueUnitType.FireTruck, 2, RescueUnitStatus.Dispatched, teamed: false);
        var a = new DispatchAssignment { IncidentId = inc.Id, Incident = inc, RescueUnitId = unit.Id, RescueUnit = unit, Status = DispatchAssignmentStatus.Dispatched };
        var calls = new List<(Guid Station, Guid? Team, Guid? Submitter, bool? Active, int Skip, int Take)>();
        _assign.Setup(x => x.GetCrewAssignmentsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<bool?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid?, Guid?, bool?, DateTime?, DateTime?, int, int, CancellationToken>((s, tm, sub, act, _, _, skip, take, _) => calls.Add((s, tm, sub, act, skip, take)))
            .ReturnsAsync((new List<DispatchAssignment> { a }, 1));

        var all = await _svc.GetMyAssignmentsAsync(staff.Id);
        var active = await _svc.GetMyAssignmentsAsync(staff.Id, "ACTIVE");
        var history = await _svc.GetMyAssignmentsAsync(staff.Id, "history");
        var bad = await _svc.GetMyAssignmentsAsync(staff.Id, "done");

        Assert.Equal(new[] { unit.Id }, all.Data!.Select(d => d.RescueUnitId));
        Assert.True(active.Success);
        Assert.True(history.Success);
        Assert.False(bad.Success);
        Assert.Equal(3, calls.Count);
        Assert.All(calls, c => Assert.Equal((_stationA, (Guid?)teamId, (Guid?)staff.Id), (c.Station, c.Team, c.Submitter)));
        Assert.Equal(new bool?[] { null, true, false }, calls.Select(c => c.Active));
        Assert.Equal(DispatchService.MyAssignmentsDefaultLimit, calls[0].Take);
    }

    [Fact]
    public async Task MyAssignmentHistory_PagesFinishedAssignments_AndValidatesRange()
    {
        var staff = Staff();
        (int Skip, int Take, bool? Active) call = default;
        _assign.Setup(x => x.GetCrewAssignmentsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<bool?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid?, Guid?, bool?, DateTime?, DateTime?, int, int, CancellationToken>((_, _, _, act, _, _, skip, take, _) => call = (skip, take, act))
            .ReturnsAsync((new List<DispatchAssignment>(), 45));

        var page = await _svc.GetMyAssignmentHistoryAsync(staff.Id, new MyAssignmentHistoryFilterDto { PageIndex = 3, PageSize = 500 });
        var badRange = await _svc.GetMyAssignmentHistoryAsync(staff.Id,
            new MyAssignmentHistoryFilterDto { From = DateTime.UtcNow, To = DateTime.UtcNow.AddDays(-1) });

        Assert.True(page.Success);
        Assert.Equal((200, DispatchService.MyAssignmentsMaxPageSize, (bool?)false), call);
        Assert.Equal(45, page.Data!.TotalCount);
        Assert.False(badRange.Success);
    }

    [Fact]
    public async Task MyAssignments_StaffWithoutStation_GetsEmptyList()
    {
        var staff = new User { Role = UserRole.RescueStaff, FullName = "No station" };
        _users.Setup(x => x.GetByIdAsync(staff.Id)).ReturnsAsync(staff);

        var r = await _svc.GetMyAssignmentsAsync(staff.Id);

        Assert.True(r.Success);
        Assert.Empty(r.Data!);
        _assign.Verify(x => x.GetCrewAssignmentsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<bool?>(),
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AssignmentDto_FlagsDeletedTeamAndVehicle_KeepsNames()
    {
        var (a, _, u, _) = Mission(DispatchAssignmentStatus.Completed);
        u.IsDeleted = true;
        a.StationTeamId = Guid.NewGuid();
        a.StationTeam = new StationTeam { Name = "Tổ A", IsDeleted = true };
        a.CompletionReport = new CompletionReport { StationTeam = a.StationTeam, StationTeamId = a.StationTeamId };

        var dto = (await _svc.GetAssignmentByIdAsync(a.Id)).Data!;

        Assert.Equal("Tổ A", dto.StationTeamName);
        Assert.True(dto.StationTeamDeleted);
        Assert.True(dto.RescueUnitDeleted);
        Assert.Equal(u.PlateNumber, dto.RescueUnit!.PlateNumber);
        Assert.True(dto.CompletionReport!.StationTeamDeleted);
    }

    [Fact]
    public async Task AssignmentRoute_UsesVehicleGps_OrDeviceOrigin_AndReturnsSteps()
    {
        var (a, inc, u, _) = Mission(DispatchAssignmentStatus.EnRoute);
        u.CurrentLat = 10.70;
        u.CurrentLng = 106.70;
        var calls = new List<(double Lat, double Lng)>();
        _routing.Setup(r => r.GetNavigationRouteAsync(It.IsAny<double>(), It.IsAny<double>(), inc.Latitude, inc.Longitude, It.IsAny<CancellationToken>()))
            .Callback<double, double, double, double, CancellationToken>((la, ln, _, _, _) => calls.Add((la, ln)))
            .ReturnsAsync(new RouteResult
            {
                Points = new List<RoutePointDto> { new(10.70, 106.70), new(inc.Latitude, inc.Longitude) },
                Steps = new List<RouteStepDto> { new() { Type = "depart", RoadName = "Nguyễn Thị Thập" }, new() { Type = "arrive" } },
                DistanceKm = 3.4,
                DurationMinutes = 7
            });

        var fromVehicle = await _svc.GetAssignmentRouteAsync(a.Id, null, null);
        var fromDevice = await _svc.GetAssignmentRouteAsync(a.Id, 10.71, 106.71);

        Assert.True(fromVehicle.Success);
        Assert.Equal(2, fromVehicle.Data!.Steps.Count);
        Assert.Equal(3.4, fromVehicle.Data.DistanceKm);
        Assert.Equal(new[] { (10.70, 106.70), (10.71, 106.71) }, calls);
        Assert.Equal((inc.Latitude, inc.Longitude), (fromDevice.Data!.ToLat, fromDevice.Data.ToLng));
    }

    [Theory]
    [InlineData(10.7, null)]
    [InlineData(null, 106.7)]
    [InlineData(95.0, 106.7)]
    [InlineData(10.7, 190.0)]
    public async Task AssignmentRoute_InvalidOrigin_IsRejected(double? lat, double? lng)
    {
        var (a, _, _, _) = Mission(DispatchAssignmentStatus.EnRoute);

        var r = await _svc.GetAssignmentRouteAsync(a.Id, lat, lng);

        Assert.False(r.Success);
        _routing.Verify(x => x.GetNavigationRouteAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IsCrewOfAssignment_DeletedVehicle_CrewStillAllowed()
    {
        var (a, _, u, staff) = Mission(DispatchAssignmentStatus.Completed);
        u.IsDeleted = true;   // bản xem nạp cả xe đã xóa mềm

        Assert.True(await _svc.IsCrewOfAssignmentAsync(a.Id, staff.Id));
    }

    [Fact]
    public async Task Status_OtherTeamOfSameStation_IsForbidden_ForUpdateAndReject()
    {
        var (a, _, u, _) = Mission(DispatchAssignmentStatus.Dispatched);
        CrewOf(u, Staff("Tổ 1"));
        var otherTeamMember = Staff("Tổ 2");
        CrewOf(Unit(RescueUnitType.FireTruck, 5), otherTeamMember);
        var loner = Staff("Chưa vào tổ");

        var update = await _svc.UpdateAssignmentStatusAsync(a.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.Accepted }, otherTeamMember.Id);
        var reject = await _svc.RejectAssignmentAsync(a.Id, new RejectAssignmentDto { Reason = RejectionReason.VehicleBreakdown }, otherTeamMember.Id);
        var byLoner = await _svc.UpdateAssignmentStatusAsync(a.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.Accepted }, loner.Id);

        Assert.Equal(ApiErrorCodes.Forbidden, update.ErrorCode);
        Assert.Equal(ApiErrorCodes.Forbidden, reject.ErrorCode);
        Assert.Equal(ApiErrorCodes.Forbidden, byLoner.ErrorCode);
        Assert.Equal(DispatchAssignmentStatus.Dispatched, a.Status);
    }

    [Fact]
    public async Task Status_OneMemberUpdates_WholeTeamProgresses_AndOnlyTeamIsNotified()
    {
        var (a1, inc, u1, leader) = Mission(DispatchAssignmentStatus.Accepted, IncidentStatus.InProgress);
        var u2 = Unit(RescueUnitType.LadderTruck, 3, RescueUnitStatus.Dispatched);
        var a2 = new DispatchAssignment { IncidentId = inc.Id, Incident = inc, RescueUnitId = u2.Id, RescueUnit = u2, Status = DispatchAssignmentStatus.Accepted };
        inc.DispatchAssignments.Add(a2);
        _assign.Setup(x => x.GetByIdWithDetailsAsync(a2.Id)).ReturnsAsync(a2);
        var u3 = Unit(RescueUnitType.Ambulance, 4, RescueUnitStatus.Dispatched);
        var a3 = new DispatchAssignment { IncidentId = inc.Id, Incident = inc, RescueUnitId = u3.Id, RescueUnit = u3, Status = DispatchAssignmentStatus.Accepted };
        inc.DispatchAssignments.Add(a3);
        var teammate = Staff("Đồng đội");
        var team1 = CrewOfUnits(new[] { u1, u2 }, leader, teammate);
        var otherTeam = CrewOf(u3, Staff("Tổ 2"));
        CrewIds(team1.Id, leader, teammate);
        var sent = new List<(Guid Assignment, IReadOnlyCollection<Guid> Crew)>();
        _rt.Setup(x => x.AssignmentStatusChangedAsync(It.IsAny<DispatchAssignmentResponseDto>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .Callback<DispatchAssignmentResponseDto, IReadOnlyCollection<Guid>, CancellationToken>((d, ids, _) => sent.Add((d.Id, ids)));

        var r = await _svc.UpdateAssignmentStatusAsync(a1.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.EnRoute }, leader.Id);

        Assert.True(r.Success);
        Assert.Equal(DispatchAssignmentStatus.EnRoute, a1.Status);
        Assert.Equal(DispatchAssignmentStatus.EnRoute, a2.Status);                 // xe thứ hai của tổ cùng chuyển
        Assert.Equal(DispatchAssignmentStatus.Accepted, a3.Status);                // tổ khác không bị ảnh hưởng
        Assert.Equal(new[] { a1.Id, a2.Id }.OrderBy(x => x), sent.Select(s => s.Assignment).OrderBy(x => x));
        Assert.All(sent, s => Assert.Equal(new[] { leader.Id, teammate.Id }.OrderBy(x => x), s.Crew.OrderBy(x => x)));
        _notify.Verify(n => n.GetCrewUserIdsAsync(It.IsAny<Guid>(), otherTeam.Id, It.IsAny<CancellationToken>()), Times.Never);

        // Đồng đội mở app thấy trạng thái mới và cập nhật tiếp được
        Assert.True((await _svc.UpdateAssignmentStatusAsync(a2.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.OnScene }, teammate.Id)).Success);
        Assert.Equal(DispatchAssignmentStatus.OnScene, a1.Status);
        Assert.Equal(DispatchAssignmentStatus.OnScene, a2.Status);
    }

    [Fact]
    public void CrewScope_Rules()
    {
        var team = Guid.NewGuid();
        var teamUnit = new RescueUnit { StationId = _stationA, StationTeamId = team };
        var freeUnit = new RescueUnit { StationId = _stationA };

        Assert.True(CrewScope.CanWorkOn(_stationA, team, teamUnit));
        Assert.False(CrewScope.CanWorkOn(_stationA, Guid.NewGuid(), teamUnit));   // tổ khác cùng trạm
        Assert.False(CrewScope.CanWorkOn(_stationA, null, teamUnit));             // chưa vào tổ
        Assert.True(CrewScope.CanWorkOn(_stationA, team, freeUnit));              // xe chưa gán tổ: cả trạm
        Assert.True(CrewScope.CanWorkOn(_stationA, null, freeUnit));
        Assert.False(CrewScope.CanWorkOn(_stationB, null, freeUnit));             // trạm khác
        Assert.False(CrewScope.CanWorkOn(null, null, freeUnit));
    }

    // ===================== Giao sự cố theo tổ =====================
    [Fact]
    public async Task Recommend_OnlyTeamsWithActiveStaff_UnteamedAndEmptyTeamsExcluded()
    {
        var inc = Incident();
        var staffed = Unit(RescueUnitType.FireTruck, 3);
        Unit(RescueUnitType.FireTruck, 1, teamed: false);                    // xe chưa gán tổ
        var emptyTeamUnit = Unit(RescueUnitType.FireTruck, 2, teamed: false);
        CrewOf(emptyTeamUnit);                                               // tổ không có nhân viên
        var suspendedUnit = Unit(RescueUnitType.FireTruck, 2.5, teamed: false);
        CrewOf(suspendedUnit, new User { Role = UserRole.RescueStaff, Status = UserStatus.Suspended, StationId = _stationA });

        var r = await _svc.GetRecommendationsAsync(inc.Id);

        var only = Assert.Single(r.Data!);
        Assert.Equal(staffed.Id, only.RescueUnitId);
        Assert.Equal(staffed.StationTeamId, only.StationTeamId);
        Assert.Equal(1, only.TeamStaffCount);
    }

    [Fact]
    public async Task Recommend_TeamOnAnotherMission_IsBusyEvenWithFreeVehicle()
    {
        var inc = Incident();
        var onMission = Unit(RescueUnitType.FireTruck, 1, teamed: false);
        var spare = Unit(RescueUnitType.FireTruck, 2, teamed: false);
        CrewOfUnits(new[] { onMission, spare }, Staffer(_stationA));
        _active.Add(new DispatchAssignment { RescueUnitId = onMission.Id, IncidentId = Guid.NewGuid(), Status = DispatchAssignmentStatus.EnRoute });
        var other = Unit(RescueUnitType.FireTruck, 5);

        var r = await _svc.GetRecommendationsAsync(inc.Id);

        Assert.Equal(new[] { other.Id }, r.Data!.Select(x => x.RescueUnitId));
    }

    [Fact]
    public async Task Recommend_TeamWithTwoVehiclesOfSameFunction_ListedOnceWithNearest()
    {
        var inc = Incident();
        var far = Unit(RescueUnitType.FireTruck, 6, teamed: false);
        var near = Unit(RescueUnitType.FireTruck, 2, teamed: false);
        CrewOfUnits(new[] { far, near }, Staffer(_stationA));

        var r = await _svc.GetRecommendationsAsync(inc.Id);

        Assert.Equal(new[] { near.Id }, r.Data!.Select(x => x.RescueUnitId));
    }

    [Fact]
    public async Task Assign_ByTeam_PicksTeamsNearestVehicleOfFunction_AndStoresTeam()
    {
        var inc = Incident(SeverityLevel.Level3);
        var fireFar = Unit(RescueUnitType.FireTruck, 6, teamed: false);
        var fireNear = Unit(RescueUnitType.FireTruck, 2, teamed: false);
        var ambulance = Unit(RescueUnitType.Ambulance, 1, teamed: false);
        var team = CrewOfUnits(new[] { fireFar, fireNear, ambulance }, Staffer(_stationA));

        var r = await _svc.AssignUnitAsync(new AssignDispatchDto
        {
            IncidentId = inc.Id,
            Teams = new() { new TeamAssignmentDto { TeamId = team.Id, UnitType = RescueUnitType.FireTruck } }
        }, _operatorId);

        Assert.True(r.Success, r.Message);
        var assignment = Assert.Single(inc.DispatchAssignments);
        Assert.Equal(fireNear.Id, assignment.RescueUnitId);
        Assert.Equal(team.Id, assignment.StationTeamId);
        Assert.Equal(team.Name, r.Data!.Single().StationTeamName);
    }

    [Fact]
    public async Task Assign_TwoVehiclesOfSameTeam_IsRejected()
    {
        var inc = Incident(SeverityLevel.Level3);
        var fire = Unit(RescueUnitType.FireTruck, 2, teamed: false);
        var ambulance = Unit(RescueUnitType.Ambulance, 3, teamed: false);
        CrewOfUnits(new[] { fire, ambulance }, Staffer(_stationA));

        var r = await _svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = inc.Id, RescueUnitIds = new() { fire.Id, ambulance.Id } }, _operatorId);

        Assert.False(r.Success);
        Assert.Empty(inc.DispatchAssignments);
        _units.Verify(u => u.TryReserveForDispatchAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Assign_UnteamedVehicleOrTeamWithoutStaff_IsRejected()
    {
        var inc = Incident();
        var unteamed = Unit(RescueUnitType.FireTruck, 2, teamed: false);
        var noStaff = Unit(RescueUnitType.FireTruck, 3, teamed: false);
        CrewOf(noStaff);

        var r1 = await _svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = inc.Id, RescueUnitId = unteamed.Id }, _operatorId);
        var r2 = await _svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = inc.Id, RescueUnitId = noStaff.Id }, _operatorId);

        Assert.False(r1.Success);
        Assert.False(r2.Success);
        Assert.Empty(inc.DispatchAssignments);
    }

    [Fact]
    public async Task Assign_TeamAlreadyOnAnotherIncident_IsConflict()
    {
        var inc = Incident();
        var onMission = Unit(RescueUnitType.Ambulance, 1, teamed: false);
        var spare = Unit(RescueUnitType.FireTruck, 2, teamed: false);
        CrewOfUnits(new[] { onMission, spare }, Staffer(_stationA));
        _active.Add(new DispatchAssignment { RescueUnitId = onMission.Id, IncidentId = Guid.NewGuid(), Status = DispatchAssignmentStatus.OnScene });

        var r = await _svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = inc.Id, RescueUnitId = spare.Id }, _operatorId);

        Assert.Equal(ApiErrorCodes.Conflict, r.ErrorCode);
        Assert.Empty(inc.DispatchAssignments);
    }

    // ===================== Lượt cuối kết thúc không qua nghiệm thu =====================
    private DispatchAssignment CompletedSibling(Incident inc, string summary = "Đã dập tắt đám cháy")
    {
        var done = new DispatchAssignment
        {
            IncidentId = inc.Id, RescueUnitId = Guid.NewGuid(), Status = DispatchAssignmentStatus.Completed,
            CompletionReport = new CompletionReport { ActionsSummary = summary, SubmittedAt = DateTime.UtcNow.AddMinutes(-1) }
        };
        inc.DispatchAssignments.Add(done);
        return done;
    }

    [Fact]
    public async Task Cancel_LastActive_AfterOtherTeamCompleted_ClosesIncident()
    {
        var (a, inc, u, _) = Mission(DispatchAssignmentStatus.Accepted, IncidentStatus.InProgress);
        CompletedSibling(inc);
        inc.ReportedByUserId = Guid.NewGuid();
        var op = new User { Role = UserRole.Operator, StationId = _stationA, FullName = "Op" };
        _users.Setup(x => x.GetByIdAsync(op.Id)).ReturnsAsync(op);

        var r = await _svc.UpdateAssignmentStatusAsync(a.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.Cancelled }, op.Id);

        Assert.True(r.Success, r.Message);
        Assert.Equal(IncidentStatus.Completed, inc.Status);
        Assert.Equal("Đã dập tắt đám cháy", inc.ResolutionSummary);
        Assert.Equal(op.Id, inc.ClosedByUserId);
        Assert.NotNull(inc.ResolutionTimeMs);
        Assert.Equal(RescueUnitStatus.Available, u.Status);
        // 1 lần ghi lượt + xe (khóa sự cố), 1 lần chốt đóng sự cố
        _lock.Verify(l => l.CommitAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        _notify.Verify(n => n.NotifyUsersAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(inc.ReportedByUserId!.Value)),
            It.Is<NotificationRequest>(m => m.Title.Vi == "Cứu hộ đã hoàn tất"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_OnlyTeam_NoneCompleted_ReturnsIncidentToQueue()
    {
        var (a, inc, _, _) = Mission(DispatchAssignmentStatus.Dispatched);
        var op = new User { Role = UserRole.Operator, StationId = _stationA, FullName = "Op" };
        _users.Setup(x => x.GetByIdAsync(op.Id)).ReturnsAsync(op);

        await _svc.UpdateAssignmentStatusAsync(a.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.Cancelled }, op.Id);

        Assert.Equal(IncidentStatus.Verified, inc.Status);
        Assert.Null(inc.ClosedAt);
    }

    [Fact]
    public async Task Cancel_WhileOtherTeamStillActive_KeepsIncidentOpen()
    {
        var (a, inc, _, _) = Mission(DispatchAssignmentStatus.Accepted, IncidentStatus.InProgress);
        CompletedSibling(inc);
        inc.DispatchAssignments.Add(new DispatchAssignment { IncidentId = inc.Id, Status = DispatchAssignmentStatus.OnScene });
        var op = new User { Role = UserRole.Operator, StationId = _stationA, FullName = "Op" };
        _users.Setup(x => x.GetByIdAsync(op.Id)).ReturnsAsync(op);

        await _svc.UpdateAssignmentStatusAsync(a.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.Cancelled }, op.Id);

        Assert.Equal(IncidentStatus.InProgress, inc.Status);
    }

    [Fact]
    public async Task Reject_LastActive_AfterOtherTeamCompleted_ClosesInsteadOfRequeue()
    {
        var (a, inc, u, staff) = Mission(DispatchAssignmentStatus.EnRoute, IncidentStatus.InProgress);
        CompletedSibling(inc, "Sơ cứu xong, bàn giao 115");

        var r = await _svc.RejectAssignmentAsync(a.Id, new RejectAssignmentDto { Reason = RejectionReason.VehicleBreakdown }, staff.Id);

        Assert.True(r.Success);
        Assert.Equal(IncidentStatus.Completed, inc.Status);
        Assert.Equal("Sơ cứu xong, bàn giao 115", inc.ResolutionSummary);
        Assert.Equal(RescueUnitStatus.Maintenance, u.Status);
    }

    [Fact]
    public async Task Cancel_IncidentAlreadyClosed_DoesNothing()
    {
        var (a, inc, _, _) = Mission(DispatchAssignmentStatus.Accepted, IncidentStatus.InProgress);
        CompletedSibling(inc);
        var op = new User { Role = UserRole.Operator, StationId = _stationA, FullName = "Op" };
        _users.Setup(x => x.GetByIdAsync(op.Id)).ReturnsAsync(op);
        // Tổ khác vừa nghiệm thu đóng sự cố ngay trước khi khóa (đọc lại thấy Completed)
        _incidents.Setup(i => i.ReloadAsync(inc, It.IsAny<CancellationToken>()))
            .Callback(() => inc.Status = IncidentStatus.Completed)
            .Returns(Task.CompletedTask);

        await _svc.UpdateAssignmentStatusAsync(a.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.Cancelled }, op.Id);

        Assert.Null(inc.ClosedAt);
        // Chỉ commit phần ghi lượt + xe; bước chốt thấy sự cố đã đóng nên không ghi gì
        _lock.Verify(l => l.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnitStatusSaves_OnlyStatusColumns_NeverWholeRow()
    {
        var (a, _, u, staff) = Mission(DispatchAssignmentStatus.Dispatched);

        await _svc.UpdateAssignmentStatusAsync(a.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.Accepted }, staff.Id);
        await _svc.UpdateAssignmentStatusAsync(a.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.EnRoute }, staff.Id);

        _units.Verify(x => x.SaveStatusAsync(u, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _units.Verify(x => x.UpdateAsync(It.IsAny<RescueUnit>()), Times.Never);
    }

    // ===================== Lịch sử báo cáo nghiệm thu =====================
    [Fact]
    public async Task CompletionReports_Operator_IsForcedToOwnStation()
    {
        Guid? station = null;
        _assign.Setup(a => a.GetCompletionReportsAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback((Guid? st, Guid? _, Guid? _, Guid? _, Guid? _, Guid? _, DateTime? _, DateTime? _, string? _, int _, int _, CancellationToken _) => station = st)
            .ReturnsAsync((new List<CompletionReport>(), 0));

        var r = await _svc.GetCompletionReportsAsync(new CompletionReportFilterDto { StationId = Guid.NewGuid(), PageSize = 1000 }, Guid.NewGuid());

        Assert.True(r.Success);
        Assert.Equal(_stationA, station);
        Assert.Equal(100, r.Data!.PageSize);
    }

    [Fact]
    public async Task CompletionReports_RescueStaff_SeesOwnTeamOrOwnSubmissions()
    {
        var staff = Staff("Tổ 1");
        var teamId = Guid.NewGuid();
        _me.SetupGet(m => m.Role).Returns(UserRole.RescueStaff);
        _users.Setup(x => x.GetTeamIdAsync(staff.Id, It.IsAny<CancellationToken>())).ReturnsAsync(teamId);
        _me.SetupGet(m => m.StationId).Returns(_stationA);
        (Guid? st, Guid? ct, Guid? cu, Guid? cs) seen = default;
        _assign.Setup(a => a.GetCompletionReportsAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback((Guid? s, Guid? _, Guid? _, Guid? c, Guid? u, Guid? cs, DateTime? _, DateTime? _, string? _, int _, int _, CancellationToken _) => seen = (s, c, u, cs))
            .ReturnsAsync((new List<CompletionReport>(), 0));

        await _svc.GetCompletionReportsAsync(new CompletionReportFilterDto(), staff.Id);

        Assert.Null(seen.st);
        Assert.Equal(teamId, seen.ct);
        Assert.Equal(staff.Id, seen.cu);
        Assert.Equal(_stationA, seen.cs); // + báo cáo của xe chưa gán tổ ở trạm mình
    }

    [Fact]
    public async Task CompletionReports_MapsIncidentTeamAndUnits()
    {
        var inc = Incident(status: IncidentStatus.Completed);
        var unit = Unit(RescueUnitType.FireTruck, 2, RescueUnitStatus.Available, teamed: false);
        var submitter = new User { FullName = "Nguyễn Văn A" };
        var report = new CompletionReport
        {
            IncidentId = inc.Id, Incident = inc, ActionsSummary = "Đã dập tắt đám cháy", SubmittedByUserId = submitter.Id, SubmittedByUser = submitter,
            MediaItems = { new CompletionReportMedia { MediaUrl = "https://x/1.jpg" } }
        };
        report.Assignments.Add(new DispatchAssignment { IncidentId = inc.Id, RescueUnitId = unit.Id, RescueUnit = unit, Status = DispatchAssignmentStatus.Completed });
        _assign.Setup(a => a.GetCompletionReportsAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CompletionReport> { report }, 1));

        var item = (await _svc.GetCompletionReportsAsync(new CompletionReportFilterDto(), Guid.NewGuid())).Data!.Items.Single();

        Assert.Equal(inc.Id, item.IncidentId);
        Assert.Equal(IncidentStatus.Completed, item.IncidentStatus);
        Assert.Equal("Nguyễn Văn A", item.SubmittedByName);
        Assert.Equal(new[] { "https://x/1.jpg" }, item.EvidenceMediaUrls);
        Assert.Equal(unit.PlateNumber, item.Units.Single().PlateNumber);
    }

    [Fact]
    public async Task CompletionReportById_OtherStationForOperator_IsForbidden()
    {
        var report = new CompletionReport
        {
            IncidentId = Guid.NewGuid(),
            Incident = new Incident { ResponsibleStationId = Guid.NewGuid() },
            StationTeam = new StationTeam { StationId = Guid.NewGuid(), Name = "Tổ X" }
        };
        _assign.Setup(a => a.GetCompletionReportByIdAsync(report.Id, It.IsAny<CancellationToken>())).ReturnsAsync(report);

        var r = await _svc.GetCompletionReportByIdAsync(report.Id, Guid.NewGuid());

        Assert.False(r.Success);
        Assert.Equal(ApiErrorCodes.Forbidden, r.ErrorCode);
    }

    [Fact]
    public async Task CompletionReportById_RescueStaff_OwnTeam_IsAllowed_OtherTeam_IsForbidden()
    {
        var staff = Staff("Tổ 1");
        var teamId = Guid.NewGuid();
        _me.SetupGet(m => m.Role).Returns(UserRole.RescueStaff);
        _users.Setup(x => x.GetTeamIdAsync(staff.Id, It.IsAny<CancellationToken>())).ReturnsAsync(teamId);
        var mine = new CompletionReport { StationTeamId = teamId };
        var other = new CompletionReport { StationTeamId = Guid.NewGuid() };
        _assign.Setup(a => a.GetCompletionReportByIdAsync(mine.Id, It.IsAny<CancellationToken>())).ReturnsAsync(mine);
        _assign.Setup(a => a.GetCompletionReportByIdAsync(other.Id, It.IsAny<CancellationToken>())).ReturnsAsync(other);

        Assert.True((await _svc.GetCompletionReportByIdAsync(mine.Id, staff.Id)).Success);
        Assert.Equal(ApiErrorCodes.Forbidden, (await _svc.GetCompletionReportByIdAsync(other.Id, staff.Id)).ErrorCode);
    }

    // Sự cố đã đóng mà không có lượt nghiệm thu: tracking không được trả PendingDispatch (client hiện nhầm "đang xử lý")
    [Theory]
    [InlineData(IncidentStatus.Completed, DispatchAssignmentStatus.Cancelled)]
    [InlineData(IncidentStatus.Cancelled, DispatchAssignmentStatus.Rejected)]
    public async Task CitizenTracking_ClosedIncidentWithoutCompletedUnit_ReportsIncidentStatus(IncidentStatus incStatus, DispatchAssignmentStatus assignmentStatus)
    {
        var (_, inc, _, _) = Mission(assignmentStatus, incStatus);

        var r = await _svc.GetCitizenTrackingAsync(inc.Id, onlyAcceptedUnits: true);

        Assert.Empty(r.Data!.Units);
        Assert.Equal(incStatus.ToString(), r.Data.DispatchStatus);
        Assert.Equal(incStatus.ToString(), r.Data.IncidentStatus);
    }

    [Fact]
    public async Task CitizenTracking_CompletedIncident_ShowsCompletedUnit()
    {
        var (a, inc, u, _) = Mission(DispatchAssignmentStatus.Completed, IncidentStatus.Completed);

        var r = await _svc.GetCitizenTrackingAsync(inc.Id, onlyAcceptedUnits: true);

        Assert.Equal("Completed", r.Data!.DispatchStatus);
        Assert.Equal(u.PlateNumber, Assert.Single(r.Data.Units).PlateNumber);
    }

    // ===================== Tự rút lệnh khi tổ không phản hồi (25/11/2026) =====================
    private (DispatchAssignment A, Incident I, RescueUnit U, StationTeam Team) UnansweredOrder(double minutesAgo, DispatchAssignmentStatus status = DispatchAssignmentStatus.Dispatched)
    {
        var u = Unit(RescueUnitType.FireTruck, 2, RescueUnitStatus.Dispatched);
        var team = _crews.Single(c => c.Id == u.StationTeamId);
        var second = Staffer(_stationA);
        team.Members.Add(new StationTeamMember { TeamId = team.Id, StaffId = second.Id, Staff = second });
        var inc = Incident(status: IncidentStatus.Dispatched);
        var a = new DispatchAssignment
        {
            IncidentId = inc.Id, Incident = inc, RescueUnitId = u.Id, RescueUnit = u, StationTeamId = team.Id,
            Status = status, DispatchedAt = DateTime.UtcNow.AddMinutes(-minutesAgo)
        };
        inc.DispatchAssignments.Add(a);
        _assign.Setup(x => x.GetByIncidentIdAsync(inc.Id)).ReturnsAsync(() => inc.DispatchAssignments.ToList());
        _assign.Setup(x => x.GetIncidentIdsWithUnacceptedBeforeAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { inc.Id });
        _notify.Setup(n => n.GetCrewUserIdsAsync(_stationA, team.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(team.Members.Select(m => m.StaffId).ToList());
        return (a, inc, u, team);
    }

    [Fact]
    public async Task UnacceptedOrder_PastTimeout_IsWithdrawn_UnitReleased_IncidentRequeued_OperatorToldTeamMembersAndPlates()
    {
        var (a, inc, u, team) = UnansweredOrder(minutesAgo: 4);
        var opOwn = new User { Role = UserRole.Operator, StationId = _stationA, Status = UserStatus.Active };
        var opOther = new User { Role = UserRole.Operator, StationId = _stationB, Status = UserStatus.Active };
        _users.Setup(x => x.GetActiveByRolesAsync(It.IsAny<IReadOnlyCollection<UserRole>>())).ReturnsAsync(new List<User> { opOwn, opOther });
        NotificationRequest? alert = null;
        IReadOnlyCollection<Guid>? alertTo = null;
        _notify.Setup(n => n.NotifyUsersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.Is<NotificationRequest>(x => x.Title.Vi == "TỔ KHÔNG PHẢN HỒI LỆNH ĐIỀU PHỐI"), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyCollection<Guid> ids, NotificationRequest n, CancellationToken _) => (alertTo, alert) = (ids, n))
            .Returns(Task.CompletedTask);

        Assert.Equal(1, await _svc.ExpireUnacceptedAssignmentsAsync());

        Assert.Equal(DispatchAssignmentStatus.Rejected, a.Status);
        Assert.Equal(RejectionReason.NoResponse, a.RejectionReason);
        Assert.Equal(RescueUnitStatus.Available, u.Status);
        Assert.Equal(IncidentStatus.Verified, inc.Status);
        _assign.Verify(x => x.AddStatusHistoryAsync(It.Is<AssignmentStatusHistory>(h => h.RejectionReason == RejectionReason.NoResponse && h.ChangedByUserId == null)), Times.Once);
        _incidents.Verify(x => x.AddAuditTrailAsync(It.Is<IncidentAuditTrail>(t => t.ActorRole == DispatchService.SystemActorRole && t.Description.Contains("không phản hồi")), It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal(new[] { opOwn.Id }, alertTo);
        Assert.Contains(team.Name, alert!.Message.Vi);
        Assert.Contains("(2 thành viên)", alert.Message.Vi);
        Assert.Contains(u.PlateNumber, alert.Message.Vi);
        Assert.Contains("về hàng chờ", alert.Message.Vi);
        _notify.Verify(n => n.NotifyCrewAsync(_stationA, team.Id, It.Is<NotificationRequest>(x => x.Title.Vi == "LỆNH ĐIỀU PHỐI ĐÃ BỊ RÚT"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(2, DispatchAssignmentStatus.Dispatched)]   // chưa quá 3 phút
    [InlineData(10, DispatchAssignmentStatus.Accepted)]    // tổ đã nhận lệnh
    public async Task UnacceptedOrder_NotDueOrAlreadyAccepted_IsLeftAlone(double minutesAgo, DispatchAssignmentStatus status)
    {
        var (a, inc, u, _) = UnansweredOrder(minutesAgo, status);

        Assert.Equal(0, await _svc.ExpireUnacceptedAssignmentsAsync());

        Assert.Equal(status, a.Status);
        Assert.Equal(RescueUnitStatus.Dispatched, u.Status);
        Assert.Equal(IncidentStatus.Dispatched, inc.Status);
    }

    [Fact]
    public async Task UnacceptedOrder_TimeoutZero_FeatureOff()
    {
        _settings.Setup(s => s.GetEscalationSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new EscalationSettingsDto { AcceptTimeoutMinutes = 0 });
        var (a, _, _, _) = UnansweredOrder(minutesAgo: 30);

        Assert.Equal(0, await _svc.ExpireUnacceptedAssignmentsAsync());
        Assert.Equal(DispatchAssignmentStatus.Dispatched, a.Status);
    }

    // ===================== Chỉ người báo gốc nhận thông báo tiến độ; báo trùng giữ realtime theo dõi =====================
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Dispatch_OnlyOriginalReporterGetsNotification_ConfirmedDuplicateIsExcluded(bool hasOriginalReporter)
    {
        Guid? reporter = hasOriginalReporter ? Guid.NewGuid() : null;
        var duplicateReporter = Guid.NewGuid();
        var inc = Incident(tweak: i => i.ReportedByUserId = reporter);
        _incidents.Setup(r => r.GetConfirmedDuplicateReporterIdsAsync(inc.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { duplicateReporter });
        var u = Unit(RescueUnitType.FireTruck, 2);
        var recipients = new List<Guid>();
        _notify.Setup(n => n.NotifyUsersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.Is<NotificationRequest>(r => r.Type == NotificationType.StatusUpdate), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyCollection<Guid> ids, NotificationRequest _, CancellationToken _) => recipients.AddRange(ids))
            .Returns(Task.CompletedTask);

        Assert.True((await _svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = inc.Id, RescueUnitId = u.Id }, _operatorId)).Success);

        Assert.Equal(reporter is Guid owner ? new[] { owner } : Array.Empty<Guid>(), recipients);
        Assert.DoesNotContain(duplicateReporter, recipients);
    }

    [Theory]
    [InlineData(DispatchAssignmentStatus.Dispatched, DispatchAssignmentStatus.Accepted, true)]
    [InlineData(DispatchAssignmentStatus.Accepted, DispatchAssignmentStatus.EnRoute, true)]
    [InlineData(DispatchAssignmentStatus.EnRoute, DispatchAssignmentStatus.OnScene, true)]
    [InlineData(DispatchAssignmentStatus.Dispatched, DispatchAssignmentStatus.Accepted, false)]
    [InlineData(DispatchAssignmentStatus.Accepted, DispatchAssignmentStatus.EnRoute, false)]
    [InlineData(DispatchAssignmentStatus.EnRoute, DispatchAssignmentStatus.OnScene, false)]
    public async Task Progress_OnlyOriginalReporterGetsNotification_TrackingRealtimeIsPreserved(
        DispatchAssignmentStatus previous, DispatchAssignmentStatus next, bool hasOriginalReporter)
    {
        var (assignment, incident, _, staff) = Mission(previous);
        Guid? reporter = hasOriginalReporter ? Guid.NewGuid() : null;
        incident.ReportedByUserId = reporter;
        var duplicateReporter = Guid.NewGuid();
        _incidents.Setup(r => r.GetConfirmedDuplicateReporterIdsAsync(incident.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { duplicateReporter });
        var recipients = new List<Guid>();
        _notify.Setup(n => n.NotifyUsersAsync(It.IsAny<IReadOnlyCollection<Guid>>(),
                It.Is<NotificationRequest>(r => r.Type == NotificationType.StatusUpdate), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyCollection<Guid> ids, NotificationRequest _, CancellationToken _) => recipients.AddRange(ids))
            .Returns(Task.CompletedTask);

        var result = await _svc.UpdateAssignmentStatusAsync(assignment.Id, new UpdateAssignmentStatusDto { Status = next }, staff.Id);

        Assert.True(result.Success);
        Assert.Equal(reporter is Guid owner ? new[] { owner } : Array.Empty<Guid>(), recipients);
        Assert.DoesNotContain(duplicateReporter, recipients);
        _rt.Verify(r => r.AssignmentStatusChangedAsync(
            It.Is<DispatchAssignmentResponseDto>(d => d.Id == assignment.Id && d.Status == next),
            It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Completion_OnlyOriginalReporterGetsNotification_ConfirmedDuplicateIsExcluded(bool hasOriginalReporter)
    {
        var (assignment, incident, _, staff) = Mission(DispatchAssignmentStatus.OnScene, IncidentStatus.InProgress);
        Guid? reporter = hasOriginalReporter ? Guid.NewGuid() : null;
        incident.ReportedByUserId = reporter;
        incident.LinkedReportCount = 1;
        var duplicateReporter = Guid.NewGuid();
        _incidents.Setup(r => r.GetConfirmedDuplicateReporterIdsAsync(incident.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { duplicateReporter });
        var recipients = new List<Guid>();
        _notify.Setup(n => n.NotifyUsersAsync(It.IsAny<IReadOnlyCollection<Guid>>(),
                It.Is<NotificationRequest>(r => r.Type == NotificationType.StatusUpdate), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyCollection<Guid> ids, NotificationRequest _, CancellationToken _) => recipients.AddRange(ids))
            .Returns(Task.CompletedTask);

        var result = await _svc.ResolveAssignmentAsync(assignment.Id, Report("https://x/a.jpg"), staff.Id);

        Assert.True(result.Success);
        Assert.Equal(IncidentStatus.Completed, incident.Status);
        Assert.Equal(reporter is Guid owner ? new[] { owner } : Array.Empty<Guid>(), recipients);
        Assert.DoesNotContain(duplicateReporter, recipients);
        _followUp.Verify(f => f.CloseLinkedReportsAsync(incident.Id, staff.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ===================== Người dân chỉ thấy xe khi tổ đã nhận lệnh (23/11/2026) =====================
    [Fact]
    public async Task CitizenTracking_HidesUnitUntilCrewAccepts_OperatorStillSeesIt()
    {
        var (a, inc, u, staff) = Mission(DispatchAssignmentStatus.Dispatched);

        var citizen = await _svc.GetCitizenTrackingAsync(inc.Id, onlyAcceptedUnits: true);
        Assert.Empty(citizen.Data!.Units);
        Assert.Null(citizen.Data.PlateNumber);
        Assert.Equal("PendingDispatch", citizen.Data.DispatchStatus);

        var op = await _svc.GetCitizenTrackingAsync(inc.Id);
        Assert.Equal(u.PlateNumber, Assert.Single(op.Data!.Units).PlateNumber);

        await _svc.UpdateAssignmentStatusAsync(a.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.Accepted }, staff.Id);
        var accepted = await _svc.GetCitizenTrackingAsync(inc.Id, onlyAcceptedUnits: true);
        Assert.Equal(u.PlateNumber, accepted.Data!.PlateNumber);
        Assert.Equal("Accepted", accepted.Data.DispatchStatus);
    }

    [Fact]
    public async Task CitizenNotification_NoPlateOnDispatch_PlateSentOnAccept()
    {
        var reporter = Guid.NewGuid();
        var inc = Incident(tweak: i => i.ReportedByUserId = reporter);
        var u = Unit(RescueUnitType.FireTruck, 2);
        var sent = new List<NotificationRequest>();
        _notify.Setup(n => n.NotifyUsersAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(reporter)), It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback((IReadOnlyCollection<Guid> _, NotificationRequest req, CancellationToken _) => sent.Add(req))
            .Returns(Task.CompletedTask);

        Assert.True((await _svc.AssignUnitAsync(new AssignDispatchDto { IncidentId = inc.Id, RescueUnitId = u.Id }, _operatorId)).Success);
        var dispatched = Assert.Single(sent);
        Assert.DoesNotContain(u.PlateNumber, dispatched.Message.Vi);

        var a = inc.DispatchAssignments.Single();
        a.RescueUnit ??= u;
        _assign.Setup(x => x.GetByIdWithDetailsAsync(a.Id)).ReturnsAsync(a);
        _assign.Setup(x => x.GetByIncidentIdAsync(inc.Id)).ReturnsAsync(() => inc.DispatchAssignments.ToList());
        var staff = _crews.Single(c => c.Id == u.StationTeamId).Members.Single().Staff!;
        _users.Setup(x => x.GetByIdAsync(staff.Id)).ReturnsAsync(staff);

        var accept = await _svc.UpdateAssignmentStatusAsync(a.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.Accepted }, staff.Id);
        Assert.True(accept.Success, accept.Message);
        Assert.Equal(2, sent.Count);
        Assert.Contains(u.PlateNumber, sent[1].Message.Vi);
    }
}
