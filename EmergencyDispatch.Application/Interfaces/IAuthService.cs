using EmergencyDispatch.Application.DTOs.Auth;
using EmergencyDispatch.Application.DTOs.Common;

namespace EmergencyDispatch.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponseDto> RegisterAsync(RegisterRequestDto dto);
    Task<ApiResponseDto<bool>> VerifyEmailAsync(VerifyEmailDto dto);
    Task<ApiResponseDto<bool>> ResendVerificationEmailAsync(ResendVerificationDto dto);
    Task<ApiResponseDto<bool>> ForgotPasswordAsync(ForgotPasswordDto dto);
    Task<ApiResponseDto<bool>> ResetPasswordAsync(ResetPasswordDto dto);
    Task<AuthResponseDto> LoginAsync(LoginRequestDto dto);
    Task<AuthResponseDto> GoogleLoginAsync(GoogleLoginDto dto);
    Task<AuthResponseDto> RefreshTokenAsync(RefreshTokenRequestDto dto);
    Task<bool> RevokeTokenAsync(string refreshToken, Guid userId);
    Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordDto dto, string? ipAddress = null);
}
