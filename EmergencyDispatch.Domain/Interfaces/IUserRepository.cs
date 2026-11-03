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
        UserStatus? status,
        Guid? stationId = null);

    /// <summary>
    /// Nhân sự thuộc trạm kèm tổ làm việc
    /// </summary>
    Task<IReadOnlyList<User>> GetByStationWithTeamAsync(Guid stationId);

    /// <summary>
    /// Nhân viên cứu hộ đang hoạt động chưa được gán trạm (để Operator kéo về trạm mình)
    /// </summary>
    Task<(IReadOnlyList<User> Users, int TotalCount)> GetUnassignedRescueStaffPagedAsync(string? search, int pageIndex, int pageSize);

    /// <summary>
    /// Người dùng đang hoạt động theo danh sách vai trò (VD gửi thông báo cho toàn bộ Operator/Admin)
    /// </summary>
    Task<IReadOnlyList<User>> GetActiveByRolesAsync(IReadOnlyCollection<EmergencyDispatch.Domain.Enums.UserRole> roles);
}
