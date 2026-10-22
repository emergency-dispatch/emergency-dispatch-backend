using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using EmergencyDispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Repositories;

public class UserRepository : GenericRepository<User>, IUserRepository
{
    public UserRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<User?> GetByIdWithDetailsAsync(Guid id)
    {
        return await _dbSet
            .Include(u => u.Station)
            .Include(u => u.IceContacts)
            .Include(u => u.TeamMembership)
                .ThenInclude(m => m!.Team)
                    .ThenInclude(t => t!.RescueUnit)
            .Include(u => u.RefreshTokens.Where(t => !t.IsRevoked))
            .FirstOrDefaultAsync(u => u.Id == id);
    }

    public async Task<User?> GetByEmailAsync(string email, bool includeTokens = false)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        IQueryable<User> query = _dbSet.Include(u => u.Station);

        if (includeTokens)
        {
            query = query.Include(u => u.RefreshTokens);
        }

        return await query.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);
    }

    public async Task<User?> GetByGoogleIdAsync(string googleId)
    {
        return await _dbSet
            .Include(u => u.Station)
            .FirstOrDefaultAsync(u => u.GoogleId == googleId);
    }

    public async Task<bool> EmailExistsAsync(string email, Guid? excludeUserId = null)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        return await _dbSet.AnyAsync(u =>
            u.Email.ToLower() == normalizedEmail &&
            (!excludeUserId.HasValue || u.Id != excludeUserId.Value));
    }

    public async Task<(IEnumerable<User> Users, int TotalCount)> GetUsersPagedAsync(
        int pageIndex,
        int pageSize,
        string? search,
        UserRole? role,
        UserStatus? status,
        Guid? stationId = null)
    {
        IQueryable<User> query = _dbSet.Include(u => u.Station);

        if (stationId.HasValue)
        {
            query = query.Where(u => u.StationId == stationId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.Trim().ToLower();
            query = query.Where(u =>
                u.FullName.ToLower().Contains(searchLower) ||
                u.Email.ToLower().Contains(searchLower) ||
                (u.PhoneNumber != null && u.PhoneNumber.Contains(searchLower)));
        }

        if (role.HasValue)
        {
            query = query.Where(u => u.Role == role.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(u => u.Status == status.Value);
        }

        var totalCount = await query.CountAsync();

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (users, totalCount);
    }

    public async Task<IReadOnlyList<User>> GetByStationWithTeamAsync(Guid stationId)
    {
        return await _dbSet
            .Include(u => u.TeamMembership)
                .ThenInclude(m => m!.Team)
            .Where(u => u.StationId == stationId)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<User>> GetActiveByRolesAsync(IReadOnlyCollection<UserRole> roles)
    {
        return await _dbSet
            .Where(u => roles.Contains(u.Role) && u.Status == UserStatus.Active)
            .ToListAsync();
    }
}
