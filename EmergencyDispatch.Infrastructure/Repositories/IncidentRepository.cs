using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using EmergencyDispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Repositories;

public class IncidentRepository : GenericRepository<Incident>, IIncidentRepository
{
    public IncidentRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<Incident?> GetIncidentWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Incidents
            .Include(i => i.ReportedByUser)
                .ThenInclude(u => u!.IceContacts)
            .Include(i => i.VerifiedByUser)
            .Include(i => i.LockedByOperator)
            .Include(i => i.CancelledByUser)
            .Include(i => i.ResponsibleStation)
            .Include(i => i.MediaItems)
            .Include(i => i.AiClassification)
            .Include(i => i.DispatchAssignments)
                .ThenInclude(da => da.RescueUnit)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
    }

    public async Task<(List<Incident> Items, int TotalCount)> GetFilteredIncidentsAsync(
        IncidentStatus? status,
        SeverityLevel? severity,
        string? searchTerm,
        DateTime? fromDate,
        DateTime? toDate,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default,
        Guid? responsibleStationId = null,
        IReadOnlyCollection<IncidentStatus>? statuses = null)
    {
        var query = _context.Incidents
            .Include(i => i.ReportedByUser)
                .ThenInclude(u => u!.IceContacts)
            .Include(i => i.VerifiedByUser)
            .Include(i => i.ResponsibleStation)
            .Include(i => i.MediaItems)
            .Include(i => i.AiClassification)
            .AsNoTracking();

        if (responsibleStationId.HasValue)
        {
            query = query.Where(i => i.ResponsibleStationId == null || i.ResponsibleStationId == responsibleStationId.Value);
        }

        if (statuses is { Count: > 0 })
        {
            query = query.Where(i => statuses.Contains(i.Status));
        }
        else if (status.HasValue)
        {
            query = query.Where(i => i.Status == status.Value);
        }

        if (severity.HasValue)
        {
            query = query.Where(i => i.Severity == severity.Value);
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLower();
            query = query.Where(i => i.Title.ToLower().Contains(term) ||
                                     i.Description.ToLower().Contains(term) ||
                                     i.LocationAddress.ToLower().Contains(term));
        }

        if (fromDate.HasValue)
        {
            query = query.Where(i => i.CreatedAt >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(i => i.CreatedAt <= toDate.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<List<Incident>> GetPendingQueueAsync(CancellationToken cancellationToken = default, Guid? responsibleStationId = null)
    {
        // Hàng đợi sự cố chưa được Operator xác minh (kể cả đang chờ AI hoặc đã bị leo thang trước khi duyệt)
        // FR-OP-02: Level 0 (Reject / AI lỗi) và Level 3 cùng nhóm ưu tiên cao nhất theo FIFO → Level 2 → Level 1
        return await _context.Incidents
            .Include(i => i.ReportedByUser)
            .Include(i => i.LockedByOperator)
            .Include(i => i.ResponsibleStation)
            .Include(i => i.MediaItems)
            .Include(i => i.AiClassification)
            .Where(i => !responsibleStationId.HasValue || i.ResponsibleStationId == null || i.ResponsibleStationId == responsibleStationId.Value)
            .Where(i => i.Status == IncidentStatus.Pending
                        || i.Status == IncidentStatus.AiProcessing
                        || i.Status == IncidentStatus.AiProcessed
                        || (i.Status == IncidentStatus.Escalated && i.VerifiedAt == null))
            .OrderBy(i => i.Severity == SeverityLevel.Level3 || i.Severity == SeverityLevel.Unclassified ? 0
                        : i.Severity == SeverityLevel.Level2 ? 1
                        : 2)
            .ThenBy(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Guid>> GetStaleUnverifiedIdsAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
    {
        return await _context.Incidents
            .Where(i => (i.Status == IncidentStatus.Pending
                         || i.Status == IncidentStatus.AiProcessing
                         || i.Status == IncidentStatus.AiProcessed)
                        && i.VerifiedAt == null
                        && i.CreatedAt < cutoffUtc)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Incident>> GetStuckAiProcessingAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Incidents
            .AsNoTracking()
            .Include(i => i.MediaItems)
            .Where(i => i.Status == IncidentStatus.AiProcessing && i.AiClassification == null)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAiClassificationAsync(AiClassification classification, CancellationToken cancellationToken = default)
    {
        await _context.AiClassifications.AddAsync(classification, cancellationToken);
    }

    public async Task AddStatusHistoryAsync(IncidentStatusHistory history, CancellationToken cancellationToken = default)
    {
        await _context.IncidentStatusHistories.AddAsync(history, cancellationToken);
    }

    public async Task AddAuditTrailAsync(IncidentAuditTrail auditTrail, CancellationToken cancellationToken = default)
    {
        await _context.IncidentAuditTrails.AddAsync(auditTrail, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryAcquireLockAsync(Guid incidentId, Guid operatorId, DateTime expiresAtUtc, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var affected = await _context.Incidents
            .Where(i => i.Id == incidentId
                        && (i.LockedByOperatorId == null
                            || i.LockExpiresAt == null
                            || i.LockExpiresAt <= nowUtc
                            || i.LockedByOperatorId == operatorId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(i => i.LockedByOperatorId, operatorId)
                .SetProperty(i => i.LockExpiresAt, expiresAtUtc), cancellationToken);

        return affected == 1;
    }

    public async Task<bool> ReleaseLockAsync(Guid incidentId, Guid operatorId, bool force, CancellationToken cancellationToken = default)
    {
        var affected = await _context.Incidents
            .Where(i => i.Id == incidentId && i.LockedByOperatorId != null && (force || i.LockedByOperatorId == operatorId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(i => i.LockedByOperatorId, (Guid?)null)
                .SetProperty(i => i.LockExpiresAt, (DateTime?)null), cancellationToken);

        return affected == 1;
    }

    public async Task<bool> HasRecentActiveIncidentAsync(Guid? userId, string? normalizedPhone, DateTime sinceUtc, CancellationToken cancellationToken = default)
    {
        if (!userId.HasValue && string.IsNullOrWhiteSpace(normalizedPhone))
        {
            return false;
        }

        return await _context.Incidents
            .Where(i => i.CreatedAt >= sinceUtc
                        && i.Status != IncidentStatus.Completed
                        && i.Status != IncidentStatus.Cancelled
                        && ((userId.HasValue && i.ReportedByUserId == userId)
                            || (normalizedPhone != null && i.ReporterPhone == normalizedPhone)))
            .AnyAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<Incident> Items, int TotalCount)> GetByReporterAsync(
        Guid userId, IncidentStatus? status, int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _context.Incidents
            .AsNoTracking()
            .Include(i => i.MediaItems)
            .Include(i => i.DispatchAssignments)
                .ThenInclude(a => a.RescueUnit)
                    .ThenInclude(r => r!.Station)
            .Where(i => i.ReportedByUserId == userId);

        if (status.HasValue)
        {
            query = query.Where(i => i.Status == status.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip((Math.Max(1, pageIndex) - 1) * pageSize)
            .Take(pageSize)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<(IReadOnlyList<IncidentAuditTrail> Items, int TotalCount)> GetAuditTrailsAsync(
        Guid? incidentId, Guid? actorId, AuditAction? action, DateTime? from, DateTime? to,
        int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _context.IncidentAuditTrails
            .AsNoTracking()
            .Include(a => a.Actor)
            .Include(a => a.Incident)
            .AsQueryable();

        if (incidentId.HasValue) query = query.Where(a => a.IncidentId == incidentId.Value);
        if (actorId.HasValue) query = query.Where(a => a.ActorId == actorId.Value);
        if (action.HasValue) query = query.Where(a => a.Action == action.Value);
        if (from.HasValue) query = query.Where(a => a.Timestamp >= from.Value);
        if (to.HasValue) query = query.Where(a => a.Timestamp <= to.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip((Math.Max(1, pageIndex) - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<IReadOnlyList<Incident>> GetForTelemetryAsync(DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
    {
        var query = _context.Incidents
            .AsNoTracking()
            .Include(i => i.AiClassification)
            .Include(i => i.ResponsibleStation)
            .Include(i => i.DispatchAssignments)
            .AsQueryable();

        if (from.HasValue) query = query.Where(i => i.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(i => i.CreatedAt <= to.Value);

        return await query.AsSplitQuery().ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<Incident> Items, int TotalCount)> GetClosedHistoryAsync(
        IReadOnlyCollection<IncidentStatus> statuses,
        DateTime? from,
        DateTime? to,
        Guid? stationId,
        SeverityLevel? severity,
        string? search,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Incidents
            .AsNoTracking()
            .Where(i => statuses.Contains(i.Status));

        if (from.HasValue) query = query.Where(i => i.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(i => i.CreatedAt <= to.Value);
        if (severity.HasValue) query = query.Where(i => i.Severity == severity.Value);
        if (stationId.HasValue)
        {
            // Trạm có ít nhất một xe tham gia (không tính lượt bị từ chối)
            query = query.Where(i => i.DispatchAssignments.Any(a =>
                a.Status != DispatchAssignmentStatus.Rejected && a.RescueUnit != null && a.RescueUnit.StationId == stationId.Value));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(i => EF.Functions.ILike(i.Title, pattern) || EF.Functions.ILike(i.LocationAddress, pattern));
        }

        var total = await query.CountAsync(cancellationToken);

        var ordered = query
            .Include(i => i.VerifiedByUser)
            .Include(i => i.ClosedByUser)
            .Include(i => i.AiClassification)
            .Include(i => i.DispatchAssignments)
                .ThenInclude(a => a.RescueUnit)
                    .ThenInclude(r => r!.Station)
            .OrderByDescending(i => i.CreatedAt)
            .AsQueryable();

        if (pageSize > 0)
        {
            ordered = ordered.Skip((Math.Max(1, pageIndex) - 1) * pageSize).Take(pageSize);
        }

        var items = await ordered.AsSplitQuery().ToListAsync(cancellationToken);
        return (items, total);
    }
}
