using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.DTOs.Team;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Moq;

namespace EmergencyDispatch.Tests.Application;

public class StationTeamServiceTests
{
    private readonly Mock<IStationTeamRepository> _teams = new();
    private readonly Mock<IStationRepository> _stations = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IRescueUnitRepository> _units = new();
    private readonly Mock<ISystemAuditService> _audit = new();
    private readonly Mock<ICurrentUserContext> _me = new();
    private readonly Mock<IRealtimeNotifier> _rt = new();
    private readonly List<StationTeam> _existing = new();
    private readonly Station _station = new() { Name = "Q7" };
    private readonly StationTeamService _svc;

    public StationTeamServiceTests()
    {
        _stations.Setup(s => s.GetByIdAsync(_station.Id)).ReturnsAsync(_station);
        _teams.Setup(t => t.GetByStationAsync(_station.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => _existing);
        _teams.Setup(t => t.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid id, CancellationToken _) => _existing.FirstOrDefault(t => t.Id == id));
        _me.SetupGet(m => m.Role).Returns(UserRole.Admin);
        _svc = new StationTeamService(_teams.Object, _stations.Object, _users.Object, _units.Object, _audit.Object, _me.Object, _rt.Object);
    }

    private User Staff(Guid? station = null, UserRole role = UserRole.RescueStaff, UserStatus status = UserStatus.Active)
    {
        var u = new User { FullName = "NV " + Guid.NewGuid().ToString()[..4], Role = role, StationId = station ?? _station.Id, Status = status };
        _users.Setup(x => x.GetByIdAsync(u.Id)).ReturnsAsync(u);
        return u;
    }

    private RescueUnit Vehicle(Guid? station = null)
    {
        var v = new RescueUnit { PlateNumber = "51A", StationId = station ?? _station.Id };
        _units.Setup(x => x.GetByIdAsync(v.Id)).ReturnsAsync(v);
        return v;
    }

    private static StationTeamInputDto Input(string name, Guid? unit, params (Guid Id, CrewRole Role)[] members) => new()
    {
        Name = name,
        RescueUnitId = unit,
        Members = members.Select(m => new StationTeamMemberInputDto { StaffId = m.Id, CrewRole = m.Role }).ToList()
    };

    [Fact]
    public async Task Create_Valid_SavesAndBroadcasts()
    {
        var a = Staff();
        var b = Staff();
        var v = Vehicle();

        var r = await _svc.CreateAsync(_station.Id, Input(" Tổ 1 ", v.Id, (a.Id, CrewRole.Commander), (b.Id, CrewRole.Driver)));

        Assert.True(r.Success);
        _teams.Verify(t => t.AddAsync(It.Is<StationTeam>(x => x.Name == "Tổ 1" && x.Members.Count == 2 && x.RescueUnits.Single().Id == v.Id), It.IsAny<CancellationToken>()), Times.Once);
        _rt.Verify(n => n.TeamChangedAsync(It.Is<TeamChangedEventDto>(e => e.Action == "Created"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_SingleMemberTeam_IsMarkedUnderstaffed()
    {
        var a = Staff();

        var r = await _svc.CreateAsync(_station.Id, Input("Tổ 1", null, (a.Id, CrewRole.Rescuer)));

        Assert.True(r.Success);
        Assert.True(r.Data!.IsUnderstaffed);
    }

    [Fact]
    public async Task Create_UnknownStation_IsNotFound()
    {
        Assert.Equal(ApiErrorCodes.NotFound, (await _svc.CreateAsync(Guid.NewGuid(), Input("x", null))).ErrorCode);
    }

    [Fact]
    public async Task Create_OperatorOfOtherStation_IsForbidden()
    {
        _me.SetupGet(m => m.Role).Returns(UserRole.Operator);
        _me.SetupGet(m => m.StationId).Returns(Guid.NewGuid());

        Assert.Equal(ApiErrorCodes.Forbidden, (await _svc.CreateAsync(_station.Id, Input("x", null))).ErrorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_EmptyName_Fails(string name)
    {
        Assert.False((await _svc.CreateAsync(_station.Id, Input(name, null))).Success);
    }

    [Fact]
    public async Task Create_DuplicateNameCaseInsensitive_IsConflict()
    {
        _existing.Add(new StationTeam { StationId = _station.Id, Name = "Tổ 1" });

        Assert.Equal(ApiErrorCodes.Conflict, (await _svc.CreateAsync(_station.Id, Input("tổ 1", null))).ErrorCode);
    }

    [Fact]
    public async Task Create_DuplicateMember_Fails()
    {
        var a = Staff();

        Assert.False((await _svc.CreateAsync(_station.Id, Input("x", null, (a.Id, CrewRole.Rescuer), (a.Id, CrewRole.Driver)))).Success);
    }

    [Fact]
    public async Task Create_MoreThanSixMembers_Fails()
    {
        var members = Enumerable.Range(0, 7).Select(_ => (Staff().Id, CrewRole.Rescuer)).ToArray();

        Assert.False((await _svc.CreateAsync(_station.Id, Input("x", null, members))).Success);
    }

    [Fact]
    public async Task Create_TwoCommanders_Fails()
    {
        Assert.False((await _svc.CreateAsync(_station.Id, Input("x", null, (Staff().Id, CrewRole.Commander), (Staff().Id, CrewRole.Commander)))).Success);
    }

    [Fact]
    public async Task Create_MemberFromOtherStationOrNotRescueStaffOrLocked_Fails()
    {
        Assert.False((await _svc.CreateAsync(_station.Id, Input("a", null, (Staff(Guid.NewGuid()).Id, CrewRole.Rescuer)))).Success);
        Assert.False((await _svc.CreateAsync(_station.Id, Input("b", null, (Staff(role: UserRole.Operator).Id, CrewRole.Rescuer)))).Success);
        Assert.False((await _svc.CreateAsync(_station.Id, Input("c", null, (Staff(status: UserStatus.Suspended).Id, CrewRole.Rescuer)))).Success);
    }

    [Fact]
    public async Task Create_MemberAlreadyInAnotherTeam_IsConflict()
    {
        var a = Staff();
        var other = new StationTeam { StationId = _station.Id, Name = "Tổ A" };
        other.Members.Add(new StationTeamMember { StaffId = a.Id });
        _existing.Add(other);

        Assert.Equal(ApiErrorCodes.Conflict, (await _svc.CreateAsync(_station.Id, Input("Tổ B", null, (a.Id, CrewRole.Rescuer)))).ErrorCode);
    }

    [Fact]
    public async Task Create_TeamWithSeveralVehicles_LinksEveryVehicle()
    {
        var a = Staff();
        var b = Staff();
        var fireTruck = Vehicle();
        var ladder = Vehicle();
        var dto = Input("Tổ 1", null, (a.Id, CrewRole.Commander), (b.Id, CrewRole.Driver));
        dto.RescueUnitIds = new List<Guid> { fireTruck.Id, ladder.Id };

        var r = await _svc.CreateAsync(_station.Id, dto);

        Assert.True(r.Success);
        Assert.Equal(2, r.Data!.RescueUnitIds.Count);
        Assert.Contains(fireTruck.Id, r.Data.RescueUnitIds);
        Assert.Contains(ladder.Id, r.Data.RescueUnitIds);
        Assert.NotNull(fireTruck.StationTeamId);
        Assert.Equal(fireTruck.StationTeamId, ladder.StationTeamId);
    }

    [Fact]
    public async Task Update_VehicleList_DetachesRemovedAndAttachesNew()
    {
        var keep = Vehicle();
        var drop = Vehicle();
        var add = Vehicle();
        var team = new StationTeam { StationId = _station.Id, Name = "Tổ 1" };
        foreach (var v in new[] { keep, drop })
        {
            v.StationTeamId = team.Id;
            team.RescueUnits.Add(v);
        }
        _existing.Add(team);
        var dto = Input("Tổ 1", null);
        dto.RescueUnitIds = new List<Guid> { keep.Id, add.Id };

        var r = await _svc.UpdateAsync(_station.Id, team.Id, dto);

        Assert.True(r.Success);
        Assert.Equal(team.Id, keep.StationTeamId);
        Assert.Equal(team.Id, add.StationTeamId);
        Assert.Null(drop.StationTeamId);
        Assert.DoesNotContain(drop, team.RescueUnits);
    }

    [Fact]
    public async Task Update_LegacySingleRescueUnitId_StillAccepted()
    {
        var v = Vehicle();
        var team = new StationTeam { StationId = _station.Id, Name = "Tổ 1" };
        _existing.Add(team);

        var r = await _svc.UpdateAsync(_station.Id, team.Id, Input("Tổ 1", v.Id));

        Assert.True(r.Success);
        Assert.Equal(v.Id, r.Data!.RescueUnitId);
        Assert.Equal(new[] { v.Id }, r.Data.RescueUnitIds);
    }

    [Fact]
    public async Task Create_VehicleOfOtherStationOrAlreadyTaken_Fails()
    {
        var foreign = Vehicle(Guid.NewGuid());
        var taken = Vehicle();
        var owner = new StationTeam { StationId = _station.Id, Name = "Tổ A" };
        owner.RescueUnits.Add(taken);
        taken.StationTeamId = owner.Id;
        _existing.Add(owner);

        Assert.False((await _svc.CreateAsync(_station.Id, Input("x", foreign.Id))).Success);
        Assert.Equal(ApiErrorCodes.Conflict, (await _svc.CreateAsync(_station.Id, Input("y", taken.Id))).ErrorCode);
    }

    [Fact]
    public async Task Update_SyncsMembers_RemovesAddsAndChangesRoles()
    {
        var keep = Staff();
        var drop = Staff();
        var add = Staff();
        var team = new StationTeam { StationId = _station.Id, Name = "Tổ 1" };
        var keepMember = new StationTeamMember { StaffId = keep.Id, CrewRole = CrewRole.Rescuer };
        var dropMember = new StationTeamMember { StaffId = drop.Id, CrewRole = CrewRole.Rescuer };
        team.Members.Add(keepMember);
        team.Members.Add(dropMember);
        _existing.Add(team);

        var r = await _svc.UpdateAsync(_station.Id, team.Id, Input("Tổ 1", null, (keep.Id, CrewRole.Commander), (add.Id, CrewRole.Driver)));

        Assert.True(r.Success);
        Assert.Equal(CrewRole.Commander, keepMember.CrewRole);
        Assert.DoesNotContain(dropMember, team.Members);
        Assert.Contains(team.Members, m => m.StaffId == add.Id && m.CrewRole == CrewRole.Driver);
        _teams.Verify(t => t.RemoveMember(dropMember), Times.Once);
    }

    [Fact]
    public async Task Update_TeamOfOtherStation_IsNotFound()
    {
        var team = new StationTeam { StationId = Guid.NewGuid(), Name = "x" };
        _existing.Add(team);

        Assert.Equal(ApiErrorCodes.NotFound, (await _svc.UpdateAsync(_station.Id, team.Id, Input("x", null))).ErrorCode);
    }

    [Fact]
    public async Task Delete_RemovesAndBroadcasts()
    {
        var team = new StationTeam { StationId = _station.Id, Name = "x" };
        _existing.Add(team);

        Assert.True((await _svc.DeleteAsync(_station.Id, team.Id)).Success);
        _teams.Verify(t => t.Remove(team), Times.Once);
        _rt.Verify(n => n.TeamChangedAsync(It.Is<TeamChangedEventDto>(e => e.Action == "Deleted"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AssignByName_CreatesTeamWhenMissing()
    {
        var staffId = Guid.NewGuid();

        await _svc.AssignByNameAsync(staffId, _station.Id, " Tổ Mới ");

        _teams.Verify(t => t.AddAsync(It.Is<StationTeam>(x => x.Name == "Tổ Mới" && x.Members.Any(m => m.StaffId == staffId)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AssignByName_FullTeam_Throws()
    {
        var team = new StationTeam { StationId = _station.Id, Name = "Tổ 1" };
        for (var i = 0; i < StationTeamService.MaxTeamSize; i++) team.Members.Add(new StationTeamMember { StaffId = Guid.NewGuid() });
        _existing.Add(team);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.AssignByNameAsync(Guid.NewGuid(), _station.Id, "tổ 1"));
    }

    [Fact]
    public async Task AssignByName_MovingStation_RemovesOldMembership()
    {
        var staffId = Guid.NewGuid();
        var old = new StationTeamMember { StaffId = staffId, Team = new StationTeam { StationId = Guid.NewGuid(), Name = "Cũ" } };
        _teams.Setup(t => t.GetMembershipAsync(staffId, It.IsAny<CancellationToken>())).ReturnsAsync(old);

        await _svc.AssignByNameAsync(staffId, null, null);

        _teams.Verify(t => t.RemoveMember(old), Times.Once);
        _teams.Verify(t => t.AddAsync(It.IsAny<StationTeam>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ===================== Tổ đang làm nhiệm vụ =====================
    private (StationTeamService Svc, StationTeam Team, RescueUnit Unit, User A, User B) BusyTeam(DispatchAssignmentStatus? active)
    {
        var a = Staff();
        var b = Staff();
        var unit = Vehicle();
        var team = new StationTeam { StationId = _station.Id, Name = "Tổ 1" };
        team.RescueUnits.Add(unit);
        unit.StationTeamId = team.Id;
        team.Members.Add(new StationTeamMember { TeamId = team.Id, StaffId = a.Id, CrewRole = CrewRole.Commander });
        team.Members.Add(new StationTeamMember { TeamId = team.Id, StaffId = b.Id });
        _existing.Add(team);
        var assigns = new Mock<IDispatchAssignmentRepository>();
        var running = active is { } st
            ? new List<DispatchAssignment> { new() { RescueUnitId = unit.Id, StationTeamId = team.Id, Status = st } }
            : new List<DispatchAssignment>();
        assigns.Setup(x => x.GetAllActiveAsync()).ReturnsAsync(running);
        assigns.Setup(x => x.GetActiveAssignmentByUnitIdAsync(unit.Id)).ReturnsAsync(running.FirstOrDefault());
        var svc = new StationTeamService(_teams.Object, _stations.Object, _users.Object, _units.Object, _audit.Object, _me.Object, _rt.Object, assigns.Object);
        return (svc, team, unit, a, b);
    }

    [Fact]
    public async Task Update_BusyTeam_CannotRemoveMemberOrVehicle_CanRenameAndChangeRole()
    {
        var (svc, team, unit, a, b) = BusyTeam(DispatchAssignmentStatus.EnRoute);
        var c = Staff();

        var dropMember = await svc.UpdateAsync(_station.Id, team.Id, Input("Tổ 1", unit.Id, (a.Id, CrewRole.Commander), (c.Id, CrewRole.Rescuer)));
        var dropVehicle = await svc.UpdateAsync(_station.Id, team.Id, Input("Tổ 1", null, (a.Id, CrewRole.Commander), (b.Id, CrewRole.Rescuer)));
        var rename = await svc.UpdateAsync(_station.Id, team.Id, Input("Tổ 1 mới", unit.Id, (a.Id, CrewRole.Rescuer), (b.Id, CrewRole.Commander)));

        Assert.Equal(ApiErrorCodes.Conflict, dropMember.ErrorCode);
        Assert.Equal(ApiErrorCodes.Conflict, dropVehicle.ErrorCode);
        Assert.True(rename.Success, rename.Message);
        Assert.Equal(team.Id, unit.StationTeamId);
    }

    [Fact]
    public async Task Update_CannotTakeBusyVehicleFromAnotherTeam()
    {
        var (_, busyTeam, busyUnit, _, _) = BusyTeam(DispatchAssignmentStatus.OnScene);
        var (svc, other, otherUnit, x, y) = BusyTeam(null);
        _ = busyTeam;
        // svc của tổ rảnh nhưng xe muốn lấy đang có lượt chạy
        var assigns = new Mock<IDispatchAssignmentRepository>();
        assigns.Setup(r => r.GetAllActiveAsync()).ReturnsAsync(new List<DispatchAssignment>());
        assigns.Setup(r => r.GetActiveAssignmentByUnitIdAsync(busyUnit.Id)).ReturnsAsync(new DispatchAssignment { Status = DispatchAssignmentStatus.OnScene });
        svc = new StationTeamService(_teams.Object, _stations.Object, _users.Object, _units.Object, _audit.Object, _me.Object, _rt.Object, assigns.Object);

        var dto = Input("Tổ 1", otherUnit.Id, (x.Id, CrewRole.Commander), (y.Id, CrewRole.Rescuer));
        dto.RescueUnitIds = new List<Guid> { otherUnit.Id, busyUnit.Id };
        var r = await svc.UpdateAsync(_station.Id, other.Id, dto);

        Assert.Equal(ApiErrorCodes.Conflict, r.ErrorCode);
        Assert.NotEqual(other.Id, busyUnit.StationTeamId);
    }

    [Fact]
    public async Task Delete_BusyTeam_IsConflict_IdleTeam_IsRemoved()
    {
        var (busySvc, busy, _, _, _) = BusyTeam(DispatchAssignmentStatus.Dispatched);
        Assert.Equal(ApiErrorCodes.Conflict, (await busySvc.DeleteAsync(_station.Id, busy.Id)).ErrorCode);
        _teams.Verify(t => t.Remove(busy), Times.Never);

        var (idleSvc, idle, _, _, _) = BusyTeam(null);
        Assert.True((await idleSvc.DeleteAsync(_station.Id, idle.Id)).Success);
        _teams.Verify(t => t.Remove(idle), Times.Once);
    }

    [Fact]
    public async Task Update_BusyTeam_CanRemoveIdleVehicle()
    {
        var (svc, team, busyUnit, a, b) = BusyTeam(DispatchAssignmentStatus.EnRoute);
        var idle = Vehicle();
        team.RescueUnits.Add(idle);
        idle.StationTeamId = team.Id;

        var dto = Input("Tổ 1", busyUnit.Id, (a.Id, CrewRole.Commander), (b.Id, CrewRole.Rescuer));
        dto.RescueUnitIds = new List<Guid> { busyUnit.Id };
        var r = await svc.UpdateAsync(_station.Id, team.Id, dto);

        Assert.True(r.Success, r.Message);
        Assert.Null(idle.StationTeamId);
        Assert.Equal(team.Id, busyUnit.StationTeamId);
    }
}

