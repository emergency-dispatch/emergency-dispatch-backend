using EmergencyDispatch.Application.DTOs.Station;

namespace EmergencyDispatch.Application.Interfaces;

public interface IStationService
{
    Task<IReadOnlyList<StationResponseDto>> GetAllAsync(bool activeOnly = false);
    Task<StationResponseDto> GetByIdAsync(Guid id);
    Task<StationResponseDto> CreateAsync(CreateStationDto dto);
    Task<StationResponseDto> UpdateAsync(Guid id, UpdateStationDto dto);
    Task DeleteAsync(Guid id);
}
