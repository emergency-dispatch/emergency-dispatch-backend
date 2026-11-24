using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Incident;

namespace EmergencyDispatch.Application.Interfaces;

public interface ICitizenMapService
{
    Task<bool> CanAccessAsync(Guid userId, CancellationToken ct = default);
    Task<ApiResponseDto<PaginatedResultDto<CitizenMapIncidentDto>>> GetAsync(Guid userId, CitizenMapQueryDto query, CancellationToken ct = default);
    Task<ApiResponseDto<CitizenMapIncidentDto>> GetAsync(Guid userId, Guid id, CancellationToken ct = default);
    Task<ApiResponseDto<CitizenMapIncidentDto>> GetByAlertAsync(Guid userId, Guid publicAlertId, CancellationToken ct = default);
}

public interface ICitizenMapNotifier
{
    Task ChangedAsync(CitizenMapChangedDto change, CancellationToken ct = default);
}
