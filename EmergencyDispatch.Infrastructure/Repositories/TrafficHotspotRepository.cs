using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using EmergencyDispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Repositories;

/// <summary>
/// Repository quản lý điểm nghẽn giao thông nội khu
/// </summary>
public class TrafficHotspotRepository : GenericRepository<TrafficHotspot>, ITrafficHotspotRepository
{
    public TrafficHotspotRepository(ApplicationDbContext context) : base(context)
    {
    }

    private IQueryable<TrafficHotspot> WithDetails()
        => _dbSet
            .Include(t => t.ReportedByUser)
            .Include(t => t.ResponsibleStation)
            .Include(t => t.AssignedRescueUnit);

    public async Task<IReadOnlyList<TrafficHotspot>> GetActiveHotspotsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await WithDetails()
            .Where(t => t.Status == TrafficHotspotStatus.Assigned
                        || (t.Status == TrafficHotspotStatus.Active && t.ExpiresAt > now))
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<TrafficHotspot?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
        => WithDetails().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<bool> IsUnitOnHotspotAsync(Guid rescueUnitId, CancellationToken cancellationToken = default)
        => _dbSet.AnyAsync(t => t.AssignedRescueUnitId == rescueUnitId && t.Status == TrafficHotspotStatus.Assigned, cancellationToken);

    public async Task<bool> TryMarkAssignedAsync(Guid id, Guid rescueUnitId, Guid assignedByUserId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var affected = await _dbSet
            .Where(t => t.Id == id && t.Status == TrafficHotspotStatus.Active && t.ExpiresAt > nowUtc)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, TrafficHotspotStatus.Assigned)
                .SetProperty(t => t.AssignedRescueUnitId, rescueUnitId)
                .SetProperty(t => t.AssignedByUserId, assignedByUserId)
                .SetProperty(t => t.AssignedAt, nowUtc)
                .SetProperty(t => t.UpdatedAt, nowUtc), cancellationToken);
        return affected == 1;
    }

    public async Task<int> DeactivateExpiredHotspotsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var expired = await _dbSet
            .Where(t => t.Status == TrafficHotspotStatus.Active && t.ExpiresAt <= now)
            .ToListAsync(cancellationToken);

        if (!expired.Any())
        {
            return 0;
        }

        foreach (var item in expired)
        {
            item.Status = TrafficHotspotStatus.Expired;
            item.IsActive = false;
            item.UpdatedAt = now;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }
}
