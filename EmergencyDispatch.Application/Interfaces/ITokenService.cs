using System.Security.Claims;
using EmergencyDispatch.Domain.Entities;

namespace EmergencyDispatch.Application.Interfaces;

public interface ITokenService
{
    string GenerateAccessToken(User user);
    RefreshToken GenerateRefreshToken(Guid userId);
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
}
