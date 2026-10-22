using EmergencyDispatch.Application.Localization;
using System.Security.Claims;
using EmergencyDispatch.API.Realtime;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace EmergencyDispatch.API.Hubs;

/// <summary>
/// Hub sự cố: đẩy sự cố mới / cập nhật tới Operator, và trạng thái sự cố tới người báo đang theo dõi.
/// Endpoint: /hubs/incidents (bắt buộc JWT qua query string access_token)
/// </summary>
[Authorize]
public class IncidentHub : Hub
{
    private readonly IIncidentService _incidentService;
    private readonly ILogger<IncidentHub> _logger;

    public IncidentHub(IIncidentService incidentService, ILogger<IncidentHub> logger)
    {
        _incidentService = incidentService;
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
    /// Đăng ký theo dõi một sự cố. Chỉ người báo, Operator/Admin và đội cứu hộ được phân công mới được tham gia (FR-CIT-03, BR-CIT-05)
    /// </summary>
    public async Task SubscribeIncident(Guid incidentId)
    {
        var userId = Guid.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
        if (userId == Guid.Empty || !await _incidentService.CanAccessIncidentAsync(incidentId, userId))
        {
            throw new HubException(LocalizationHelper.Localize("Bạn không có quyền theo dõi sự cố này."));
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeContract.IncidentGroup(incidentId));
    }

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

        // Operator nhận sự kiện của trạm mình phụ trách
        if (user.IsInRole(nameof(UserRole.Operator))
            && Guid.TryParse(user.FindFirstValue("StationId"), out var operatorStationId))
        {
            await groups.AddToGroupAsync(context.ConnectionId, RealtimeContract.OperatorStationGroup(operatorStationId));
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
