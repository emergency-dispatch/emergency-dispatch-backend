using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using EmergencyDispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Repositories;

public class RescueUnitRepository : GenericRepository<RescueUnit>, IRescueUnitRepository
{
    public RescueUnitRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<RescueUnit?> GetByIdWithDetailsAsync(Guid id)
    {
        return await _dbSet
            .Include(r => r.Station)
            .Include(r => r.DispatchAssignments.OrderByDescending(d => d.DispatchedAt))
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<IReadOnlyList<RescueUnit>> GetByStationIdAsync(Guid stationId)
    {
        return await _dbSet
            .Include(r => r.Station)
            .Where(r => r.StationId == stationId)
            .OrderBy(r => r.PlateNumber)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<RescueUnit>> GetAvailableUnitsAsync(RescueUnitType? unitType = null)
    {
        var query = _dbSet
            .Include(r => r.Station)
            .Where(r => r.Status == RescueUnitStatus.Available);

        if (unitType.HasValue)
        {
            query = query.Where(r => r.UnitType == unitType.Value);
        }

        return await query.ToListAsync();
    }

    public async Task<IReadOnlyList<RescueUnit>> GetAllWithStationAsync()
    {
        return await _dbSet
            .Include(r => r.Station)
            .OrderBy(r => r.Station != null ? r.Station.Name : string.Empty)
            .ThenBy(r => r.PlateNumber)
            .ToListAsync();
    }

    public async Task<bool> PlateNumberExistsAsync(string plateNumber, Guid? excludeId = null)
    {
        var normalized = plateNumber.Trim().ToUpperInvariant();
        var query = _dbSet.Where(r => r.PlateNumber.ToUpper() == normalized);

        if (excludeId.HasValue)
        {
            query = query.Where(r => r.Id != excludeId.Value);
        }

        return await query.AnyAsync();
    }

    public async Task<bool> TryReserveForDispatchAsync(Guid unitId, CancellationToken cancellationToken = default)
    {
        var affected = await _dbSet
            .Where(r => r.Id == unitId && r.Status == RescueUnitStatus.Available)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.Status, RescueUnitStatus.Dispatched)
                .SetProperty(r => r.UpdatedAt, DateTime.UtcNow), cancellationToken);

        return affected == 1;
    }

    public async Task ReleaseReservationAsync(Guid unitId, CancellationToken cancellationToken = default)
    {
        await _dbSet
            .Where(r => r.Id == unitId && r.Status == RescueUnitStatus.Dispatched)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.Status, RescueUnitStatus.Available)
                .SetProperty(r => r.UpdatedAt, DateTime.UtcNow), cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ReleaseExpiredCoolDownsAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var expiredIds = await _dbSet
            .Where(r => r.Status == RescueUnitStatus.CoolDown && (r.CoolDownUntil == null || r.CoolDownUntil <= nowUtc))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (expiredIds.Count == 0)
        {
            return expiredIds;
        }

        await _dbSet
            .Where(r => expiredIds.Contains(r.Id) && r.Status == RescueUnitStatus.CoolDown)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.Status, RescueUnitStatus.Available)
                .SetProperty(r => r.CoolDownUntil, (DateTime?)null)
                .SetProperty(r => r.UpdatedAt, nowUtc), cancellationToken);

        return expiredIds;
    }
}
