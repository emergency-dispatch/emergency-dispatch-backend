using EmergencyDispatch.Application.Localization;
using System.ComponentModel.DataAnnotations;
using EmergencyDispatch.API.Realtime;
using EmergencyDispatch.Application.DTOs.RescueUnit;
using EmergencyDispatch.Application.Interfaces;
using System.Security.Claims;
using EmergencyDispatch.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace EmergencyDispatch.API.Hubs;

/// <summary>
/// Hub theo dõi vị trí đội cứu hộ realtime (ping 3-5s từ app Rescuer).
/// Endpoint: /hubs/tracking. Operator nhận toàn bộ; người báo nhận vị trí xe đang tới sự cố của mình.
/// </summary>
[Authorize]
public class TrackingHub : Hub
{
    private readonly IRescueUnitService _rescueUnitService;
    private readonly IIncidentService _incidentService;
    private readonly ILogger<TrackingHub> _logger;

    public TrackingHub(IRescueUnitService rescueUnitService, IIncidentService incidentService, ILogger<TrackingHub> logger)
    {
        _rescueUnitService = rescueUnitService;
        _incidentService = incidentService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        await HubGroupHelper.JoinRoleGroupsAsync(Context, Groups);
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
            throw new HubException(LocalizationHelper.Localize("Chỉ nhân viên cứu hộ mới được gửi vị trí."));
        }

        var validation = new List<ValidationResult>();
        if (!Validator.TryValidateObject(message, new ValidationContext(message), validation, true))
        {
            throw new HubException(string.Join("; ", validation.Select(v => LocalizationHelper.Localize(v.ErrorMessage))));
        }

        // Nhân viên cứu hộ chỉ được gửi vị trí của xe thuộc trạm mình
        if (user?.IsInRole(nameof(UserRole.Admin)) != true)
        {
            var stationClaim = user?.FindFirstValue("StationId");
            RescueUnitResponseDto unitInfo;
            try
            {
                unitInfo = await _rescueUnitService.GetByIdAsync(message.RescueUnitId);
            }
            catch (KeyNotFoundException)
            {
                throw new HubException(LocalizationHelper.Localize("Không tìm thấy phương tiện cứu hộ."));
            }

            if (!Guid.TryParse(stationClaim, out var stationId) || unitInfo.StationId != stationId)
            {
                throw new HubException(LocalizationHelper.Localize("Bạn chỉ được gửi vị trí của xe thuộc trạm mình."));
            }
        }

        try
        {
            // Service lưu vị trí và phát ReceiveRescueUnitLocation (sự cố lấy từ lượt phân công đang chạy)
            await _rescueUnitService.UpdateLocationAsync(message.RescueUnitId, new UpdateRescueUnitLocationDto
            {
                Latitude = message.Latitude,
                Longitude = message.Longitude,
                HeadingDegrees = message.HeadingDegrees,
                SpeedKmh = message.SpeedKmh
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TrackingHub: không cập nhật được vị trí xe {RescueUnitId}", message.RescueUnitId);
            throw new HubException(LocalizationHelper.Localize("Không cập nhật được vị trí đội cứu hộ."));
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
