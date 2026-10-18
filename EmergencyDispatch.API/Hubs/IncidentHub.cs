using System.Security.Claims;
using EmergencyDispatch.API.Realtime;
using EmergencyDispatch.Domain.Enums;
using Microsoft.AspNetCore.SignalR;

namespace EmergencyDispatch.API.Hubs;

/// <summary>
/// Hub sự cố: đẩy sự cố mới / cập nhật tới Operator, và trạng thái sự cố tới người báo đang theo dõi.
/// Endpoint: /hubs/incidents (JWT qua query string access_token, cho phép khách vãng lai theo dõi SOS của mình)
/// </summary>
public class IncidentHub : Hub
{
    private readonly ILogger<IncidentHub> _logger;

    public IncidentHub(ILogger<IncidentHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        await HubGroupHelper.JoinRoleGroupsAsync(Context, Groups);
        _logger.LogDebug("IncidentHub connected: {ConnectionId} (user={User})",
            Context.ConnectionId, Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "guest");
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Người báo (kể cả khách) đăng ký theo dõi trạng thái một sự cố
    /// </summary>
    public Task SubscribeIncident(Guid incidentId)
        => Groups.AddToGroupAsync(Context.ConnectionId, RealtimeContract.IncidentGroup(incidentId));

    /// <summary>
    /// Hủy theo dõi sự cố
    /// </summary>
    public Task UnsubscribeIncident(Guid incidentId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, RealtimeContract.IncidentGroup(incidentId));
}

/// <summary>
/// Tự động gán kết nối vào nhóm theo vai trò trong JWT
/// </summary>
internal static class HubGroupHelper
{
    public static async Task JoinRoleGroupsAsync(HubCallerContext context, IGroupManager groups)
    {
        var user = context.User;
        if (user?.Identity?.IsAuthenticated != true) return;

        if (user.IsInRole(nameof(UserRole.Operator)) || user.IsInRole(nameof(UserRole.Admin)))
        {
            await groups.AddToGroupAsync(context.ConnectionId, RealtimeContract.OperatorsGroup);
        }

        if (user.IsInRole(nameof(UserRole.Admin)))
        {
            await groups.AddToGroupAsync(context.ConnectionId, RealtimeContract.AdminsGroup);
        }

        if (user.IsInRole(nameof(UserRole.RescueStaff)))
        {
            await groups.AddToGroupAsync(context.ConnectionId, RealtimeContract.RescuersGroup);

            var stationId = user.FindFirstValue("StationId");
            if (!string.IsNullOrWhiteSpace(stationId))
            {
                await groups.AddToGroupAsync(context.ConnectionId, RealtimeContract.StationGroup(stationId));
            }
        }
    }
}
