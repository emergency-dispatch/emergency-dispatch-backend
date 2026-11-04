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
        _me.SetupGet(m => m.Role).Returns(UserRole.Operator);
        _me.SetupGet(m => m.StationId).Returns(_stationA);
        _svc = new DispatchService(_assign.Object, _units.Object, _incidents.Object, _users.Object, _rt.Object, _settings.Object,
            _notify.Object, NullLogger<DispatchService>.Instance, _routing.Object, _me.Object, _esc.Object, _followUp.Object, _fieldRequests.Object,
            TestHelpers.Config(("Cloudinary:CloudName", "demo")), _teams.Object);
    }

    private RescueUnit Unit(RescueUnitType type, double kmNorth, RescueUnitStatus status = RescueUnitStatus.Available, Guid? station = null)
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
        return u;
    }

    private Incident Incident(SeverityLevel sev = SeverityLevel.Level2, IncidentEventType type = IncidentEventType.FireExplosion,
        IncidentStatus status = IncidentStatus.Verified, Action<Incident>? tweak = null)
    {
        var inc = new Incident
        {
            Title = "Cháy", Latitude = Lat, Longitude = Lng, Severity = sev, EventType = type, Status = status,
            ResponsibleStationId = _stationA, VerifiedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        };
        tweak?.Invoke(inc);
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
        _rt.Verify(n => n.JobAssignedAsync(It.IsAny<DispatchAssignmentResponseDto>(), It.IsAny<CancellationToken>()), Times.Once);
        _notify.Verify(n => n.NotifyStationStaffAsync(_stationA, It.IsAny<EmergencyDispatch.Application.DTOs.Notification.NotificationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
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
        var u = Unit(RescueUnitType.FireTruck, 2, RescueUnitStatus.Dispatched);
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
        var op = new User { Role = UserRole.Operator };
        _users.Setup(x => x.GetByIdAsync(op.Id)).ReturnsAsync(op);

        var byStaff = await _svc.UpdateAssignmentStatusAsync(a.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.Cancelled }, staff.Id);
        var byOp = await _svc.UpdateAssignmentStatusAsync(a.Id, new UpdateAssignmentStatusDto { Status = DispatchAssignmentStatus.Cancelled }, op.Id);

        Assert.Equal(ApiErrorCodes.Forbidden, byStaff.ErrorCode);
        Assert.True(byOp.Success);
        Assert.Equal(RescueUnitStatus.Available, u.Status);
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
        _notify.Setup(n => n.NotifyUsersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.Is<NotificationRequest>(r => r.Title == "TỔ ĐÃ NGHIỆM THU NHIỆM VỤ"), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<Guid>, NotificationRequest, CancellationToken>((ids, _, _) => notified = ids);

        var r = await _svc.ResolveAssignmentAsync(a.Id, Report("https://x/a.jpg"), staff.Id);

        Assert.True(r.Success);
        Assert.Equal(DispatchAssignmentStatus.Completed, a.Status);
        Assert.Equal(IncidentStatus.Completed, inc.Status);
        Assert.Equal(new[] { driver.Id, medic.Id }.OrderBy(x => x), notified!.OrderBy(x => x));
        _rt.Verify(x => x.AssignmentStatusChangedAsync(It.Is<DispatchAssignmentResponseDto>(d => d.Status == DispatchAssignmentStatus.Completed), It.IsAny<CancellationToken>()), Times.Once);
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
        _notify.Verify(n => n.NotifyUsersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.Is<NotificationRequest>(r => r.Title == "TỔ ĐÃ NGHIỆM THU NHIỆM VỤ"), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_CrewWithTwoUnits_OneSignatureClosesAllItsUnits_OtherCrewStillPending()
    {
        // Tổ 1 có 2 xe: xe chữa cháy đã tới (OnScene), xe thang còn trên đường (EnRoute). Tổ 2 có 1 xe cấp cứu.
        var (a1, inc, u1, leader) = Mission(DispatchAssignmentStatus.OnScene, IncidentStatus.InProgress);
        DispatchAssignment AddUnit(RescueUnitType type, DispatchAssignmentStatus status, out RescueUnit unit)
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
        _rt.Verify(x => x.AssignmentStatusChangedAsync(It.Is<DispatchAssignmentResponseDto>(d => d.Status == DispatchAssignmentStatus.Completed), It.IsAny<CancellationToken>()), Times.Exactly(2));

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
}
