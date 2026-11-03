using System.Linq.Expressions;
using EmergencyDispatch.Application.DTOs.Station;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Moq;

namespace EmergencyDispatch.Tests.Application;

public class StationServiceTests
{
    private readonly Mock<IStationRepository> _stations = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ISystemAuditService> _audit = new();
    private readonly Mock<IUserSessionInvalidator> _sessions = new();
    private readonly List<Station> _all = new();
    private readonly StationService _svc;

    public StationServiceTests()
    {
        _stations.Setup(s => s.AddAsync(It.IsAny<Station>())).ReturnsAsync((Station s) => { _all.Add(s); return s; });
        _stations.Setup(s => s.GetAllAsync(It.IsAny<Expression<Func<Station, bool>>?>(), It.IsAny<Func<IQueryable<Station>, IQueryable<Station>>?>()))
            .ReturnsAsync((Expression<Func<Station, bool>>? p, Func<IQueryable<Station>, IQueryable<Station>>? _) => p == null ? _all : _all.Where(p.Compile()).ToList());
        _svc = new StationService(_stations.Object, _users.Object, _audit.Object, _sessions.Object);
    }

    private User Operator(UserStatus status = UserStatus.Active, UserRole role = UserRole.Operator, Guid? station = null)
    {
        var u = new User { FullName = "Op", Role = role, Status = status, StationId = station };
        _users.Setup(x => x.GetByIdAsync(u.Id)).ReturnsAsync(u);
        return u;
    }

    private Station Existing(Action<Station>? tweak = null)
    {
        var s = new Station { Name = "Q7", Address = "a" };
        tweak?.Invoke(s);
        _all.Add(s);
        _stations.Setup(x => x.GetByIdWithUnitsAndStaffAsync(s.Id)).ReturnsAsync(s);
        return s;
    }

    private static CreateStationDto NewDto(Guid? head = null) => new() { Name = " Trạm Q7 ", Address = " 1 NVL ", Latitude = 10.73, Longitude = 106.72, HeadOperatorId = head };

    private static UpdateStationDto UpdDto(Guid? head = null) => new() { Name = "Q7", Address = "a", Latitude = 10.7, Longitude = 106.7, HeadOperatorId = head, CoverageRadiusKm = 8 };

    [Fact]
    public async Task Create_WithoutHead_TrimsFields()
    {
        var dto = await _svc.CreateAsync(NewDto());

        Assert.Equal("Trạm Q7", _all.Single().Name);
        Assert.Equal("1 NVL", _all.Single().Address);
        Assert.Equal("Trạm Q7", dto.Name);
        _sessions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_WithHead_AssignsOperatorToStationAndForcesRelogin()
    {
        var op = Operator();

        await _svc.CreateAsync(NewDto(op.Id));

        var station = _all.Single();
        Assert.Equal(op.Id, station.HeadOperatorId);
        Assert.Equal(station.Id, op.StationId);
        _sessions.Verify(s => s.InvalidateAsync(op.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_HeadMustBeActiveOperator()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.CreateAsync(NewDto(Operator(role: UserRole.RescueStaff).Id)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.CreateAsync(NewDto(Operator(UserStatus.Suspended).Id)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.CreateAsync(NewDto(Guid.NewGuid())));
    }

    [Fact]
    public async Task Create_OperatorAlreadyHeadsAnotherStation_Throws()
    {
        var op = Operator();
        Existing(s => s.HeadOperatorId = op.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.CreateAsync(NewDto(op.Id)));
    }

    [Fact]
    public async Task Update_ChangingHead_UnassignsOldHeadAndInvalidatesBothSessions()
    {
        var oldHead = Operator();
        var newHead = Operator();
        var s = Existing(x => x.HeadOperatorId = oldHead.Id);
        oldHead.StationId = s.Id;

        await _svc.UpdateAsync(s.Id, UpdDto(newHead.Id));

        Assert.Null(oldHead.StationId);
        Assert.Equal(s.Id, newHead.StationId);
        Assert.Equal(8, s.CoverageRadiusKm);
        _sessions.Verify(x => x.InvalidateAsync(oldHead.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _sessions.Verify(x => x.InvalidateAsync(newHead.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_SameHead_DoesNotInvalidateSessions()
    {
        var head = Operator();
        var s = Existing(x => x.HeadOperatorId = head.Id);
        head.StationId = s.Id;

        await _svc.UpdateAsync(s.Id, UpdDto(head.Id));

        _sessions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Update_Unknown_Throws()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.UpdateAsync(Guid.NewGuid(), UpdDto()));
    }

    [Fact]
    public async Task Delete_WithUnitOnMission_Throws()
    {
        var s = Existing(x => x.RescueUnits.Add(new RescueUnit { Status = RescueUnitStatus.OnScene }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.DeleteAsync(s.Id));
        _stations.Verify(x => x.DeleteAsync(It.IsAny<Station>()), Times.Never);
    }

    [Fact]
    public async Task Delete_PutsUnitsOutOfService_UnassignsStaff_AndLogsThemOut()
    {
        var unit = new RescueUnit { Status = RescueUnitStatus.Available, PlateNumber = "x" };
        var membership = new StationTeamMember();
        var staff = new User { Role = UserRole.RescueStaff, TeamMembership = membership };
        var head = Guid.NewGuid();
        var s = Existing(x => { x.RescueUnits.Add(unit); x.StaffMembers.Add(staff); x.HeadOperatorId = head; });
        staff.StationId = s.Id;

        await _svc.DeleteAsync(s.Id);

        Assert.Equal(RescueUnitStatus.OutOfService, unit.Status);
        Assert.Null(staff.StationId);
        Assert.True(membership.IsDeleted);
        Assert.Null(s.HeadOperatorId);
        _stations.Verify(x => x.DeleteAsync(s), Times.Once);
        _sessions.Verify(x => x.InvalidateAsync(staff.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _sessions.Verify(x => x.InvalidateAsync(head, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
