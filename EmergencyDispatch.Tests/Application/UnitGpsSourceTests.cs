using EmergencyDispatch.Application.DTOs.RescueUnit;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Moq;

namespace EmergencyDispatch.Tests.Application;

/// <summary>
/// Mỗi xe một nguồn GPS: người nhận lệnh ưu tiên, thành viên khác thay khi nguồn im lặng, bỏ tọa độ nhảy xa
/// </summary>
public class UnitGpsSourceTests
{
    private static readonly DateTime T0 = new(2026, 11, 26, 8, 0, 0, DateTimeKind.Utc);
    private readonly Guid _unit = Guid.NewGuid();
    private readonly Guid _assignment = Guid.NewGuid();
    private readonly Guid _driver = Guid.NewGuid();
    private readonly Guid _mate = Guid.NewGuid();

    [Fact]
    public void Tracker_OtherMember_IgnoredWhileSourceSends_TakesOverAfterSilence()
    {
        var t = new UnitGpsSourceTracker();

        Assert.Equal(GpsSampleDecision.Accepted, t.Evaluate(_unit, _assignment, _driver, _driver, 10.80, 106.70, T0));
        Assert.Equal(GpsSampleDecision.NotPrimarySource, t.Evaluate(_unit, _assignment, _mate, _driver, 10.80, 106.70, T0.AddSeconds(4)));
        Assert.Equal(GpsSampleDecision.Accepted, t.Evaluate(_unit, _assignment, _driver, _driver, 10.801, 106.70, T0.AddSeconds(8)));
        // Người nhận lệnh im lặng quá 30 giây → thành viên còn lại làm nguồn
        Assert.Equal(GpsSampleDecision.Accepted, t.Evaluate(_unit, _assignment, _mate, _driver, 10.802, 106.70, T0.AddSeconds(45)));
        Assert.Equal(GpsSampleDecision.NotPrimarySource, t.Evaluate(_unit, _assignment, Guid.NewGuid(), _driver, 10.802, 106.70, T0.AddSeconds(49)));
    }

    [Fact]
    public void Tracker_Accepter_TakesBackSourceAnytime()
    {
        var t = new UnitGpsSourceTracker();
        Assert.Equal(GpsSampleDecision.Accepted, t.Evaluate(_unit, _assignment, _mate, _driver, 10.80, 106.70, T0));

        Assert.Equal(GpsSampleDecision.Accepted, t.Evaluate(_unit, _assignment, _driver, _driver, 10.80, 106.70, T0.AddSeconds(4)));
        Assert.Equal(GpsSampleDecision.NotPrimarySource, t.Evaluate(_unit, _assignment, _mate, _driver, 10.80, 106.70, T0.AddSeconds(8)));
    }

    [Fact]
    public void Tracker_ImplausibleJump_IsIgnored_JitterAndNormalDrivingAccepted()
    {
        var t = new UnitGpsSourceTracker();
        t.Evaluate(_unit, _assignment, _driver, _driver, 10.8075, 106.7130, T0);

        // ~11 km sau 4 giây (máy test đặt nơi khác)
        Assert.Equal(GpsSampleDecision.ImplausibleJump, t.Evaluate(_unit, _assignment, _driver, _driver, 10.8415, 106.8098, T0.AddSeconds(4)));
        // Lệch GPS ~100 m
        Assert.Equal(GpsSampleDecision.Accepted, t.Evaluate(_unit, _assignment, _driver, _driver, 10.8084, 106.7130, T0.AddSeconds(8)));
        // ~1 km sau 1 phút (60 km/h)
        Assert.Equal(GpsSampleDecision.Accepted, t.Evaluate(_unit, _assignment, _driver, _driver, 10.8174, 106.7130, T0.AddSeconds(68)));
    }

    [Fact]
    public void Tracker_NewAssignment_StartsFresh()
    {
        var t = new UnitGpsSourceTracker();
        t.Evaluate(_unit, _assignment, _driver, _driver, 10.80, 106.70, T0);

        Assert.Equal(GpsSampleDecision.Accepted, t.Evaluate(_unit, Guid.NewGuid(), _mate, null, 10.90, 106.90, T0.AddSeconds(4)));
    }

    private readonly Mock<IRescueUnitRepository> _units = new();
    private readonly Mock<IDispatchAssignmentRepository> _assign = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IRealtimeNotifier> _rt = new();

    private (RescueUnitService Svc, RescueUnit Unit) Service()
    {
        var teamId = Guid.NewGuid();
        var unit = new RescueUnit { PlateNumber = "36A-111333", StationId = Guid.NewGuid(), StationTeamId = teamId, CurrentLat = 10.8075, CurrentLng = 106.7130 };
        var active = new DispatchAssignment { RescueUnitId = unit.Id, IncidentId = Guid.NewGuid(), StationTeamId = teamId, Status = DispatchAssignmentStatus.EnRoute };
        _units.Setup(x => x.GetByIdAsync(unit.Id)).ReturnsAsync(unit);
        _assign.Setup(x => x.GetActiveAssignmentByUnitIdAsync(unit.Id)).ReturnsAsync(active);
        _assign.Setup(x => x.GetAcceptedByUserIdAsync(active.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_driver);
        _users.Setup(x => x.GetTeamIdAsync(_driver, It.IsAny<CancellationToken>())).ReturnsAsync(teamId);
        _users.Setup(x => x.GetTeamIdAsync(_mate, It.IsAny<CancellationToken>())).ReturnsAsync(teamId);
        var svc = new RescueUnitService(_units.Object, new Mock<IStationRepository>().Object, _assign.Object,
            new Mock<ISystemAuditService>().Object, new Mock<ICurrentUserContext>().Object, _rt.Object,
            userRepository: _users.Object, gpsSources: new UnitGpsSourceTracker());
        return (svc, unit);
    }

    private static UpdateRescueUnitLocationDto At(double lat, double lng) => new() { Latitude = lat, Longitude = lng };

    [Fact]
    public async Task Service_StaffOutsideCrewTeam_IsRejected()
    {
        var (svc, unit) = Service();
        var outsider = Guid.NewGuid();
        _users.Setup(x => x.GetTeamIdAsync(outsider, It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.UpdateLocationAsync(unit.Id, At(10.81, 106.71), new GpsSender(outsider, UserRole.RescueStaff)));
        _units.Verify(x => x.SaveLocationAsync(It.IsAny<Guid>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Service_SecondCrewMember_IsIgnoredWhileAccepterSends()
    {
        var (svc, unit) = Service();

        await svc.UpdateLocationAsync(unit.Id, At(10.8080, 106.7130), new GpsSender(_driver, UserRole.RescueStaff));
        await svc.UpdateLocationAsync(unit.Id, At(10.8085, 106.7135), new GpsSender(_mate, UserRole.RescueStaff));

        _units.Verify(x => x.SaveLocationAsync(unit.Id, 10.8080, 106.7130, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _units.Verify(x => x.SaveLocationAsync(unit.Id, 10.8085, It.IsAny<double>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(10.8080, unit.CurrentLat);
    }

    [Fact]
    public async Task Service_OperatorSimulator_BypassesCrewRules()
    {
        var (svc, unit) = Service();
        await svc.UpdateLocationAsync(unit.Id, At(10.8080, 106.7130), new GpsSender(_driver, UserRole.RescueStaff));

        // Giả lập GPS (bản dev) chạy nhanh, không thuộc tổ: vẫn ghi
        await svc.UpdateLocationAsync(unit.Id, At(10.90, 106.80), new GpsSender(Guid.NewGuid(), UserRole.Operator));

        _units.Verify(x => x.SaveLocationAsync(unit.Id, 10.90, 106.80, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
