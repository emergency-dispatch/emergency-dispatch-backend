using EmergencyDispatch.Application.Localization;
using System.Security.Claims;
using EmergencyDispatch.API.Hubs;
using EmergencyDispatch.API.Realtime;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;

namespace EmergencyDispatch.API.Services;

/// <summary>
/// Kiểm tra access token còn khớp với trạng thái tài khoản trong DB (khóa, xóa, đổi role, đổi trạm).
/// Trạng thái được cache ngắn hạn; khi Admin thay đổi tài khoản thì cache bị xóa ngay nên token cũ bị từ chối ở request kế tiếp.
/// </summary>
public class UserSessionValidator : IUserSessionInvalidator
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private readonly IMemoryCache _cache;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<IncidentHub> _incidentHub;
    private readonly ILogger<UserSessionValidator> _logger;

    public UserSessionValidator(
        IMemoryCache cache,
        IServiceScopeFactory scopeFactory,
        IHubContext<IncidentHub> incidentHub,
        ILogger<UserSessionValidator> logger)
    {
        _cache = cache;
        _scopeFactory = scopeFactory;
        _incidentHub = incidentHub;
        _logger = logger;
    }

    private sealed record SessionState(bool Exists, UserStatus Status, UserRole Role, Guid? StationId);

    private static string CacheKey(Guid userId) => $"session-state:{userId}";

    /// <summary>
    /// Trả về null nếu token hợp lệ, ngược lại trả lý do từ chối
    /// </summary>
    public async Task<string?> ValidateAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return "Token không hợp lệ.";
        }

        var state = await _cache.GetOrCreateAsync(CacheKey(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            using var scope = _scopeFactory.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var user = await users.GetByIdAsync(userId);
            return user == null || user.IsDeleted
                ? new SessionState(false, UserStatus.Inactive, UserRole.Citizen, null)
                : new SessionState(true, user.Status, user.Role, user.StationId);
        });

        if (state == null || !state.Exists)
        {
            return "Tài khoản không còn tồn tại.";
        }
        if (state.Status == UserStatus.Suspended)
        {
            return "Tài khoản đã bị khóa.";
        }
        if (state.Status != UserStatus.Active)
        {
            return "Tài khoản đang tạm ngưng.";
        }
        if (principal.FindFirstValue(ClaimTypes.Role) != state.Role.ToString())
        {
            return "Quyền của tài khoản đã thay đổi. Vui lòng đăng nhập lại.";
        }

        var tokenStation = principal.FindFirstValue("StationId");
        var currentStation = state.StationId?.ToString();
        if (!string.Equals(tokenStation, currentStation, StringComparison.OrdinalIgnoreCase)
            && !(string.IsNullOrEmpty(tokenStation) && string.IsNullOrEmpty(currentStation)))
        {
            return "Trạm làm việc của tài khoản đã thay đổi. Vui lòng đăng nhập lại.";
        }

        return null;
    }

    public async Task InvalidateAsync(Guid userId, string reason, CancellationToken cancellationToken = default)
    {
        _cache.Remove(CacheKey(userId));

        // Mở khóa thì chỉ cần làm mới cache, không đẩy người dùng ra
        if (reason == "unlocked")
        {
            return;
        }

        try
        {
            await _incidentHub.Clients.User(userId.ToString())
                .SendAsync(RealtimeContract.ForceLogout, new { reason }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không gửi được ForceLogout tới người dùng {UserId}", userId);
        }
    }
}

/// <summary>
/// Chặn lời gọi hub của kết nối SignalR đã mở trước khi tài khoản bị khóa / đổi quyền
/// </summary>
public class UserSessionHubFilter : IHubFilter
{
    private readonly UserSessionValidator _validator;

    public UserSessionHubFilter(UserSessionValidator validator)
    {
        _validator = validator;
    }

    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var user = invocationContext.Context.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var reason = await _validator.ValidateAsync(user);
            if (reason != null)
            {
                invocationContext.Context.Abort();
                throw new HubException(LocalizationHelper.Localize(reason));
            }
        }

        return await next(invocationContext);
    }
}
