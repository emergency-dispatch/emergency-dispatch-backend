using System.Security.Claims;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.API.Services;

/// <summary>
/// Đọc người dùng hiện tại từ JWT của request (dùng cho nhật ký quản trị)
/// </summary>
public class HttpCurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public Guid? UserId => Guid.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? FullName => User?.FindFirstValue(ClaimTypes.Name);

    public UserRole? Role => Enum.TryParse<UserRole>(User?.FindFirstValue(ClaimTypes.Role), out var role) ? role : null;

    public string? IpAddress => _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public Guid? StationId => Guid.TryParse(User?.FindFirstValue("StationId"), out var id) ? id : null;
}
