using EmergencyDispatch.Application.DTOs.Auth;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Moq;

namespace EmergencyDispatch.Tests.Application;

public class AuthServiceTests
{
    private const string Pw = "Secret@123";
    private static readonly string PwHash = BCrypt.Net.BCrypt.HashPassword(Pw, 4);

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IRefreshTokenRepository> _tokens = new();
    private readonly Mock<ITokenService> _tokenSvc = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly Mock<IOtpAttemptLimiter> _otp = new();
    private readonly AuthService _svc;

    public AuthServiceTests()
    {
        _tokenSvc.Setup(t => t.GenerateAccessToken(It.IsAny<User>())).Returns("access");
        _tokenSvc.Setup(t => t.GenerateRefreshToken(It.IsAny<Guid>()))
            .Returns((Guid id) => new RefreshToken { UserId = id, Token = "refresh-" + Guid.NewGuid(), ExpiresAt = DateTime.UtcNow.AddDays(7) });
        _svc = new AuthService(_users.Object, _tokens.Object, _tokenSvc.Object, _email.Object, TestHelpers.Config(), _otp.Object);
    }

    private User ExistingUser(Action<User>? tweak = null)
    {
        var u = new User { FullName = "A", Email = "a@test.local", PasswordHash = PwHash, Role = UserRole.Citizen, Status = UserStatus.Active };
        tweak?.Invoke(u);
        _users.Setup(r => r.GetByEmailAsync("a@test.local", It.IsAny<bool>())).ReturnsAsync(u);
        _users.Setup(r => r.GetByIdAsync(u.Id)).ReturnsAsync(u);
        return u;
    }

    // ---------------- Register ----------------
    [Fact]
    public async Task Register_DuplicateEmail_Throws()
    {
        _users.Setup(r => r.EmailExistsAsync("a@test.local", null)).ReturnsAsync(true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.RegisterAsync(new RegisterRequestDto { FullName = "A", Email = " a@test.local ", Password = Pw }));
    }

    [Fact]
    public async Task Register_CreatesUnverifiedCitizen_SendsSixDigitOtp_AndReturnsTokens()
    {
        User? saved = null;
        _users.Setup(r => r.AddAsync(It.IsAny<User>())).Callback<User>(u => saved = u).ReturnsAsync((User u) => u);

        var res = await _svc.RegisterAsync(new RegisterRequestDto { FullName = " An ", Email = "New@Test.Local", Password = Pw, PhoneNumber = " 0901 " });

        Assert.NotNull(saved);
        Assert.Equal("new@test.local", saved!.Email);
        Assert.Equal("An", saved.FullName);
        Assert.Equal(UserRole.Citizen, saved.Role);
        Assert.False(saved.IsEmailVerified);
        Assert.Matches("^[0-9]{6}$", saved.EmailVerificationToken);
        Assert.InRange((saved.EmailVerificationTokenExpiry!.Value - DateTime.UtcNow).TotalMinutes, 14.9, 15.1);
        Assert.True(BCrypt.Net.BCrypt.Verify(Pw, saved.PasswordHash));
        _email.Verify(e => e.SendVerificationEmailAsync("new@test.local", "An", saved.EmailVerificationToken!, It.IsAny<CancellationToken>()), Times.Once);
        _tokens.Verify(t => t.AddAsync(It.IsAny<RefreshToken>()), Times.Once);
        Assert.Equal("access", res.AccessToken);
    }

    // ---------------- Verify email ----------------
    [Fact]
    public async Task VerifyEmail_UnknownEmail_Fails()
    {
        var r = await _svc.VerifyEmailAsync(new VerifyEmailDto { Email = "x@test.local", Token = "123456" });

        Assert.False(r.Success);
    }

    [Fact]
    public async Task VerifyEmail_AlreadyVerified_SucceedsWithoutChanges()
    {
        ExistingUser(u => u.IsEmailVerified = true);

        var r = await _svc.VerifyEmailAsync(new VerifyEmailDto { Email = "a@test.local", Token = "000000" });

        Assert.True(r.Success);
        _users.Verify(x => x.UpdateAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task VerifyEmail_CorrectOtp_ActivatesAndClearsOtp()
    {
        var u = ExistingUser(x => { x.EmailVerificationToken = "123456"; x.EmailVerificationTokenExpiry = DateTime.UtcNow.AddMinutes(5); });

        var r = await _svc.VerifyEmailAsync(new VerifyEmailDto { Email = " A@test.local ", Token = " 123456 " });

        Assert.True(r.Success);
        Assert.True(u.IsEmailVerified);
        Assert.Null(u.EmailVerificationToken);
        _otp.Verify(o => o.Reset("verify-email", "a@test.local"), Times.Once);
        _email.Verify(e => e.SendWelcomeEmailAsync(u.Email, u.FullName, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("654321", 5)]
    [InlineData("123456", -1)]
    public async Task VerifyEmail_WrongOrExpiredOtp_Fails(string token, int expiryMinutes)
    {
        var u = ExistingUser(x => { x.EmailVerificationToken = "123456"; x.EmailVerificationTokenExpiry = DateTime.UtcNow.AddMinutes(expiryMinutes); });

        var r = await _svc.VerifyEmailAsync(new VerifyEmailDto { Email = "a@test.local", Token = token });

        Assert.False(r.Success);
        Assert.False(u.IsEmailVerified);
        Assert.Equal("123456", u.EmailVerificationToken);
    }

    [Fact]
    public async Task VerifyEmail_TooManyWrongOtps_InvalidatesCode()
    {
        var u = ExistingUser(x => { x.EmailVerificationToken = "123456"; x.EmailVerificationTokenExpiry = DateTime.UtcNow.AddMinutes(5); });
        _otp.Setup(o => o.RegisterFailure("verify-email", "a@test.local")).Returns(true);

        var r = await _svc.VerifyEmailAsync(new VerifyEmailDto { Email = "a@test.local", Token = "000000" });

        Assert.False(r.Success);
        Assert.Null(u.EmailVerificationToken);
        _users.Verify(x => x.UpdateAsync(u), Times.Once);
    }

    // ---------------- Resend / forgot / reset ----------------
    [Fact]
    public async Task ResendVerification_UnknownEmail_ReturnsGenericSuccess()
    {
        var r = await _svc.ResendVerificationEmailAsync(new ResendVerificationDto { Email = "nobody@test.local" });

        Assert.True(r.Success);
        _email.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResendVerification_AlreadyVerified_Fails()
    {
        ExistingUser(u => u.IsEmailVerified = true);

        Assert.False((await _svc.ResendVerificationEmailAsync(new ResendVerificationDto { Email = "a@test.local" })).Success);
    }

    [Fact]
    public async Task ResendVerification_IssuesNewOtp()
    {
        var u = ExistingUser(x => x.EmailVerificationToken = "111111");

        var r = await _svc.ResendVerificationEmailAsync(new ResendVerificationDto { Email = "a@test.local" });

        Assert.True(r.Success);
        Assert.Matches("^[0-9]{6}$", u.EmailVerificationToken);
        _email.Verify(e => e.SendVerificationEmailAsync(u.Email, u.FullName, u.EmailVerificationToken!, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmail_DoesNotRevealAndSendsNothing()
    {
        var r = await _svc.ForgotPasswordAsync(new ForgotPasswordDto { Email = "nobody@test.local" });

        Assert.True(r.Success);
        _email.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ForgotPassword_KnownEmail_SetsTenMinuteCode()
    {
        var u = ExistingUser();

        await _svc.ForgotPasswordAsync(new ForgotPasswordDto { Email = "a@test.local" });

        Assert.Matches("^[0-9]{6}$", u.PasswordResetToken);
        Assert.InRange((u.PasswordResetTokenExpiry!.Value - DateTime.UtcNow).TotalMinutes, 9.9, 10.1);
        _email.Verify(e => e.SendPasswordResetEmailAsync(u.Email, u.FullName, u.PasswordResetToken!, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResetPassword_ValidCode_ChangesPasswordRevokesTokensAndAlerts()
    {
        var u = ExistingUser(x => { x.PasswordResetToken = "222222"; x.PasswordResetTokenExpiry = DateTime.UtcNow.AddMinutes(5); });

        var r = await _svc.ResetPasswordAsync(new ResetPasswordDto { Email = "a@test.local", Token = "222222", NewPassword = "New@12345", ConfirmPassword = "New@12345" });

        Assert.True(r.Success);
        Assert.True(BCrypt.Net.BCrypt.Verify("New@12345", u.PasswordHash));
        Assert.Null(u.PasswordResetToken);
        _tokens.Verify(t => t.RevokeUserTokensAsync(u.Id, null), Times.Once);
        _email.Verify(e => e.SendPasswordChangedAlertAsync(u.Email, u.FullName, It.IsAny<DateTime>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("999999", 5)]
    [InlineData("222222", -1)]
    public async Task ResetPassword_WrongOrExpiredCode_Fails(string token, int expiryMinutes)
    {
        var u = ExistingUser(x => { x.PasswordResetToken = "222222"; x.PasswordResetTokenExpiry = DateTime.UtcNow.AddMinutes(expiryMinutes); });

        var r = await _svc.ResetPasswordAsync(new ResetPasswordDto { Email = "a@test.local", Token = token, NewPassword = "New@12345" });

        Assert.False(r.Success);
        Assert.Equal(PwHash, u.PasswordHash);
        _tokens.Verify(t => t.RevokeUserTokensAsync(It.IsAny<Guid>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ResetPassword_TooManyWrongCodes_InvalidatesCode()
    {
        var u = ExistingUser(x => { x.PasswordResetToken = "222222"; x.PasswordResetTokenExpiry = DateTime.UtcNow.AddMinutes(5); });
        _otp.Setup(o => o.RegisterFailure("reset-password", "a@test.local")).Returns(true);

        await _svc.ResetPasswordAsync(new ResetPasswordDto { Email = "a@test.local", Token = "000000", NewPassword = "x" });

        Assert.Null(u.PasswordResetToken);
    }

    // ---------------- Login ----------------
    [Fact]
    public async Task Login_UnknownUser_Throws()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.LoginAsync(new LoginRequestDto { Email = "x@test.local", Password = Pw }));
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsTokensAndResetsFailureCounter()
    {
        var u = ExistingUser(x => x.FailedLoginCount = 3);

        var res = await _svc.LoginAsync(new LoginRequestDto { Email = " A@Test.Local ", Password = Pw });

        Assert.Equal("access", res.AccessToken);
        Assert.Equal(0, u.FailedLoginCount);
        Assert.Equal(u.Id, res.User.Id);
    }

    [Fact]
    public async Task Login_WrongPassword_IncrementsCounter()
    {
        var u = ExistingUser();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.LoginAsync(new LoginRequestDto { Email = "a@test.local", Password = "wrong" }));

        Assert.Equal(1, u.FailedLoginCount);
        Assert.Null(u.LockoutEndUtc);
    }

    [Fact]
    public async Task Login_FifthWrongPassword_LocksFor15Minutes()
    {
        var u = ExistingUser(x => x.FailedLoginCount = 4);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.LoginAsync(new LoginRequestDto { Email = "a@test.local", Password = "wrong" }));

        Assert.NotNull(u.LockoutEndUtc);
        Assert.InRange((u.LockoutEndUtc!.Value - DateTime.UtcNow).TotalMinutes, 14.9, 15.1);
        Assert.Equal(0, u.FailedLoginCount);
    }

    [Fact]
    public async Task Login_WhileLockedOut_RejectsEvenCorrectPassword()
    {
        ExistingUser(x => x.LockoutEndUtc = DateTime.UtcNow.AddMinutes(10));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.LoginAsync(new LoginRequestDto { Email = "a@test.local", Password = Pw }));
        _tokenSvc.Verify(t => t.GenerateAccessToken(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task Login_ExpiredLockout_AllowsLoginAndClearsLock()
    {
        var u = ExistingUser(x => x.LockoutEndUtc = DateTime.UtcNow.AddMinutes(-1));

        await _svc.LoginAsync(new LoginRequestDto { Email = "a@test.local", Password = Pw });

        Assert.Null(u.LockoutEndUtc);
    }

    [Theory]
    [InlineData(UserStatus.Suspended)]
    [InlineData(UserStatus.Inactive)]
    public async Task Login_SuspendedOrInactive_Throws(UserStatus status)
    {
        ExistingUser(x => { x.Status = status; x.LockReason = "spam"; });

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.LoginAsync(new LoginRequestDto { Email = "a@test.local", Password = Pw }));
        if (status == UserStatus.Suspended)
        {
            Assert.Contains("spam", ex.Message);
        }
    }

    [Fact]
    public async Task Login_GoogleOnlyAccountWithoutPassword_Throws()
    {
        ExistingUser(x => x.PasswordHash = null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.LoginAsync(new LoginRequestDto { Email = "a@test.local", Password = Pw }));
    }

    // ---------------- Refresh / revoke / change password ----------------
    [Fact]
    public async Task Refresh_ValidToken_RotatesToken()
    {
        var u = ExistingUser();
        var old = new RefreshToken { UserId = u.Id, Token = "old", ExpiresAt = DateTime.UtcNow.AddDays(1) };
        _tokens.Setup(t => t.GetByTokenAsync("old")).ReturnsAsync(old);

        var res = await _svc.RefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = "old" });

        Assert.True(old.IsRevoked);
        Assert.NotEqual("old", res.RefreshToken);
        _tokens.Verify(t => t.UpdateAsync(old), Times.Once);
        _tokens.Verify(t => t.AddAsync(It.Is<RefreshToken>(n => n.UserId == u.Id)), Times.Once);
    }

    [Fact]
    public async Task Refresh_RevokedOrExpiredOrUnknownToken_Throws()
    {
        _tokens.Setup(t => t.GetByTokenAsync("revoked")).ReturnsAsync(new RefreshToken { IsRevoked = true, ExpiresAt = DateTime.UtcNow.AddDays(1) });
        _tokens.Setup(t => t.GetByTokenAsync("expired")).ReturnsAsync(new RefreshToken { ExpiresAt = DateTime.UtcNow.AddMinutes(-1) });

        foreach (var tok in new[] { "revoked", "expired", "unknown" })
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.RefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = tok }));
        }
    }

    [Fact]
    public async Task Refresh_SuspendedUser_Throws()
    {
        var u = ExistingUser(x => x.Status = UserStatus.Suspended);
        _tokens.Setup(t => t.GetByTokenAsync("t")).ReturnsAsync(new RefreshToken { UserId = u.Id, ExpiresAt = DateTime.UtcNow.AddDays(1) });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.RefreshTokenAsync(new RefreshTokenRequestDto { RefreshToken = "t" }));
    }

    [Fact]
    public async Task RevokeToken_OnlyOwnActiveToken()
    {
        var owner = Guid.NewGuid();
        var tok = new RefreshToken { UserId = owner, Token = "t", ExpiresAt = DateTime.UtcNow.AddDays(1) };
        _tokens.Setup(t => t.GetByTokenAsync("t")).ReturnsAsync(tok);

        Assert.False(await _svc.RevokeTokenAsync("t", Guid.NewGuid()));
        Assert.False(tok.IsRevoked);
        Assert.True(await _svc.RevokeTokenAsync("t", owner));
        Assert.True(tok.IsRevoked);
        Assert.False(await _svc.RevokeTokenAsync("t", owner));
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_Throws()
    {
        var u = ExistingUser();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _svc.ChangePasswordAsync(u.Id, new ChangePasswordDto { CurrentPassword = "bad", NewPassword = "New@12345" }));
        _tokens.Verify(t => t.RevokeUserTokensAsync(It.IsAny<Guid>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ChangePassword_UnknownUser_Throws()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _svc.ChangePasswordAsync(Guid.NewGuid(), new ChangePasswordDto { CurrentPassword = Pw, NewPassword = "New@12345" }));
    }

    [Fact]
    public async Task ChangePassword_Valid_UpdatesHashRevokesTokensAndSendsAlertWithIp()
    {
        var u = ExistingUser();

        Assert.True(await _svc.ChangePasswordAsync(u.Id, new ChangePasswordDto { CurrentPassword = Pw, NewPassword = "New@12345" }, "1.2.3.4"));

        Assert.True(BCrypt.Net.BCrypt.Verify("New@12345", u.PasswordHash));
        _tokens.Verify(t => t.RevokeUserTokensAsync(u.Id, null), Times.Once);
        _email.Verify(e => e.SendPasswordChangedAlertAsync(u.Email, u.FullName, It.IsAny<DateTime>(), "1.2.3.4", It.IsAny<CancellationToken>()), Times.Once);
    }
}
