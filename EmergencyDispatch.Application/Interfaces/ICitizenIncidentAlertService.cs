using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Notification;

namespace EmergencyDispatch.Application.Interfaces;

public interface ICitizenIncidentAlertService
{
    Task<int> PublishPendingAsync(CancellationToken cancellationToken = default);
    Task<int> DeliverPendingAsync(CancellationToken cancellationToken = default);
    Task<PaginatedResultDto<CitizenIncidentAlertDto>> GetMineAsync(Guid userId, int pageIndex, int pageSize, CancellationToken cancellationToken = default);
    Task<ApiResponseDto<CitizenIncidentAlertDto>> GetMineAsync(Guid alertId, Guid userId, CancellationToken cancellationToken = default);
}
