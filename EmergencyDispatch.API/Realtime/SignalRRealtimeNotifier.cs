using EmergencyDispatch.API.Hubs;
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
}
