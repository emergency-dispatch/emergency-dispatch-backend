using EmergencyDispatch.Application.DTOs.Catalog;
using EmergencyDispatch.Application.DTOs.Common;

namespace EmergencyDispatch.Application.Interfaces;

public interface ITypeCatalogService
{
    Task<List<RescueUnitTypeDto>> GetUnitTypesAsync(bool includeInactive, CancellationToken cancellationToken = default);
    Task<ApiResponseDto<RescueUnitTypeDto>> CreateUnitTypeAsync(UpsertRescueUnitTypeDto dto, CancellationToken cancellationToken = default);
    Task<ApiResponseDto<RescueUnitTypeDto>> UpdateUnitTypeAsync(Guid id, UpsertRescueUnitTypeDto dto, CancellationToken cancellationToken = default);
    Task<ApiResponseDto<bool>> DeleteUnitTypeAsync(Guid id, CancellationToken cancellationToken = default);

    Task<List<IncidentTypeDto>> GetIncidentTypesAsync(bool includeInactive, CancellationToken cancellationToken = default);
    Task<ApiResponseDto<IncidentTypeDto>> CreateIncidentTypeAsync(UpsertIncidentTypeDto dto, CancellationToken cancellationToken = default);
    Task<ApiResponseDto<IncidentTypeDto>> UpdateIncidentTypeAsync(Guid id, UpsertIncidentTypeDto dto, CancellationToken cancellationToken = default);
    Task<ApiResponseDto<bool>> DeleteIncidentTypeAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ApiResponseDto<IncidentTypeDto>> ReplaceUnitRulesAsync(Guid incidentTypeId, ReplaceUnitRulesDto dto, CancellationToken cancellationToken = default);
}
