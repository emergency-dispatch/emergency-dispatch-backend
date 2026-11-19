using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using EmergencyDispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Repositories;

public class IncidentStationTransferRepository(ApplicationDbContext db) : IIncidentStationTransferRepository
{
    public async Task<IReadOnlyList<Guid>> GetUnarmedIdsAsync(DateTime unchangedSinceUtc, int batchSize, CancellationToken cancellationToken = default)
        => await db.Incidents.AsNoTracking().Where(i => i.StationAssignedAt != null && i.StationResponseDueAt == null
            && i.ResponsibleStationId != null && i.DuplicateOfIncidentId == null
            && i.Status == IncidentStatus.AiProcessed && (i.UpdatedAt ?? i.CreatedAt) <= unchangedSinceUtc)
            .OrderBy(i => i.CreatedAt).Take(batchSize).Select(i => i.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<IncidentStationTransfer>> GetPendingDispatchesAsync(int batchSize, CancellationToken cancellationToken = default)
        => await db.IncidentStationTransfers.AsNoTracking()
            .Where(t => t.AutoDispatchAttemptedAt == null
                && t.Incident.StationAssignmentVersion == t.AssignmentVersion
                && t.Incident.ResponsibleStationId == t.ToStationId
                && t.Incident.Status != IncidentStatus.AiProcessing)
            .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).Take(batchSize).ToListAsync(cancellationToken);

    public async Task MarkDispatchAttemptedAsync(Guid transferId, DateTime utcNow, CancellationToken cancellationToken = default)
        => await db.IncidentStationTransfers.Where(t => t.Id == transferId && t.AutoDispatchAttemptedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.AutoDispatchAttemptedAt, utcNow), cancellationToken);

    public async Task AddAsync(IncidentStationTransfer transfer, CancellationToken cancellationToken = default)
        => await db.IncidentStationTransfers.AddAsync(transfer, cancellationToken);

    public async Task<IReadOnlyList<IncidentStationTransfer>> GetHistoryAsync(Guid incidentId, CancellationToken cancellationToken = default)
        => await db.IncidentStationTransfers.AsNoTracking().Where(t => t.IncidentId == incidentId)
            .OrderBy(t => t.AssignmentVersion).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetCandidateIdsAsync(DateTime nowUtc, int batchSize, CancellationToken cancellationToken = default)
    {
        var waiting = new[] { IncidentStatus.Pending, IncidentStatus.AiProcessed, IncidentStatus.Verified, IncidentStatus.Escalated };
        return await db.Incidents.AsNoTracking()
            .Where(i => i.StationAssignedAt != null && i.StationResponseDueAt != null
                && i.ResponsibleStationId != null && i.DuplicateOfIncidentId == null
                && waiting.Contains(i.Status)
                && !i.DispatchAssignments.Any(a => a.Status == DispatchAssignmentStatus.Dispatched
                    || a.Status == DispatchAssignmentStatus.Accepted || a.Status == DispatchAssignmentStatus.EnRoute
                    || a.Status == DispatchAssignmentStatus.OnScene || a.Status == DispatchAssignmentStatus.Completed))
            .Where(i => i.ResponsibleStation == null || !i.ResponsibleStation.IsActive || i.ResponsibleStation.IsDeleted
                || !db.Users.Any(u => !u.IsDeleted && u.Status == UserStatus.Active && u.Role == UserRole.Operator
                    && u.StationId == i.ResponsibleStationId)
                || i.StationAcknowledgedAt == null && i.StationResponseDueAt <= nowUtc
                    && (i.LockedByOperatorId == null || i.LockExpiresAt == null || i.LockExpiresAt <= nowUtc))
            .OrderBy(i => i.StationTransferLastCheckedAt ?? DateTime.MinValue)
            .ThenBy(i => i.StationResponseDueAt).ThenBy(i => i.Id)
            .Take(batchSize).Select(i => i.Id).ToListAsync(cancellationToken);
    }

    public async Task MoveLinkedReportsAsync(Guid parentIncidentId, Guid stationId, CancellationToken cancellationToken = default)
        => await db.Incidents.Where(i => i.DuplicateOfIncidentId == parentIncidentId && i.Status == IncidentStatus.Linked)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.ResponsibleStationId, stationId), cancellationToken);
}
