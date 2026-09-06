using System.Security.Cryptography;
using BCrypt.Net;
using EmergencyDispatch.Application.DTOs.Auth;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;

namespace EmergencyDispatch.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ITokenService _tokenService;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;

    public AuthService(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        ITokenService tokenService,
        IEmailService emailService,
        IConfiguration configuration)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _tokenService = tokenService;
        _emailService = emailService;
        _configuration = configuration;
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterRequestDto dto)
    {
        if (await _userRepository.EmailExistsAsync(dto.Email.Trim()))
        {
            throw new InvalidOperationException("Email này đã được sử dụng trong hệ thống.");
        }

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
        var otp = RandomNumberGenerator.GetInt32(100000, 999999).ToString();

        var user = new User
        {
            FullName = dto.FullName.Trim(),
            Email = dto.Email.Trim().ToLowerInvariant(),
            PhoneNumber = dto.PhoneNumber?.Trim(),
            PasswordHash = passwordHash,
            Role = UserRole.Citizen,
            Status = UserStatus.Active,
            IsEmailVerified = false,
            EmailVerificationToken = otp,
            EmailVerificationTokenExpiry = DateTime.UtcNow.AddMinutes(15),
            CreatedAt = DateTime.UtcNow
        };

        await _userRepository.AddAsync(user);

        // Gửi email xác thực tài khoản kèm mã OTP
        await _emailService.SendVerificationEmailAsync(user.Email, user.FullName, otp);

        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken(user.Id);
        await _refreshTokenRepository.AddAsync(refreshToken);

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresAt = refreshToken.ExpiresAt,
            User = MapToUserDto(user)
        };
    }

    public async Task<ApiResponseDto<bool>> VerifyEmailAsync(VerifyEmailDto dto)
    {
        var email = dto.Email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByEmailAsync(email);

        if (user == null)
        {
            return ApiResponseDto<bool>.FailureResult("Không tìm thấy tài khoản tương ứng với email.");
        }

        if (user.IsEmailVerified)
        {
            return ApiResponseDto<bool>.SuccessResult(true, "Tài khoản đã được xác thực trước đó.");
        }

        if (user.EmailVerificationToken != dto.Token.Trim() || user.EmailVerificationTokenExpiry < DateTime.UtcNow)
        {
            return ApiResponseDto<bool>.FailureResult("Mã xác thực không chính xác hoặc đã hết hạn.");
        }

        user.IsEmailVerified = true;
        user.EmailVerificationToken = null;
        user.EmailVerificationTokenExpiry = null;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user);

        // Gửi email chào mừng Welcome
        await _emailService.SendWelcomeEmailAsync(user.Email, user.FullName);

        return ApiResponseDto<bool>.SuccessResult(true, "Xác thực email thành công. Chào mừng bạn gia nhập hệ thống!");
    }

    public async Task<ApiResponseDto<bool>> ResendVerificationEmailAsync(ResendVerificationDto dto)
    {
        var email = dto.Email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByEmailAsync(email);

        if (user == null)
        {
            return ApiResponseDto<bool>.SuccessResult(true, "Nếu email tồn tại trong hệ thống, mã xác thực mới đã được gửi.");
        }

        if (user.IsEmailVerified)
        {
            return ApiResponseDto<bool>.FailureResult("Tài khoản này đã được xác thực email trước đó.");
        }

        var otp = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        user.EmailVerificationToken = otp;
        user.EmailVerificationTokenExpiry = DateTime.UtcNow.AddMinutes(15);
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user);
        await _emailService.SendVerificationEmailAsync(user.Email, user.FullName, otp);

        return ApiResponseDto<bool>.SuccessResult(true, "Mã xác thực mới đã được gửi tới email của bạn.");
    }

    public async Task<ApiResponseDto<bool>> ForgotPasswordAsync(ForgotPasswordDto dto)
    {
        var email = dto.Email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByEmailAsync(email);

        if (user != null)
        {
            var otp = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
            user.PasswordResetToken = otp;
            user.PasswordResetTokenExpiry = DateTime.UtcNow.AddMinutes(10);
            user.UpdatedAt = DateTime.UtcNow;

            await _userRepository.UpdateAsync(user);
            await _emailService.SendPasswordResetEmailAsync(user.Email, user.FullName, otp);
        }

        return ApiResponseDto<bool>.SuccessResult(true, "Nếu email hợp lệ, hướng dẫn đặt lại mật khẩu đã được gửi tới hộp thư của bạn.");
    }

    public async Task<ApiResponseDto<bool>> ResetPasswordAsync(ResetPasswordDto dto)
    {
        var email = dto.Email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByEmailAsync(email);

        if (user == null || user.PasswordResetToken != dto.Token.Trim() || user.PasswordResetTokenExpiry < DateTime.UtcNow)
        {
            return ApiResponseDto<bool>.FailureResult("Mã xác thực không hợp lệ hoặc đã hết hạn.");
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        user.PasswordResetToken = null;
        user.PasswordResetTokenExpiry = null;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user);

        // Thu hồi toàn bộ refresh token cũ để bảo mật
        await _refreshTokenRepository.RevokeUserTokensAsync(user.Id);

        // Gửi cảnh báo đổi mật khẩu
        await _emailService.SendPasswordChangedAlertAsync(user.Email, user.FullName, DateTime.UtcNow);

        return ApiResponseDto<bool>.SuccessResult(true, "Đặt lại mật khẩu thành công. Vui lòng đăng nhập lại với mật khẩu mới.");
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto dto)
    {
        var email = dto.Email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByEmailAsync(email);

        if (user == null || string.IsNullOrEmpty(user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Email hoặc mật khẩu không chính xác.");
        }

        if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Email hoặc mật khẩu không chính xác.");
        }

        if (user.Status == UserStatus.Suspended)
        {
            throw new UnauthorizedAccessException("Tài khoản của bạn đã bị khóa. Vui lòng liên hệ quản trị viên.");
        }

        if (user.Status == UserStatus.Inactive)
        {
            throw new UnauthorizedAccessException("Tài khoản chưa được kích hoạt.");
        }

        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken(user.Id);
        await _refreshTokenRepository.AddAsync(refreshToken);

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresAt = refreshToken.ExpiresAt,
            User = MapToUserDto(user)
        };
    }

    public async Task<AuthResponseDto> GoogleLoginAsync(GoogleLoginDto dto)
    {
        GoogleJsonWebSignature.Payload payload;
        try
        {
            var googleClientId = _configuration["Authentication:Google:ClientId"];
            var settings = new GoogleJsonWebSignature.ValidationSettings();
            if (!string.IsNullOrEmpty(googleClientId))
            {
                settings.Audience = new[] { googleClientId };
            }

            payload = await GoogleJsonWebSignature.ValidateAsync(dto.IdToken, settings);
        }
        catch (Exception ex)
        {
            throw new UnauthorizedAccessException($"Xác thực Google thất bại: {ex.Message}");
        }

        var user = await _userRepository.GetByGoogleIdAsync(payload.Subject)
                   ?? await _userRepository.GetByEmailAsync(payload.Email.ToLowerInvariant());

        if (user == null)
        {
            user = new User
            {
                FullName = payload.Name ?? "Google User",
                Email = payload.Email.ToLowerInvariant(),
                GoogleId = payload.Subject,
                AvatarUrl = payload.Picture,
                Role = UserRole.Citizen,
                Status = UserStatus.Active,
                IsEmailVerified = true, // Google đã xác thực email
                CreatedAt = DateTime.UtcNow
            };
            await _userRepository.AddAsync(user);
        }
        else
        {
            if (string.IsNullOrEmpty(user.GoogleId))
            {
                user.GoogleId = payload.Subject;
            }
            if (string.IsNullOrEmpty(user.AvatarUrl) && !string.IsNullOrEmpty(payload.Picture))
            {
                user.AvatarUrl = payload.Picture;
            }
            user.IsEmailVerified = true;
            user.UpdatedAt = DateTime.UtcNow;
            await _userRepository.UpdateAsync(user);
        }

        if (user.Status == UserStatus.Suspended)
        {
            throw new UnauthorizedAccessException("Tài khoản của bạn đã bị khóa. Vui lòng liên hệ quản trị viên.");
        }

        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken(user.Id);
        await _refreshTokenRepository.AddAsync(refreshToken);

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresAt = refreshToken.ExpiresAt,
            User = MapToUserDto(user)
        };
    }

    public async Task<AuthResponseDto> RefreshTokenAsync(RefreshTokenRequestDto dto)
    {
        var existingToken = await _refreshTokenRepository.GetByTokenAsync(dto.RefreshToken);

        if (existingToken == null || !existingToken.IsActive)
        {
            throw new UnauthorizedAccessException("Refresh token không hợp lệ hoặc đã hết hạn.");
        }

        var user = await _userRepository.GetByIdAsync(existingToken.UserId);
        if (user == null || user.Status != UserStatus.Active)
        {
            throw new UnauthorizedAccessException("Người dùng không còn hoạt động.");
        }

        existingToken.IsRevoked = true;
        existingToken.RevokedAt = DateTime.UtcNow;
        existingToken.UpdatedAt = DateTime.UtcNow;
        await _refreshTokenRepository.UpdateAsync(existingToken);

        var newAccessToken = _tokenService.GenerateAccessToken(user);
        var newRefreshToken = _tokenService.GenerateRefreshToken(user.Id);
        await _refreshTokenRepository.AddAsync(newRefreshToken);

        return new AuthResponseDto
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken.Token,
            ExpiresAt = newRefreshToken.ExpiresAt,
            User = MapToUserDto(user)
        };
    }

    public async Task<bool> RevokeTokenAsync(string refreshToken, Guid userId)
    {
        var token = await _refreshTokenRepository.GetByTokenAsync(refreshToken);
        if (token == null || token.UserId != userId || !token.IsActive)
        {
            return false;
        }

        token.IsRevoked = true;
        token.RevokedAt = DateTime.UtcNow;
        token.UpdatedAt = DateTime.UtcNow;
        await _refreshTokenRepository.UpdateAsync(token);
        return true;
    }

    public async Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordDto dto, string? ipAddress = null)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            throw new KeyNotFoundException("Không tìm thấy người dùng.");
        }

        if (string.IsNullOrEmpty(user.PasswordHash) || !BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Mật khẩu hiện tại không chính xác.");
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);

        // Thu hồi toàn bộ refresh token cũ để bảo mật
        await _refreshTokenRepository.RevokeUserTokensAsync(userId);

        // Bắt buộc gửi email cảnh báo bảo mật thời gian thực
        await _emailService.SendPasswordChangedAlertAsync(user.Email, user.FullName, DateTime.UtcNow, ipAddress);

        return true;
    }

    private static UserDto MapToUserDto(User user) => new()
    {
        Id = user.Id,
        FullName = user.FullName,
        Email = user.Email,
        PhoneNumber = user.PhoneNumber,
        AvatarUrl = user.AvatarUrl,
        Role = user.Role,
        Status = user.Status,
        StationId = user.StationId,
        StationName = user.Station?.Name,
        IsEmailVerified = user.IsEmailVerified,
        BloodType = user.BloodType
    };
}
