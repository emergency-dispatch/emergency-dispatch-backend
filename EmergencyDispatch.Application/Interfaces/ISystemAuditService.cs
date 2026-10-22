using EmergencyDispatch.Application.DTOs.Audit;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.Interfaces;

public interface ISystemAuditService
{
    /// <summary>
    /// Ghi một thao tác quản trị. Người thực hiện lấy từ request hiện tại.
    /// Không bao giờ ném lỗi: ghi nhật ký thất bại không được làm hỏng thao tác chính.
    /// </summary>
    Task LogAsync(
        SystemAuditAction action,
        string entityType,
        Guid? entityId,
        string? entityName,
        string description,
        object? oldValue = null,
        object? newValue = null,
        CancellationToken cancellationToken = default);

    Task<PaginatedResultDto<SystemAuditLogDto>> GetPagedAsync(SystemAuditLogFilterDto filter, CancellationToken cancellationToken = default);
}
