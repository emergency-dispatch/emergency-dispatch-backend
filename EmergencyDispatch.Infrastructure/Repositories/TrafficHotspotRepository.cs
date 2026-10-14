using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Interfaces;
using EmergencyDispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Repositories;

public class TrafficHotspotRepository : GenericRepository<TrafficHotspot>, ITrafficHotspotRepository
{
    public TrafficHotspotRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<TrafficHotspot>> GetActiveHotspotsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _dbSet
            .Include(t => t.ReportedByUser)
            .Where(t => t.IsActive && t.ExpiresAt > now)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> DeactivateExpiredHotspotsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var expired = await _dbSet
            .Where(t => t.IsActive && t.ExpiresAt <= now)
            .ToListAsync(cancellationToken);

        if (!expired.Any())
        {
            return 0;
        }

        foreach (var item in expired)
        {
            item.IsActive = false;
            item.UpdatedAt = now;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }
}
