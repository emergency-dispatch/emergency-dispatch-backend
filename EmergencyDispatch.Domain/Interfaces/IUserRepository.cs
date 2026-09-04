using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Interfaces;

/// <summary>
/// Repository tùy biến cho thực thể User
/// </summary>
public interface IUserRepository : IGenericRepository<User>
{
    Task<User?> GetByIdWithDetailsAsync(Guid id);
    Task<User?> GetByEmailAsync(string email, bool includeTokens = false);
    Task<User?> GetByGoogleIdAsync(string googleId);
    Task<bool> EmailExistsAsync(string email, Guid? excludeUserId = null);
    Task<(IEnumerable<User> Users, int TotalCount)> GetUsersPagedAsync(
        int pageIndex,
        int pageSize,
        string? search,
        UserRole? role,
        UserStatus? status);
}
