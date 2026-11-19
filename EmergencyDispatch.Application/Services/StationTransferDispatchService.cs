using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Application.Services;

public interface IStationTransferDispatchService
{
    Task ProcessPendingAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Background retry after a committed handover; survives a restart without a request's Operator identity.
/// Thử điều phối sau khi chuyển trạm đã lưu; không dùng quyền của Operator trạm cũ.
/// </summary>
public sealed class StationTransferDispatchService(
    IIncidentStationTransferRepository transfers,
    IIncidentRepository incidents,
    IAutoDispatchService autoDispatch,
    ILogger<StationTransferDispatchService> logger) : IStationTransferDispatchService
{
    public async Task ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        foreach (var transfer in await transfers.GetPendingDispatchesAsync(100, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                incidents.ClearChangeTracker();
                await autoDispatch.TryAfterStationTransferAsync(transfer.IncidentId, transfer.ToStationId,
                    transfer.AssignmentVersion, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                incidents.ClearChangeTracker();
                // The transfer already notified the receiving Operator; preserve manual handling on failure.
                logger.LogError(ex, "AI dispatch after handover failed for {IncidentId}, station {StationId}; Operator handling remains available.",
                    transfer.IncidentId, transfer.ToStationId);
            }
            finally
            {
                // On shutdown leave it pending for restart; otherwise this is one attempt per handover.
                if (!cancellationToken.IsCancellationRequested)
                    await transfers.MarkDispatchAttemptedAsync(transfer.Id, DateTime.UtcNow, cancellationToken);
            }
        }
    }
}
