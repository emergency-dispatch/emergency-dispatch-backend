using EmergencyDispatch.API.Hubs;
using EmergencyDispatch.Application.DTOs.Dispatch;
using EmergencyDispatch.Application.DTOs.Escalation;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.DTOs.Notification;
using EmergencyDispatch.Application.DTOs.Traffic;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace EmergencyDispatch.API.Realtime;

/// <summary>
/// Triển khai IRealtimeNotifier bằng SignalR. Lỗi realtime chỉ được log,
/// không làm hỏng luồng nghiệp vụ (SOS vẫn được lưu và phản hồi).
/// </summary>
/// <remarks>
/// Nhóm: "operators" = mọi Operator + Admin; "operators:station:{id}" = Operator của trạm;
/// "admins" = Admin; "station:{id}" = nhân viên cứu hộ của trạm; "incident:{id}" = người đang theo dõi sự cố.
/// Lệnh / tiến độ nhiệm vụ của một xe gửi theo userId của đội nhận lệnh (CrewScope), không gửi cả nhóm trạm.
/// </remarks>
public class SignalRRealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<IncidentHub> _incidentHub;
    private readonly IHubContext<TrackingHub> _trackingHub;
    private readonly ILogger<SignalRRealtimeNotifier> _logger;

    public SignalRRealtimeNotifier(IHubContext<IncidentHub> incidentHub, IHubContext<TrackingHub> trackingHub, ILogger<SignalRRealtimeNotifier> logger)
    {
        _incidentHub = incidentHub;
        _trackingHub = trackingHub;
        _logger = logger;
    }

    /// <summary>
    /// Trung tâm chỉ huy của một sự cố: Operator trạm phụ trách + Admin; sự cố chưa có trạm → mọi Operator
    /// </summary>
    private IClientProxy CommandCenter(Guid? responsibleStationId, params Guid[] extraStationIds)
    {
        if (!responsibleStationId.HasValue)
        {
            return _incidentHub.Clients.Group(RealtimeContract.OperatorsGroup);
        }

        var groups = new List<string> { RealtimeContract.OperatorStationGroup(responsibleStationId.Value), RealtimeContract.AdminsGroup };
        groups.AddRange(extraStationIds.Where(id => id != Guid.Empty && id != responsibleStationId).Distinct().Select(RealtimeContract.OperatorStationGroup));
        return _incidentHub.Clients.Groups(groups);
    }

    /// <summary>
    /// Đội nhận lệnh của xe theo userId (không dùng nhóm trạm: tổ khác cùng trạm không được nhận)
    /// </summary>
    private IClientProxy Crew(IReadOnlyCollection<Guid> crewUserIds)
        => _incidentHub.Clients.Users(crewUserIds.Distinct().Select(id => id.ToString()).ToList());

    private async Task SafeAsync(Func<Task> send, string what, object? id)
    {
        try
        {
            await send();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không đẩy được {What} {Id} qua SignalR.", what, id);
        }
    }

    public Task IncidentCreatedAsync(IncidentResponseDto incident, CancellationToken cancellationToken = default)
        => SafeAsync(() => CommandCenter(incident.ResponsibleStationId)
            .SendAsync(RealtimeContract.ReceiveNewIncident, incident, cancellationToken), "sự cố mới", incident.Id);

    public Task IncidentUpdatedAsync(IncidentResponseDto incident, CancellationToken cancellationToken = default)
        => SafeAsync(async () =>
        {
            await CommandCenter(incident.ResponsibleStationId)
                .SendAsync(RealtimeContract.ReceiveIncidentUpdate, incident, cancellationToken);

            // Người báo / đội cứu hộ đang theo dõi chỉ nhận bản rút gọn (không lộ hồ sơ y tế / danh bạ ICE)
            var statusPayload = new
            {
                incident.Id,
                incident.Status,
                incident.Severity,
                AiSummary = incident.AiClassification?.Summary,
                incident.UpdatedAt
            };
            await _incidentHub.Clients.Group(RealtimeContract.IncidentGroup(incident.Id))
                .SendAsync(RealtimeContract.ReceiveIncidentStatus, statusPayload, cancellationToken);
        }, "cập nhật sự cố", incident.Id);

    public Task JobAssignedAsync(DispatchAssignmentResponseDto assignment, IReadOnlyCollection<Guid> crewUserIds, CancellationToken cancellationToken = default)
        => SafeAsync(async () =>
        {
            var unitStationId = assignment.RescueUnit?.StationId ?? Guid.Empty;

            // Chỉ đội nhận lệnh của xe: tổ phụ trách xe, hoặc nhân viên trạm nếu xe chưa gán tổ (CrewScope)
            await Crew(crewUserIds).SendAsync(RealtimeContract.ReceiveJobAssigned, assignment, cancellationToken);

            await CommandCenter(assignment.ResponsibleStationId, unitStationId)
                .SendAsync(RealtimeContract.ReceiveJobAssigned, assignment, cancellationToken);
        }, "lệnh phân công", assignment.Id);

    public Task AssignmentStatusChangedAsync(DispatchAssignmentResponseDto assignment, IReadOnlyCollection<Guid> crewUserIds, CancellationToken cancellationToken = default)
        => SafeAsync(async () =>
        {
            var unitStationId = assignment.RescueUnit?.StationId ?? Guid.Empty;

            await CommandCenter(assignment.ResponsibleStationId, unitStationId)
                .SendAsync(RealtimeContract.ReceiveAssignmentStatus, assignment, cancellationToken);

            // Một thành viên cập nhật tiến độ → cả tổ thấy; tổ khác cùng trạm không nhận
            await Crew(crewUserIds).SendAsync(RealtimeContract.ReceiveAssignmentStatus, assignment, cancellationToken);

            // Người dân theo dõi sự cố: bản rút gọn
            await _incidentHub.Clients.Group(RealtimeContract.IncidentGroup(assignment.IncidentId))
                .SendAsync(RealtimeContract.ReceiveAssignmentStatus, new
                {
                    assignment.Id,
                    assignment.IncidentId,
                    RescueUnitPlate = assignment.RescueUnit?.PlateNumber,
                    RescueUnitType = assignment.RescueUnit?.UnitType,
                    assignment.Status,
                    assignment.DispatchedAt,
                    assignment.AcceptedAt,
                    assignment.ArrivedAt,
                    assignment.CompletedAt
                }, cancellationToken);
        }, "trạng thái phân công", assignment.Id);

    public Task TrafficHotspotReportedAsync(TrafficHotspotResponseDto hotspot, CancellationToken cancellationToken = default)
        => SafeAsync(() => _incidentHub.Clients.Groups(RealtimeContract.OperatorsGroup, RealtimeContract.RescuersGroup)
            .SendAsync(RealtimeContract.ReceiveTrafficHotspot, hotspot, cancellationToken), "điểm nghẽn", hotspot.Id);

    public Task TrafficHotspotEscalatedAsync(TrafficHotspotResponseDto hotspot, CancellationToken cancellationToken = default)
        => SafeAsync(() => CommandCenter(hotspot.ResponsibleStationId)
            .SendAsync(RealtimeContract.ReceiveTrafficHotspotEscalation, hotspot, cancellationToken), "leo thang điểm nghẽn", hotspot.Id);

    public Task FieldRequestChangedAsync(FieldRequestEventDto change, IReadOnlyCollection<Guid> crewUserIds, CancellationToken cancellationToken = default)
        => SafeAsync(async () =>
        {
            var request = change.Request;
            await Crew(crewUserIds).SendAsync(RealtimeContract.ReceiveFieldRequest, change, cancellationToken);

            await CommandCenter(request.ResponsibleStationId, request.UnitStationId)
                .SendAsync(RealtimeContract.ReceiveFieldRequest, change, cancellationToken);
        }, "yêu cầu từ hiện trường", change.Request.Id);

    public Task IncidentSupplementAddedAsync(IncidentSupplementDto supplement, Guid? responsibleStationId,
        IReadOnlyCollection<Guid> assignedStationIds, IReadOnlyCollection<Guid> crewUserIds, CancellationToken cancellationToken = default)
        => SafeAsync(async () =>
        {
            await Crew(crewUserIds).SendAsync(RealtimeContract.ReceiveIncidentSupplement, supplement, cancellationToken);

            await CommandCenter(responsibleStationId, assignedStationIds.ToArray())
                .SendAsync(RealtimeContract.ReceiveIncidentSupplement, supplement, cancellationToken);
            await _incidentHub.Clients.Group(RealtimeContract.IncidentGroup(supplement.TargetIncidentId))
                .SendAsync(RealtimeContract.ReceiveIncidentSupplement, supplement, cancellationToken);
        }, "thông tin bổ sung", supplement.TargetIncidentId);

    public Task TrafficTaskChangedAsync(TrafficTaskEventDto task, IReadOnlyCollection<Guid> crewUserIds, CancellationToken cancellationToken = default)
        => SafeAsync(async () =>
        {
            // Chỉ đội nhận lệnh của xe được giao (giống ReceiveJobAssigned)
            await Crew(crewUserIds).SendAsync(RealtimeContract.ReceiveTrafficTaskAssigned, task, cancellationToken);

            await CommandCenter(task.Hotspot.ResponsibleStationId, task.UnitStationId)
                .SendAsync(RealtimeContract.ReceiveTrafficTaskAssigned, task, cancellationToken);
        }, "nhiệm vụ giải tỏa điểm nghẽn", task.Hotspot.Id);

    public Task EscalationAlertAsync(EscalationAlertDto alert, CancellationToken cancellationToken = default)
        => SafeAsync(async () =>
        {
            // Cấp 2 (quá gấp đôi ngưỡng) hoặc sự cố chưa có trạm → mọi Operator; cấp 1 → trạm phụ trách + Admin
            var target = alert.Tier >= 2 || !alert.ResponsibleStationId.HasValue
                ? _incidentHub.Clients.Group(RealtimeContract.OperatorsGroup)
                : CommandCenter(alert.ResponsibleStationId);
            await target.SendAsync(RealtimeContract.ReceiveEscalationAlert, alert, cancellationToken);

            await _incidentHub.Clients.Group(RealtimeContract.IncidentGroup(alert.IncidentId))
                .SendAsync(RealtimeContract.ReceiveEscalationAlert, alert, cancellationToken);
        }, "cảnh báo leo thang", alert.IncidentId);

    public Task NotificationCreatedAsync(Guid userId, NotificationDto notification, CancellationToken cancellationToken = default)
        => SafeAsync(() => _incidentHub.Clients.User(userId.ToString())
            .SendAsync(RealtimeContract.ReceiveNotification, notification, cancellationToken), "thông báo", notification.Id);

    public Task AutoDispatchResultAsync(AutoDispatchEventDto result, CancellationToken cancellationToken = default)
        => SafeAsync(() => CommandCenter(result.ResponsibleStationId)
            .SendAsync(RealtimeContract.ReceiveAutoDispatched, result, cancellationToken), "kết quả AI tự điều phối", result.IncidentId);

    public Task OutOfAreaAlertAsync(OutOfAreaAlertDto alert, CancellationToken cancellationToken = default)
        => SafeAsync(() => CommandCenter(alert.ResponsibleStationId)
            .SendAsync(RealtimeContract.ReceiveOutOfAreaAlert, alert, cancellationToken), "cảnh báo ngoài khu vực", alert.IncidentId);

    public Task SupportDispatchedAsync(Guid supportingStationId, DispatchAssignmentResponseDto assignment, CancellationToken cancellationToken = default)
        => SafeAsync(() => _incidentHub.Clients.Group(RealtimeContract.OperatorStationGroup(supportingStationId))
            .SendAsync(RealtimeContract.ReceiveSupportDispatched, assignment, cancellationToken), "điều xe hỗ trợ", assignment.Id);

    public Task IncidentTransferredAsync(IncidentTransferredDto transfer, IncidentResponseDto incident, CancellationToken cancellationToken = default)
        => SafeAsync(async () =>
        {
            var groups = new List<string> { RealtimeContract.OperatorStationGroup(transfer.ToStationId), RealtimeContract.AdminsGroup };
            if (transfer.FromStationId.HasValue)
            {
                groups.Add(RealtimeContract.OperatorStationGroup(transfer.FromStationId.Value));
            }
            await _incidentHub.Clients.Groups(groups).SendAsync(RealtimeContract.ReceiveIncidentTransferred, transfer, cancellationToken);

            // Trạm nhận thấy sự cố xuất hiện trong hàng đợi của mình ngay
            await _incidentHub.Clients.Group(RealtimeContract.OperatorStationGroup(transfer.ToStationId))
                .SendAsync(RealtimeContract.ReceiveNewIncident, incident, cancellationToken);
        }, "chuyển trạm", transfer.IncidentId);

    public Task IncidentLockChangedAsync(IncidentLockEventDto lockEvent, Guid? responsibleStationId, CancellationToken cancellationToken = default)
        => SafeAsync(() => CommandCenter(responsibleStationId)
            .SendAsync(RealtimeContract.ReceiveIncidentLock, lockEvent, cancellationToken), "khóa mềm", lockEvent.IncidentId);

    public Task RescueUnitLocationAsync(RescueUnitLocationEventDto location, CancellationToken cancellationToken = default)
        => SafeAsync(() =>
        {
            // Live map nằm trên /hubs/tracking: Operator / Admin, nhân viên trạm có xe, người theo dõi sự cố xe đang xử lý
            var groups = new List<string> { RealtimeContract.OperatorsGroup, RealtimeContract.StationGroup(location.StationId.ToString()) };
            if (location.IncidentId.HasValue)
            {
                groups.Add(RealtimeContract.IncidentGroup(location.IncidentId.Value));
            }
            return _trackingHub.Clients.Groups(groups).SendAsync(RealtimeContract.ReceiveRescueUnitLocation, location, cancellationToken);
        }, "vị trí xe", location.RescueUnitId);

    public Task TeamChangedAsync(TeamChangedEventDto change, CancellationToken cancellationToken = default)
        => SafeAsync(() => _incidentHub.Clients.Groups(
                RealtimeContract.StationGroup(change.StationId.ToString()),
                RealtimeContract.OperatorStationGroup(change.StationId),
                RealtimeContract.AdminsGroup)
            .SendAsync(RealtimeContract.ReceiveTeamChanged, change, cancellationToken), "thay đổi tổ", change.TeamId);

    public Task SettingsChangedAsync(SettingsChangedEventDto change, CancellationToken cancellationToken = default)
        => SafeAsync(() => _incidentHub.Clients.Group(RealtimeContract.OperatorsGroup)
            .SendAsync(RealtimeContract.ReceiveSettingsChanged, change, cancellationToken), "cấu hình", change.Key);

    public Task NotificationReadAsync(Guid userId, NotificationReadEventDto read, CancellationToken cancellationToken = default)
        => SafeAsync(() => _incidentHub.Clients.User(userId.ToString())
            .SendAsync(RealtimeContract.ReceiveNotificationRead, read, cancellationToken), "đã đọc thông báo", userId);

    public Task RescueUnitStatusChangedAsync(RescueUnitStatusEventDto unit, CancellationToken cancellationToken = default)
        => SafeAsync(() => _incidentHub.Clients.Groups(RealtimeContract.OperatorsGroup, RealtimeContract.StationGroup(unit.StationId.ToString()))
            .SendAsync(RealtimeContract.ReceiveRescueUnitStatus, unit, cancellationToken), "trạng thái xe", unit.RescueUnitId);
}
