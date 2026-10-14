using System.ComponentModel.DataAnnotations;
using EmergencyDispatch.API.Realtime;
using EmergencyDispatch.Application.DTOs.RescueUnit;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Enums;
using Microsoft.AspNetCore.SignalR;

namespace EmergencyDispatch.API.Hubs;

/// <summary>
/// Hub theo dõi vị trí đội cứu hộ realtime (ping 3-5s từ app Rescuer).
/// Endpoint: /hubs/tracking. Operator nhận toàn bộ; người báo nhận vị trí xe đang tới sự cố của mình.
/// </summary>
public class TrackingHub : Hub
{
    private readonly IRescueUnitService _rescueUnitService;
    private readonly ILogger<TrackingHub> _logger;

    public TrackingHub(IRescueUnitService rescueUnitService, ILogger<TrackingHub> logger)
    {
        _rescueUnitService = rescueUnitService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        await HubGroupHelper.JoinRoleGroupsAsync(Context, Groups);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Người báo đăng ký nhận vị trí đội cứu hộ của một sự cố
    /// </summary>
    public Task SubscribeIncident(Guid incidentId)
        => Groups.AddToGroupAsync(Context.ConnectionId, RealtimeContract.IncidentGroup(incidentId));

    public Task UnsubscribeIncident(Guid incidentId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, RealtimeContract.IncidentGroup(incidentId));

    /// <summary>
    /// Rescuer gửi vị trí GPS định kỳ. Lưu vị trí hiện tại của xe và phát tới Operator (+ người báo nếu có IncidentId)
    /// </summary>
    public async Task UpdateLocation(RescueUnitLocationMessage message)
    {
        var user = Context.User;
        var allowed = user?.IsInRole(nameof(UserRole.RescueStaff)) == true
                      || user?.IsInRole(nameof(UserRole.Admin)) == true;
        if (!allowed)
        {
            throw new HubException("Chỉ nhân viên cứu hộ mới được gửi vị trí.");
        }

        var validation = new List<ValidationResult>();
        if (!Validator.TryValidateObject(message, new ValidationContext(message), validation, true))
        {
            throw new HubException(string.Join("; ", validation.Select(v => v.ErrorMessage)));
        }

        try
        {
            await _rescueUnitService.UpdateLocationAsync(message.RescueUnitId, new UpdateRescueUnitLocationDto
            {
                Latitude = message.Latitude,
                Longitude = message.Longitude
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TrackingHub: không cập nhật được vị trí xe {RescueUnitId}", message.RescueUnitId);
            throw new HubException("Không cập nhật được vị trí đội cứu hộ.");
        }

        var payload = new
        {
            message.RescueUnitId,
            message.IncidentId,
            message.Latitude,
            message.Longitude,
            message.HeadingDegrees,
            message.SpeedKmh,
            TimestampUtc = DateTime.UtcNow
        };

        await Clients.Group(RealtimeContract.OperatorsGroup)
            .SendAsync(RealtimeContract.ReceiveRescueUnitLocation, payload);

        if (message.IncidentId.HasValue)
        {
            await Clients.Group(RealtimeContract.IncidentGroup(message.IncidentId.Value))
                .SendAsync(RealtimeContract.ReceiveRescueUnitLocation, payload);
        }
    }
}

/// <summary>
/// Gói tin vị trí realtime từ app Rescuer
/// </summary>
public class RescueUnitLocationMessage
{
    [Required]
    public Guid RescueUnitId { get; set; }

    /// <summary>Sự cố xe đang xử lý (để phát vị trí cho người báo)</summary>
    public Guid? IncidentId { get; set; }

    [Range(-90.0, 90.0)]
    public double Latitude { get; set; }

    [Range(-180.0, 180.0)]
    public double Longitude { get; set; }

    [Range(0.0, 360.0)]
    public double? HeadingDegrees { get; set; }

    [Range(0.0, 300.0)]
    public double? SpeedKmh { get; set; }
}
