using EmergencyDispatch.Domain.Entities;

namespace EmergencyDispatch.Domain.Interfaces;

public interface IIncidentStationTransferRepository
{
    Task<IReadOnlyList<Guid>> GetUnarmedIdsAsync(DateTime unchangedSinceUtc, int batchSize, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IncidentStationTransfer>> GetPendingDispatchesAsync(int batchSize, CancellationToken cancellationToken = default);
    Task MarkDispatchAttemptedAsync(Guid transferId, DateTime utcNow, CancellationToken cancellationToken = default);
    Task AddAsync(IncidentStationTransfer transfer, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IncidentStationTransfer>> GetHistoryAsync(Guid incidentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> GetCandidateIdsAsync(DateTime nowUtc, int batchSize, CancellationToken cancellationToken = default);
    Task MoveLinkedReportsAsync(Guid parentIncidentId, Guid stationId, CancellationToken cancellationToken = default);
}
