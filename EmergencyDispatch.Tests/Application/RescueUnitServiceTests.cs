using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.DTOs.RescueUnit;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Moq;

namespace EmergencyDispatch.Tests.Application;

public class RescueUnitServiceTests
{
    private readonly Mock<IRescueUnitRepository> _units = new();
    private readonly Mock<IStationRepository> _stations = new();
    private readonly Mock<IDispatchAssignmentRepository> _assign = new();
    private readonly Mock<ISystemAuditService> _audit = new();
    private readonly Mock<ICurrentUserContext> _me = new();
    private readonly Mock<IRealtimeNotifier> _rt = new();
    private readonly Station _a = new() { Name = "A", Latitude = 10.7, Longitude = 106.7 };
    private readonly Station _b = new() { Name = "B", Latitude = 10.8, Longitude = 106.6 };
    private readonly RescueUnitService _svc;

    public RescueUnitServiceTests()
    {
        _stations.Setup(s => s.GetByIdAsync(_a.Id)).ReturnsAsync(_a);
        _stations.Setup(s => s.GetByIdAsync(_b.Id)).ReturnsAsync(_b);
        _units.Setup(u => u.AddAsync(It.IsAny<RescueUnit>())).ReturnsAsync((RescueUnit u) => u);
        AsAdmin();
        _svc = new RescueUnitService(_units.Object, _stations.Object, _assign.Object, _audit.Object, _me.Object, _rt.Object);
    }

    private void AsAdmin() => _me.SetupGet(m => m.Role).Returns(UserRole.Admin);

    private void AsOperatorOf(Station s)
    {
        _me.SetupGet(m => m.Role).Returns(UserRole.Operator);
        _me.SetupGet(m => m.StationId).Returns(s.Id);
    }

    private RescueUnit Existing(RescueUnitStatus status = RescueUnitStatus.Available, Station? station = null, bool activeAssignment = false)
    {
        var u = new RescueUnit { PlateNumber = "51A-001", UnitType = RescueUnitType.FireTruck, Status = status, StationId = (station ?? _a).Id, Station = station ?? _a };
        _units.Setup(x => x.GetByIdWithDetailsAsync(u.Id)).ReturnsAsync(u);
        _units.Setup(x => x.GetByIdAsync(u.Id)).ReturnsAsync(u);
        if (activeAssignment)
        {
            _assign.Setup(x => x.GetActiveAssignmentByUnitIdAsync(u.Id)).ReturnsAsync(new DispatchAssignment { IncidentId = Guid.NewGuid() });
        }
        return u;
    }

    [Theory]
    [InlineData(RescueUnitStatus.Dispatched, true)]
    [InlineData(RescueUnitStatus.OnScene, true)]
    [InlineData(RescueUnitStatus.Returning, true)]
    [InlineData(RescueUnitStatus.Available, false)]
    [InlineData(RescueUnitStatus.CoolDown, false)]
    [InlineData(RescueUnitStatus.Maintenance, false)]
    public void IsOnMission(RescueUnitStatus s, bool expected)
    {
        Assert.Equal(expected, RescueUnitService.IsOnMission(s));
    }

    [Fact]
    public async Task Create_NormalizesPlate_DefaultsToStationLocation_AndBroadcasts()
    {
        RescueUnit? saved = null;
        _units.Setup(u => u.AddAsync(It.IsAny<RescueUnit>())).Callback<RescueUnit>(u => saved = u).ReturnsAsync((RescueUnit u) => u);

        var dto = await _svc.CreateAsync(new CreateRescueUnitDto { PlateNumber = " 51a-123 ", UnitType = RescueUnitType.Ambulance, StationId = _a.Id });

        Assert.Equal("51A-123", saved!.PlateNumber);
        Assert.Equal(RescueUnitStatus.Available, saved.Status);
        Assert.Equal(_a.Latitude, saved.CurrentLat);
        Assert.Equal(_a.Longitude, saved.CurrentLng);
        Assert.Equal("51A-123", dto.PlateNumber);
        _rt.Verify(n => n.RescueUnitStatusChangedAsync(It.IsAny<RescueUnitStatusEventDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_DuplicatePlate_Throws()
    {
        _units.Setup(u => u.PlateNumberExistsAsync("51A-1", null)).ReturnsAsync(true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.CreateAsync(new CreateRescueUnitDto { PlateNumber = "51A-1", StationId = _a.Id }));
    }

    [Fact]
    public async Task Create_UnknownStation_Throws()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.CreateAsync(new CreateRescueUnitDto { PlateNumber = "x", StationId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Create_OperatorForOtherStation_IsUnauthorized()
    {
        AsOperatorOf(_a);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.CreateAsync(new CreateRescueUnitDto { PlateNumber = "x", StationId = _b.Id }));
    }

    [Fact]
    public async Task Update_MoveStation_ResetsLocationToNewStation()
    {
        var u = Existing();

        await _svc.UpdateAsync(u.Id, new UpdateRescueUnitDto { PlateNumber = "51a-001", UnitType = RescueUnitType.FireTruck, Status = RescueUnitStatus.Available, StationId = _b.Id });

        Assert.Equal(_b.Id, u.StationId);
        Assert.Equal(_b.Latitude, u.CurrentLat);
    }

    [Fact]
    public async Task Update_OperatorCannotMoveUnitToOtherStation()
    {
        AsOperatorOf(_a);
        var u = Existing();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _svc.UpdateAsync(u.Id, new UpdateRescueUnitDto { PlateNumber = "x", Status = RescueUnitStatus.Available, StationId = _b.Id }));
    }

    [Fact]
    public async Task Update_OnMission_CannotChangeStatusOrStation()
    {
        var u = Existing(RescueUnitStatus.OnScene);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.UpdateAsync(u.Id, new UpdateRescueUnitDto { PlateNumber = "51A-001", Status = RescueUnitStatus.Available, StationId = _a.Id }));
    }

    [Theory]
    [InlineData(RescueUnitStatus.Dispatched)]
    [InlineData(RescueUnitStatus.OnScene)]
    [InlineData(RescueUnitStatus.CoolDown)]
    public async Task UpdateStatus_SystemManagedStatuses_CannotBeSetManually(RescueUnitStatus target)
    {
        var u = Existing();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.UpdateStatusAsync(u.Id, new UpdateRescueUnitStatusDto { Status = target }));
        Assert.Equal(RescueUnitStatus.Available, u.Status);
    }

    [Fact]
    public async Task UpdateStatus_ToMaintenance_ClearsCoolDownAndBroadcasts()
    {
        var u = Existing(RescueUnitStatus.CoolDown);
        u.CoolDownUntil = DateTime.UtcNow.AddMinutes(3);

        await _svc.UpdateStatusAsync(u.Id, new UpdateRescueUnitStatusDto { Status = RescueUnitStatus.Maintenance });

        Assert.Equal(RescueUnitStatus.Maintenance, u.Status);
        Assert.Null(u.CoolDownUntil);
        _rt.Verify(n => n.RescueUnitStatusChangedAsync(It.Is<RescueUnitStatusEventDto>(e => e.PreviousStatus == RescueUnitStatus.CoolDown), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateStatus_WithActiveAssignment_Throws()
    {
        var u = Existing(activeAssignment: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.UpdateStatusAsync(u.Id, new UpdateRescueUnitStatusDto { Status = RescueUnitStatus.Maintenance }));
    }

    [Fact]
    public async Task UpdateStatus_SameStatus_DoesNotBroadcast()
    {
        var u = Existing();

        await _svc.UpdateStatusAsync(u.Id, new UpdateRescueUnitStatusDto { Status = RescueUnitStatus.Available });

        _rt.Verify(n => n.RescueUnitStatusChangedAsync(It.IsAny<RescueUnitStatusEventDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateLocation_SavesAndBroadcastsWithActiveIncident()
    {
        var u = Existing(activeAssignment: true);

        await _svc.UpdateLocationAsync(u.Id, new UpdateRescueUnitLocationDto { Latitude = 10.75, Longitude = 106.71, HeadingDegrees = 90, SpeedKmh = 40 });

        Assert.Equal(10.75, u.CurrentLat);
        Assert.NotNull(u.LastLocationUpdateAt);
        _rt.Verify(n => n.RescueUnitLocationAsync(It.Is<RescueUnitLocationEventDto>(e => e.IncidentId != null && e.HeadingDegrees == 90), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_OnMission_Throws_IdleUnit_IsDeleted()
    {
        var busy = Existing(RescueUnitStatus.Dispatched);
        var idle = Existing();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.DeleteAsync(busy.Id));
        await _svc.DeleteAsync(idle.Id);

        _units.Verify(x => x.DeleteAsync(idle), Times.Once);
        _rt.Verify(n => n.RescueUnitStatusChangedAsync(It.Is<RescueUnitStatusEventDto>(e => e.IsDeleted), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetById_Unknown_Throws()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.GetByIdAsync(Guid.NewGuid()));
    }
}
