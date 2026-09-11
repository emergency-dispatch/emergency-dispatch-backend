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
}
