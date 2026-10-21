using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using EmergencyDispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Repositories;

public class BlockedPhoneRepository : GenericRepository<BlockedPhoneNumber>, IBlockedPhoneRepository
{
    public BlockedPhoneRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<BlockedPhoneNumber?> GetActiveBlockAsync(string normalizedPhone, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(b => b.PhoneNumber == normalizedPhone
                        && b.RemovedAt == null
                        && (b.ExpiresAt == null || b.ExpiresAt > utcNow))
            .OrderByDescending(b => b.BlockedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<BlockedPhoneNumber> Items, int TotalCount)> GetPagedAsync(
        BlockedNumberStatus? status,
        string? search,
        int pageIndex,
        int pageSize,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var query = _dbSet
            .Include(b => b.BlockedByUser)
            .Include(b => b.RemovedByUser)
            .AsNoTracking()
            .AsQueryable();

        query = status switch
        {
            BlockedNumberStatus.Removed => query.Where(b => b.RemovedAt != null),
            BlockedNumberStatus.Expired => query.Where(b => b.RemovedAt == null && b.ExpiresAt != null && b.ExpiresAt <= utcNow),
            BlockedNumberStatus.Active => query.Where(b => b.RemovedAt == null && (b.ExpiresAt == null || b.ExpiresAt > utcNow)),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var digits = new string(search.Where(char.IsDigit).ToArray());
            query = query.Where(b => (digits.Length > 0 && b.PhoneNumber.Contains(digits)) || b.Reason.Contains(search));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(b => b.BlockedAt)
            .Skip((Math.Max(1, pageIndex) - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<BlockedPhoneNumber?> GetWithUsersAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(b => b.BlockedByUser)
            .Include(b => b.RemovedByUser)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
    }
}
