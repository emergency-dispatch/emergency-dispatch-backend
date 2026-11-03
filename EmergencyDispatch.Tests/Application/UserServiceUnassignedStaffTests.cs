using EmergencyDispatch.Application.DTOs.User;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Moq;

namespace EmergencyDispatch.Tests.Application;

/// <summary>GET /api/users/unassigned-staff and pulling such staff into the operator's own station.</summary>
public class UserServiceUnassignedStaffTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IStationRepository> _stations = new();
    private readonly Mock<IStationTeamService> _teams = new();
    private readonly Mock<IUserSessionInvalidator> _sessions = new();
    private readonly Mock<ICurrentUserContext> _me = new();
    private readonly Station _own = new() { Name = "Q7" };
    private readonly UserService _svc;

    public UserServiceUnassignedStaffTests()
    {
        _stations.Setup(s => s.GetByIdAsync(_own.Id)).ReturnsAsync(_own);
        _me.SetupGet(m => m.Role).Returns(UserRole.Operator);
        _me.SetupGet(m => m.StationId).Returns(_own.Id);
        _svc = new UserService(_users.Object, _stations.Object, Mock.Of<IRescueUnitRepository>(), _teams.Object,
            Mock.Of<ISystemAuditService>(), _sessions.Object, _me.Object, Mock.Of<IRealtimeNotifier>());
    }

    [Fact]
    public async Task GetUnassigned_ReturnsContactInfoOnly_WithoutNationalIdOrMedicalData()
    {
        var staff = new User
        {
            FullName = "NV A", Email = "a@test.local", PhoneNumber = "0901234567", Role = UserRole.RescueStaff, Status = UserStatus.Active,
            CitizenIdNumber = "079000000111", BloodType = BloodType.A_Positive, Allergies = new() { "penicillin" }, MedicalNotes = "hen"
        };
        _users.Setup(u => u.GetUnassignedRescueStaffPagedAsync("a", 1, 20)).ReturnsAsync((new List<User> { staff }, 1));

        var page = await _svc.GetUnassignedStaffAsync(1, 20, "a");

        var dto = Assert.Single(page.Items);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(staff.Id, dto.Id);
        Assert.Equal("0901234567", dto.PhoneNumber);
        Assert.Null(dto.StationId);
        Assert.Null(dto.CitizenIdNumber);
        Assert.Null(dto.BloodType);
        Assert.Null(dto.MedicalNotes);
        Assert.Empty(dto.Allergies);
    }

    [Fact]
    public async Task GetUnassigned_IsNotLimitedToOperatorStation_AndClampsPaging()
    {
        _users.Setup(u => u.GetUnassignedRescueStaffPagedAsync(null, 1, 100)).ReturnsAsync((new List<User>(), 0));

        var page = await _svc.GetUnassignedStaffAsync(0, 500, null);

        Assert.Equal(1, page.PageIndex);
        Assert.Equal(100, page.PageSize);
        _users.Verify(u => u.GetUnassignedRescueStaffPagedAsync(null, 1, 100), Times.Once);
        _users.Verify(u => u.GetUsersPagedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<UserRole?>(), It.IsAny<UserStatus?>(), It.IsAny<Guid?>()), Times.Never);
    }

    [Fact]
    public async Task Operator_CanAssignUnassignedStaffToOwnStation()
    {
        var staff = new User { FullName = "NV B", Role = UserRole.RescueStaff, Status = UserStatus.Active, StationId = null };
        _users.Setup(u => u.GetByIdWithDetailsAsync(staff.Id)).ReturnsAsync(staff);

        var dto = await _svc.AssignStaffToStationAndTeamAsync(staff.Id, new AssignStaffToStationAndTeamDto { StationId = _own.Id }, UserRole.Operator);

        Assert.Equal(_own.Id, staff.StationId);
        Assert.Equal(_own.Id, dto.StationId);
        _sessions.Verify(s => s.InvalidateAsync(staff.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Operator_CannotTakeStaffOfAnotherStation()
    {
        var staff = new User { Role = UserRole.RescueStaff, Status = UserStatus.Active, StationId = Guid.NewGuid() };
        _users.Setup(u => u.GetByIdWithDetailsAsync(staff.Id)).ReturnsAsync(staff);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _svc.AssignStaffToStationAndTeamAsync(staff.Id, new AssignStaffToStationAndTeamDto { StationId = _own.Id }, UserRole.Operator));
    }
}
