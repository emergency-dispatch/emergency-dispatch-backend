using EmergencyDispatch.Application.DTOs.RescueUnit;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.Interfaces;

public interface IRescueUnitService
{
    Task<IReadOnlyList<RescueUnitResponseDto>> GetAllAsync();
    Task<RescueUnitResponseDto> GetByIdAsync(Guid id);
    Task<IReadOnlyList<RescueUnitResponseDto>> GetByStationIdAsync(Guid stationId);
    Task<IReadOnlyList<RescueUnitResponseDto>> GetAvailableUnitsAsync(RescueUnitType? unitType = null);
    Task<RescueUnitResponseDto> CreateAsync(CreateRescueUnitDto dto);
    Task<RescueUnitResponseDto> UpdateAsync(Guid id, UpdateRescueUnitDto dto);
    Task<RescueUnitResponseDto> UpdateLocationAsync(Guid id, UpdateRescueUnitLocationDto dto);
    Task<RescueUnitResponseDto> UpdateStatusAsync(Guid id, UpdateRescueUnitStatusDto dto);
    Task DeleteAsync(Guid id);
}
