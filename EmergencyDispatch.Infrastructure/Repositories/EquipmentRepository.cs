using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using EmergencyDispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Repositories;

/// <summary>
/// Repository thiết bị cứu hộ của trạm
/// </summary>
public class EquipmentRepository : GenericRepository<Equipment>, IEquipmentRepository
{
    public EquipmentRepository(ApplicationDbContext context) : base(context)
    {
    }

    private IQueryable<Equipment> WithDetails()
        => _dbSet
            .Include(e => e.Station)
            .Include(e => e.RescueUnit);

    public Task<Equipment?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
        => WithDetails().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Equipment>> GetListAsync(
        Guid? stationId,
        Guid? rescueUnitId,
        EquipmentStatus? status,
        EquipmentCategory? category,
        CancellationToken cancellationToken = default)
    {
        var query = WithDetails();
        if (stationId.HasValue)
        {
            query = query.Where(e => e.StationId == stationId.Value);
        }
        if (rescueUnitId.HasValue)
        {
            query = query.Where(e => e.RescueUnitId == rescueUnitId.Value);
        }
        if (status.HasValue)
        {
            query = query.Where(e => e.Status == status.Value);
        }
        if (category.HasValue)
        {
            query = query.Where(e => e.Category == category.Value);
        }

        return await query
            .OrderBy(e => e.Station!.Name)
            .ThenBy(e => e.Name)
            .ToListAsync(cancellationToken);
    }
}
