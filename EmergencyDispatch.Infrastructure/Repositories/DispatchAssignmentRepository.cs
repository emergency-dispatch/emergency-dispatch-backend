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
            .Include(d => d.CompletionReport)
                .ThenInclude(c => c!.MediaItems)
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
            .Include(d => d.CompletionReport)
                .ThenInclude(c => c!.MediaItems)
            .Where(d => d.IncidentId == incidentId)
            .OrderByDescending(d => d.DispatchedAt)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<DispatchAssignment>> GetByRescueUnitIdAsync(Guid rescueUnitId)
    {
        return await _dbSet
            .Include(d => d.Incident)
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
}
