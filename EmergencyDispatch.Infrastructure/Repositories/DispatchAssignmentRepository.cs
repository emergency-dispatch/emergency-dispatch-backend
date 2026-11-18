using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using EmergencyDispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Repositories;

public class DispatchAssignmentRepository : GenericRepository<DispatchAssignment>, IDispatchAssignmentRepository
{
    public DispatchAssignmentRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<DispatchAssignment?> GetByIdWithDetailsAsync(Guid id)
    {
        return await _dbSet
            .Include(d => d.Incident)
            .Include(d => d.RescueUnit)
                .ThenInclude(r => r!.Station)
            .Include(d => d.StationTeam)
            .Include(d => d.CompletionReport)
                .ThenInclude(c => c!.MediaItems)
            .Include(d => d.CompletionReport)
                .ThenInclude(c => c!.StationTeam)
            .FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<Guid?> GetIncidentIdAsync(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(d => d.Id == assignmentId)
            .Select(d => (Guid?)d.IncidentId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DispatchAssignment>> GetByIncidentIdAsync(Guid incidentId)
    {
        return await _dbSet
            .Include(d => d.RescueUnit)
                .ThenInclude(r => r!.Station)
            .Include(d => d.StationTeam)
            .Include(d => d.CompletionReport)
                .ThenInclude(c => c!.MediaItems)
            .Include(d => d.CompletionReport)
                .ThenInclude(c => c!.StationTeam)
            .Where(d => d.IncidentId == incidentId)
            .OrderByDescending(d => d.DispatchedAt)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<DispatchAssignment>> GetFreshByIncidentIdAsync(Guid incidentId, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AsNoTracking()
            .Include(d => d.CompletionReport)
            .Where(d => d.IncidentId == incidentId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DispatchAssignment>> GetByRescueUnitIdAsync(Guid rescueUnitId)
    {
        // Dùng cho "nhiệm vụ của tôi": kèm xe, tổ và báo cáo nghiệm thu để app xem lại nhiệm vụ đã hoàn thành
        return await _dbSet
            .Include(d => d.Incident)
            .Include(d => d.RescueUnit)
                .ThenInclude(r => r!.Station)
            .Include(d => d.StationTeam)
            .Include(d => d.CompletionReport)
                .ThenInclude(c => c!.MediaItems)
            .Include(d => d.CompletionReport)
                .ThenInclude(c => c!.StationTeam)
            .AsSplitQuery()
            .Where(d => d.RescueUnitId == rescueUnitId)
            .OrderByDescending(d => d.DispatchedAt)
            .ToListAsync();
    }

    public async Task<DispatchAssignment?> GetActiveAssignmentByUnitIdAsync(Guid rescueUnitId)
    {
        return await _dbSet
            .Include(d => d.Incident)
            .Where(d => d.RescueUnitId == rescueUnitId &&
                        (d.Status == DispatchAssignmentStatus.Dispatched ||
                         d.Status == DispatchAssignmentStatus.Accepted ||
                         d.Status == DispatchAssignmentStatus.EnRoute ||
                         d.Status == DispatchAssignmentStatus.OnScene))
            .OrderByDescending(d => d.DispatchedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<DispatchAssignment>> GetAllActiveAsync()
    {
        return await _dbSet
            .AsNoTracking()
            .Where(d => d.Status == DispatchAssignmentStatus.Dispatched ||
                        d.Status == DispatchAssignmentStatus.Accepted ||
                        d.Status == DispatchAssignmentStatus.EnRoute ||
                        d.Status == DispatchAssignmentStatus.OnScene)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<DispatchAssignment>> GetActiveWithDetailsAsync(Guid? stationId, CancellationToken cancellationToken = default)
    {
        var query = _dbSet
            .AsNoTracking()
            .Include(d => d.Incident)
            .Include(d => d.RescueUnit)
                .ThenInclude(r => r!.Station)
            .Include(d => d.StationTeam)
            .Where(d => d.Status == DispatchAssignmentStatus.Dispatched ||
                        d.Status == DispatchAssignmentStatus.Accepted ||
                        d.Status == DispatchAssignmentStatus.EnRoute ||
                        d.Status == DispatchAssignmentStatus.OnScene);

        if (stationId.HasValue)
        {
            query = query.Where(d => (d.Incident != null && d.Incident.ResponsibleStationId == stationId)
                                     || (d.RescueUnit != null && d.RescueUnit.StationId == stationId));
        }

        return await query.OrderBy(d => d.DispatchedAt).ToListAsync(cancellationToken);
    }

    public async Task AddStatusHistoryAsync(AssignmentStatusHistory history)
    {
        await _context.AssignmentStatusHistories.AddAsync(history);
    }

    public async Task AddCompletionReportAsync(CompletionReport report)
    {
        await _context.CompletionReports.AddAsync(report);
    }

    private IQueryable<CompletionReport> CompletionReportsWithDetails() => _context.CompletionReports
        .AsNoTracking()
        .Include(c => c.Incident)
        .Include(c => c.StationTeam)
            .ThenInclude(t => t!.Station)
        .Include(c => c.SubmittedByUser)
        .Include(c => c.MediaItems)
        .Include(c => c.Assignments)
            .ThenInclude(a => a.RescueUnit)
                .ThenInclude(r => r!.Station);

    public async Task<(IReadOnlyList<CompletionReport> Items, int TotalCount)> GetCompletionReportsAsync(
        Guid? stationId,
        Guid? teamId,
        Guid? incidentId,
        Guid? crewTeamId,
        Guid? crewUserId,
        Guid? crewStationId,
        DateTime? from,
        DateTime? to,
        string? search,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.CompletionReports.AsNoTracking();

        if (stationId.HasValue)
        {
            query = query.Where(c => (c.StationTeam != null && c.StationTeam.StationId == stationId.Value)
                                     || (c.Incident != null && c.Incident.ResponsibleStationId == stationId.Value)
                                     || c.Assignments.Any(a => a.RescueUnit != null && a.RescueUnit.StationId == stationId.Value));
        }
        if (crewTeamId.HasValue || crewUserId.HasValue || crewStationId.HasValue)
        {
            query = query.Where(c => (crewTeamId.HasValue && c.StationTeamId == crewTeamId.Value)
                                     || (crewUserId.HasValue && c.SubmittedByUserId == crewUserId.Value)
                                     || (crewStationId.HasValue && c.StationTeamId == null
                                         && c.Assignments.Any(a => a.RescueUnit != null && a.RescueUnit.StationId == crewStationId.Value)));
        }
        if (teamId.HasValue) query = query.Where(c => c.StationTeamId == teamId.Value);
        if (incidentId.HasValue) query = query.Where(c => c.IncidentId == incidentId.Value);
        if (from.HasValue) query = query.Where(c => c.SubmittedAt >= from.Value);
        if (to.HasValue) query = query.Where(c => c.SubmittedAt <= to.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(c => EF.Functions.ILike(c.ActionsSummary, pattern)
                                     || (c.Incident != null && (EF.Functions.ILike(c.Incident.Code, pattern)
                                                                || EF.Functions.ILike(c.Incident.Title, pattern)
                                                                || EF.Functions.ILike(c.Incident.LocationAddress, pattern))));
        }

        var total = await query.CountAsync(cancellationToken);

        var ids = query.OrderByDescending(c => c.SubmittedAt).Select(c => c.Id);
        if (pageSize > 0)
        {
            ids = ids.Skip((Math.Max(1, pageIndex) - 1) * pageSize).Take(pageSize);
        }
        var pageIds = await ids.ToListAsync(cancellationToken);

        var items = await CompletionReportsWithDetails()
            .Where(c => pageIds.Contains(c.Id))
            .OrderByDescending(c => c.SubmittedAt)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<CompletionReport?> GetCompletionReportByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await CompletionReportsWithDetails()
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }
}
