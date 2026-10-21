using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Interfaces;

public interface IBlockedPhoneRepository : IGenericRepository<BlockedPhoneNumber>
{
    /// <summary>
    /// Bản ghi chặn còn hiệu lực của số điện thoại (đã chuẩn hóa), null nếu không bị chặn
    /// </summary>
    Task<BlockedPhoneNumber?> GetActiveBlockAsync(string normalizedPhone, DateTime utcNow, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<BlockedPhoneNumber> Items, int TotalCount)> GetPagedAsync(
        BlockedNumberStatus? status,
        string? search,
        int pageIndex,
        int pageSize,
        DateTime utcNow,
        CancellationToken cancellationToken = default);

    Task<BlockedPhoneNumber?> GetWithUsersAsync(Guid id, CancellationToken cancellationToken = default);
}
