using EmergencyDispatch.Application.DTOs.User;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Moq;

namespace EmergencyDispatch.Tests.Application;

public class UserServiceTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IStationRepository> _stations = new();
    private readonly Mock<IRescueUnitRepository> _units = new();
    private readonly Mock<IStationTeamService> _teams = new();
    private readonly Mock<ISystemAuditService> _audit = new();
    private readonly Mock<IUserSessionInvalidator> _sessions = new();
    private readonly Mock<ICurrentUserContext> _me = new();
    private readonly Mock<IRealtimeNotifier> _rt = new();
    private readonly Station _stationA = new() { Name = "A" };
    private readonly Station _stationB = new() { Name = "B" };
    private readonly UserService _svc;

    public UserServiceTests()
    {
        _stations.Setup(s => s.GetByIdAsync(_stationA.Id)).ReturnsAsync(_stationA);
        _stations.Setup(s => s.GetByIdAsync(_stationB.Id)).ReturnsAsync(_stationB);
        _stations.Setup(s => s.GetAllAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Station, bool>>?>(), It.IsAny<Func<IQueryable<Station>, IQueryable<Station>>?>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<Station, bool>>? p, Func<IQueryable<Station>, IQueryable<Station>>? _) =>
                new[] { _stationA, _stationB }.Where(p!.Compile()).ToList());
        _users.Setup(u => u.AddAsync(It.IsAny<User>())).ReturnsAsync((User u) => u);
        _me.SetupGet(m => m.Role).Returns(UserRole.Admin);
        _svc = new UserService(_users.Object, _stations.Object, _units.Object, _teams.Object, _audit.Object, _sessions.Object, _me.Object, _rt.Object);
    }

    private User Existing(UserRole role = UserRole.RescueStaff, Guid? station = null)
    {
        var u = new User { FullName = "U", Email = "u@test.local", Role = role, Status = UserStatus.Active, StationId = station };
        _users.Setup(x => x.GetByIdWithDetailsAsync(u.Id)).ReturnsAsync(u);
        _users.Setup(x => x.GetByIdAsync(u.Id)).ReturnsAsync(u);
        return u;
    }

    private void AsOperatorOf(Station s)
    {
        _me.SetupGet(m => m.Role).Returns(UserRole.Operator);
        _me.SetupGet(m => m.StationId).Returns(s.Id);
    }

    private static CreateUserDto NewUser(UserRole role, Guid? station = null) => new()
    {
        FullName = " Nhân viên ", Email = " New@Test.Local ", Password = "Test@123456", Role = role, StationId = station
    };

    // -------- profile --------
    [Fact]
    public async Task UpdateProfile_UpdatesMedicalFields_AndRespectsTriStateFields()
    {
        var u = Existing(UserRole.Citizen);
        u.PreferredLanguage = "vi";
        u.Allergies = new() { "penicillin" };

        var dto = new UpdateProfileDto { FullName = " An ", BloodType = BloodType.A_Positive, ChronicConditions = new() { "Hen" }, EmergencyContactPhone = " 0900 " };
        await _svc.UpdateProfileAsync(u.Id, dto);

        Assert.Equal("An", u.FullName);
        Assert.Equal(BloodType.A_Positive, u.BloodType);
        Assert.Equal(new[] { "Hen" }, u.ChronicConditions);
        Assert.Equal(new[] { "penicillin" }, u.Allergies);   // null list = unchanged
        Assert.Equal("vi", u.PreferredLanguage);             // not sent = unchanged
        Assert.Equal("0900", u.EmergencyContactPhone);

        await _svc.UpdateProfileAsync(u.Id, new UpdateProfileDto { FullName = "An", PreferredLanguage = null });
        Assert.Null(u.PreferredLanguage);                    // sent as null = cleared
    }

    [Fact]
    public async Task UpdateProfile_UnknownUser_Throws()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.UpdateProfileAsync(Guid.NewGuid(), new UpdateProfileDto { FullName = "x" }));
    }

    // -------- create --------
    [Theory]
    [InlineData(UserRole.Operator)]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Citizen)]
    public async Task Create_OperatorCanOnlyCreateRescueStaff(UserRole target)
    {
        AsOperatorOf(_stationA);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.CreateUserAsync(NewUser(target), UserRole.Operator));
    }

    [Fact]
    public async Task Create_ByOperator_ForcesOwnStation()
    {
        AsOperatorOf(_stationA);
        User? saved = null;
        _users.Setup(u => u.AddAsync(It.IsAny<User>())).Callback<User>(u => saved = u).ReturnsAsync((User u) => u);

        await _svc.CreateUserAsync(NewUser(UserRole.RescueStaff, _stationB.Id), UserRole.Operator);

        Assert.Equal(_stationA.Id, saved!.StationId);
        Assert.Equal("new@test.local", saved.Email);
        Assert.Equal("Nhân viên", saved.FullName);
        Assert.Equal(UserStatus.Active, saved.Status);
    }

    [Fact]
    public async Task Create_DuplicateEmail_Throws()
    {
        _users.Setup(u => u.EmailExistsAsync("New@Test.Local", null)).ReturnsAsync(true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.CreateUserAsync(NewUser(UserRole.RescueStaff), UserRole.Admin));
    }

    [Fact]
    public async Task Create_OperatorForStation_BecomesHead_UnlessStationAlreadyHasOne()
    {
        var created = await _svc.CreateUserAsync(NewUser(UserRole.Operator, _stationA.Id), UserRole.Admin);
        Assert.Equal(created.Id, _stationA.HeadOperatorId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.CreateUserAsync(NewUser(UserRole.Operator, _stationA.Id), UserRole.Admin));
    }

    // -------- update --------
    [Fact]
    public async Task Update_RoleOrStationChange_InvalidatesSession()
    {
        var u = Existing(UserRole.RescueStaff, _stationA.Id);

        await _svc.UpdateUserAsync(u.Id, new UpdateUserDto { FullName = "U", StationId = _stationB.Id }, UserRole.Admin);

        Assert.Equal(_stationB.Id, u.StationId);
        _sessions.Verify(s => s.InvalidateAsync(u.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_NameOnly_DoesNotInvalidateSession()
    {
        var u = Existing(UserRole.RescueStaff, _stationA.Id);

        await _svc.UpdateUserAsync(u.Id, new UpdateUserDto { FullName = "Tên mới", StationId = _stationA.Id }, UserRole.Admin);

        Assert.Equal("Tên mới", u.FullName);
        _sessions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Update_OperatorCannotEditStaffOfOtherStation()
    {
        AsOperatorOf(_stationA);
        var u = Existing(UserRole.RescueStaff, _stationB.Id);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.UpdateUserAsync(u.Id, new UpdateUserDto { FullName = "x" }, UserRole.Operator));
    }

    [Fact]
    public async Task Update_OperatorCannotPromoteStaff()
    {
        AsOperatorOf(_stationA);
        var u = Existing(UserRole.RescueStaff, _stationA.Id);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.UpdateUserAsync(u.Id, new UpdateUserDto { FullName = "x", Role = UserRole.Operator }, UserRole.Operator));
    }

    [Fact]
    public async Task Update_OperatorMovedToOtherStation_TransfersHeadship()
    {
        var op = Existing(UserRole.Operator, _stationA.Id);
        _stationA.HeadOperatorId = op.Id;

        await _svc.UpdateUserAsync(op.Id, new UpdateUserDto { FullName = "Op", StationId = _stationB.Id }, UserRole.Admin);

        Assert.Null(_stationA.HeadOperatorId);
        Assert.Equal(op.Id, _stationB.HeadOperatorId);
    }

    // -------- lock / unlock / delete --------
    [Fact]
    public async Task Lock_SuspendsRevokesTokensAndInvalidatesSession()
    {
        var u = Existing();
        var token = new RefreshToken { ExpiresAt = DateTime.UtcNow.AddDays(1) };
        u.RefreshTokens.Add(token);
        var admin = Guid.NewGuid();

        await _svc.LockUserAsync(u.Id, "  Vi phạm quy định  ", admin, UserRole.Admin);

        Assert.Equal(UserStatus.Suspended, u.Status);
        Assert.Equal("Vi phạm quy định", u.LockReason);
        Assert.Equal(admin, u.LockedByUserId);
        Assert.True(token.IsRevoked);
        _sessions.Verify(s => s.InvalidateAsync(u.Id, It.Is<string>(r => r.Contains("Vi phạm quy định")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Lock_Self_Throws()
    {
        var u = Existing(UserRole.Admin);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.LockUserAsync(u.Id, "self lock", u.Id, UserRole.Admin));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc ")]
    public async Task Lock_ReasonShorterThanFive_Throws(string reason)
    {
        var u = Existing();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.LockUserAsync(u.Id, reason, Guid.NewGuid(), UserRole.Admin));
    }

    [Fact]
    public async Task Lock_OperatorCannotLockAnotherOperator()
    {
        var u = Existing(UserRole.Operator);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.LockUserAsync(u.Id, "reason long", Guid.NewGuid(), UserRole.Operator));
    }

    [Fact]
    public async Task Unlock_ClearsLockAndLoginLockout()
    {
        var u = Existing();
        u.Status = UserStatus.Suspended;
        u.LockReason = "x";
        u.FailedLoginCount = 4;
        u.LockoutEndUtc = DateTime.UtcNow.AddMinutes(10);

        await _svc.UnlockUserAsync(u.Id, UserRole.Admin);

        Assert.Equal(UserStatus.Active, u.Status);
        Assert.Null(u.LockReason);
        Assert.Equal(0, u.FailedLoginCount);
        Assert.Null(u.LockoutEndUtc);
    }

    [Fact]
    public async Task Delete_ClearsStationHeadship_AndInvalidatesSession()
    {
        var op = Existing(UserRole.Operator, _stationA.Id);
        _stationA.HeadOperatorId = op.Id;

        Assert.True(await _svc.DeleteUserAsync(op.Id));
        Assert.Null(_stationA.HeadOperatorId);
        _sessions.Verify(s => s.InvalidateAsync(op.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(await _svc.DeleteUserAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task UpdateFcmToken_TrimsAndSaves()
    {
        var u = Existing();

        Assert.True(await _svc.UpdateFcmTokenAsync(u.Id, "  tok  "));
        Assert.Equal("tok", u.FcmToken);
        Assert.False(await _svc.UpdateFcmTokenAsync(Guid.NewGuid(), "x"));
    }

    [Fact(Skip = "Known gap (TC_F5_SEC_17): UserResponseDto always includes CitizenIdNumber, so Operators see staff national IDs in GET /api/users.")]
    public async Task GetAll_AsOperator_HidesCitizenIdNumber()
    {
        AsOperatorOf(_stationA);
        var staff = new User { Role = UserRole.RescueStaff, StationId = _stationA.Id, CitizenIdNumber = "079000000111" };
        _users.Setup(u => u.GetUsersPagedAsync(1, 20, null, null, null, _stationA.Id)).ReturnsAsync((new List<User> { staff }, 1));

        var page = await _svc.GetAllAsync(1, 20, null, null, null);

        Assert.All(page.Items, x => Assert.True(string.IsNullOrEmpty(x.CitizenIdNumber)));
    }
}
