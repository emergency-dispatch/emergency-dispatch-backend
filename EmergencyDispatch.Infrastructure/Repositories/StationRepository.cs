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
                .ThenInclude(u => u.TeamMembership)
                    .ThenInclude(m => m!.Team)
                        .ThenInclude(t => t!.RescueUnit)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<IReadOnlyList<Station>> GetAllActiveAsync()
    {
        return await _dbSet
            .Include(s => s.RescueUnits)
            .Include(s => s.StaffMembers)
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Station>> GetAllWithUnitsAndStaffAsync()
    {
        return await _dbSet
            .Include(s => s.RescueUnits)
            .Include(s => s.StaffMembers)
            .OrderBy(s => s.Name)
            .ToListAsync();
    }
}
