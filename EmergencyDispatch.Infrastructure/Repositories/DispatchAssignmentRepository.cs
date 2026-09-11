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
            .FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<IReadOnlyList<DispatchAssignment>> GetByIncidentIdAsync(Guid incidentId)
    {
        return await _dbSet
            .Include(d => d.RescueUnit)
                .ThenInclude(r => r!.Station)
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
}
