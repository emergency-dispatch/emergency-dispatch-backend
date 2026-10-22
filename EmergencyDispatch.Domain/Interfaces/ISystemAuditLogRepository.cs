using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Interfaces;

public interface ISystemAuditLogRepository
{
    /// <summary>
    /// Thêm và lưu ngay (đã SaveChanges)
    /// </summary>
    Task AddAsync(SystemAuditLog log, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<SystemAuditLog> Items, int TotalCount)> GetPagedAsync(
        string? entityType,
        Guid? entityId,
        Guid? actorId,
        SystemAuditAction? action,
        DateTime? from,
        DateTime? to,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);
}
