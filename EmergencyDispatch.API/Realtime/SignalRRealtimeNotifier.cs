using EmergencyDispatch.API.Hubs;
using EmergencyDispatch.Application.DTOs.Dispatch;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace EmergencyDispatch.API.Realtime;

/// <summary>
/// Triển khai IRealtimeNotifier bằng SignalR. Lỗi realtime chỉ được log,
/// không làm hỏng luồng nghiệp vụ (SOS vẫn được lưu và phản hồi).
/// </summary>
public class SignalRRealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<IncidentHub> _incidentHub;
    private readonly ILogger<SignalRRealtimeNotifier> _logger;

    public SignalRRealtimeNotifier(IHubContext<IncidentHub> incidentHub, ILogger<SignalRRealtimeNotifier> logger)
    {
        _incidentHub = incidentHub;
        _logger = logger;
    }

    public async Task IncidentCreatedAsync(IncidentResponseDto incident, CancellationToken cancellationToken = default)
    {
        try
        {
            await _incidentHub.Clients.Group(RealtimeContract.OperatorsGroup)
                .SendAsync(RealtimeContract.ReceiveNewIncident, incident, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không đẩy được sự cố mới {IncidentId} qua SignalR.", incident.Id);
        }
    }

    public async Task IncidentUpdatedAsync(IncidentResponseDto incident, CancellationToken cancellationToken = default)
    {
        try
        {
            await _incidentHub.Clients.Group(RealtimeContract.OperatorsGroup)
                .SendAsync(RealtimeContract.ReceiveIncidentUpdate, incident, cancellationToken);

            // Người báo chỉ nhận bản rút gọn (không lộ hồ sơ y tế / danh bạ ICE qua group công khai)
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
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không đẩy được cập nhật sự cố {IncidentId} qua SignalR.", incident.Id);
        }
    }

    public async Task JobAssignedAsync(DispatchAssignmentResponseDto assignment, CancellationToken cancellationToken = default)
    {
        try
        {
            // Bắn tới toàn bộ lực lượng cứu hộ
            await _incidentHub.Clients.Group(RealtimeContract.RescuersGroup)
                .SendAsync(RealtimeContract.ReceiveJobAssigned, assignment, cancellationToken);

            // Bắn tới trạm trực thuộc nếu có thông tin trạm
            if (assignment.RescueUnit != null && assignment.RescueUnit.StationId != Guid.Empty)
            {
                await _incidentHub.Clients.Group(RealtimeContract.StationGroup(assignment.RescueUnit.StationId.ToString()))
                    .SendAsync(RealtimeContract.ReceiveJobAssigned, assignment, cancellationToken);
            }

            // Bắn tới Operator để cập nhật trạng thái phân công
            await _incidentHub.Clients.Group(RealtimeContract.OperatorsGroup)
                .SendAsync(RealtimeContract.ReceiveJobAssigned, assignment, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không đẩy được thông báo phân công nhiệm vụ {AssignmentId} qua SignalR.", assignment.Id);
        }
    }

    public async Task AssignmentStatusChangedAsync(DispatchAssignmentResponseDto assignment, CancellationToken cancellationToken = default)
    {
        try
        {
            // Đẩy tới Operator
            await _incidentHub.Clients.Group(RealtimeContract.OperatorsGroup)
                .SendAsync(RealtimeContract.ReceiveAssignmentStatus, assignment, cancellationToken);

            // Đẩy tới người dân theo dõi sự cố
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
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không đẩy được cập nhật trạng thái phân công {AssignmentId} qua SignalR.", assignment.Id);
        }
    }

    public async Task TrafficHotspotReportedAsync(EmergencyDispatch.Application.DTOs.Traffic.TrafficHotspotResponseDto hotspot, CancellationToken cancellationToken = default)
    {
        try
        {
            await _incidentHub.Clients.Group(RealtimeContract.OperatorsGroup)
                .SendAsync(RealtimeContract.ReceiveTrafficHotspot, hotspot, cancellationToken);

            await _incidentHub.Clients.Group(RealtimeContract.RescuersGroup)
                .SendAsync(RealtimeContract.ReceiveTrafficHotspot, hotspot, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không đẩy được điểm nghẽn giao thông {HotspotId} qua SignalR.", hotspot.Id);
        }
    }

    public async Task EscalationAlertAsync(EmergencyDispatch.Application.DTOs.Escalation.EscalationAlertDto alert, CancellationToken cancellationToken = default)
    {
        try
        {
            // Bắn cảnh báo còi hú / đỏ rực tới toàn bộ Operator và Admin
            await _incidentHub.Clients.Group(RealtimeContract.OperatorsGroup)
                .SendAsync(RealtimeContract.ReceiveEscalationAlert, alert, cancellationToken);

            await _incidentHub.Clients.Group(RealtimeContract.AdminsGroup)
                .SendAsync(RealtimeContract.ReceiveEscalationAlert, alert, cancellationToken);

            // Bắn tới group sự cố
            await _incidentHub.Clients.Group(RealtimeContract.IncidentGroup(alert.IncidentId))
                .SendAsync(RealtimeContract.ReceiveEscalationAlert, alert, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không đẩy được cảnh báo leo thang sự cố {IncidentId} qua SignalR.", alert.IncidentId);
        }
    }
}
