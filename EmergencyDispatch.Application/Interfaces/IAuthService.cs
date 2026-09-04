using EmergencyDispatch.Application.DTOs.Auth;

namespace EmergencyDispatch.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponseDto> RegisterAsync(RegisterRequestDto dto);
    Task<AuthResponseDto> LoginAsync(LoginRequestDto dto);
    Task<AuthResponseDto> GoogleLoginAsync(GoogleLoginDto dto);
    Task<AuthResponseDto> RefreshTokenAsync(RefreshTokenRequestDto dto);
    Task<bool> RevokeTokenAsync(string refreshToken, Guid userId);
    Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordDto dto);
}
