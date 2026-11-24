using EmergencyDispatch.API.Hubs;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace EmergencyDispatch.API.Realtime;

public sealed class SignalRCitizenMapNotifier(IHubContext<IncidentHub> hub) : ICitizenMapNotifier
{
    public Task ChangedAsync(CitizenMapChangedDto change, CancellationToken ct = default)
        => hub.Clients.Group(RealtimeContract.CitizenMapGroup)
            .SendAsync(RealtimeContract.ReceiveCitizenMapChanged, change, ct);
}
