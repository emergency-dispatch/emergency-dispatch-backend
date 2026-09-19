using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Interfaces;
using EmergencyDispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Repositories;

public class IceContactRepository : GenericRepository<IceContact>, IIceContactRepository
{
    public IceContactRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<IceContact>> GetByUserIdAsync(Guid userId)
    {
        return await _dbSet
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.IsPrimary)
            .ThenByDescending(c => c.CreatedAt)
            .ToListAsync();
    }
}
