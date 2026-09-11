using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Interfaces;
using EmergencyDispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Repositories;

public class StationRepository : GenericRepository<Station>, IStationRepository
{
    public StationRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<Station?> GetByIdWithUnitsAndStaffAsync(Guid id)
    {
        return await _dbSet
            .Include(s => s.RescueUnits)
            .Include(s => s.StaffMembers)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<IReadOnlyList<Station>> GetAllActiveAsync()
    {
        return await _dbSet
            .Include(s => s.RescueUnits)
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync();
    }
}
