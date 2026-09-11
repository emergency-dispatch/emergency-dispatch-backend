using EmergencyDispatch.Application.DTOs.RescueUnit;
using EmergencyDispatch.Application.DTOs.Station;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;

namespace EmergencyDispatch.Application.Services;

public class StationService : IStationService
{
    private readonly IStationRepository _stationRepository;

    public StationService(IStationRepository stationRepository)
    {
        _stationRepository = stationRepository;
    }

    public async Task<IReadOnlyList<StationResponseDto>> GetAllAsync(bool activeOnly = false)
    {
        IReadOnlyList<Station> stations;
        if (activeOnly)
        {
            stations = await _stationRepository.GetAllActiveAsync();
        }
        else
        {
            var all = await _stationRepository.GetAllAsync(include: q => q);
            stations = all.OrderBy(s => s.Name).ToList();
        }

        return stations.Select(MapToDto).ToList();
    }

    public async Task<StationResponseDto> GetByIdAsync(Guid id)
    {
        var station = await _stationRepository.GetByIdWithUnitsAndStaffAsync(id);
        if (station == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy trạm cứu hộ với ID: {id}");
        }

        return MapToDto(station);
    }

    public async Task<StationResponseDto> CreateAsync(CreateStationDto dto)
    {
        var station = new Station
        {
            Name = dto.Name.Trim(),
            Address = dto.Address.Trim(),
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            PhoneNumber = dto.PhoneNumber?.Trim(),
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        await _stationRepository.AddAsync(station);
        return MapToDto(station);
    }

    public async Task<StationResponseDto> UpdateAsync(Guid id, UpdateStationDto dto)
    {
        var station = await _stationRepository.GetByIdWithUnitsAndStaffAsync(id);
        if (station == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy trạm cứu hộ với ID: {id}");
        }

        station.Name = dto.Name.Trim();
        station.Address = dto.Address.Trim();
        station.Latitude = dto.Latitude;
        station.Longitude = dto.Longitude;
        station.PhoneNumber = dto.PhoneNumber?.Trim();
        station.IsActive = dto.IsActive;
        station.UpdatedAt = DateTime.UtcNow;

        await _stationRepository.UpdateAsync(station);
        return MapToDto(station);
    }

    public async Task DeleteAsync(Guid id)
    {
        var station = await _stationRepository.GetByIdWithUnitsAndStaffAsync(id);
        if (station == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy trạm cứu hộ với ID: {id}");
        }

        if (station.RescueUnits.Any(r => r.Status == RescueUnitStatus.Dispatched || r.Status == RescueUnitStatus.OnScene))
        {
            throw new InvalidOperationException("Không thể xóa hoặc tạm ngưng trạm đang có xe thực hiện nhiệm vụ cứu nạn.");
        }

        await _stationRepository.DeleteAsync(station);
    }

    private static StationResponseDto MapToDto(Station station)
    {
        var units = station.RescueUnits?.Select(r => new RescueUnitResponseDto
        {
            Id = r.Id,
            PlateNumber = r.PlateNumber,
            UnitType = r.UnitType,
            Status = r.Status,
            CurrentLat = r.CurrentLat,
            CurrentLng = r.CurrentLng,
            LastLocationUpdateAt = r.LastLocationUpdateAt,
            StationId = station.Id,
            StationName = station.Name,
            CreatedAt = r.CreatedAt
        }).ToList() ?? new List<RescueUnitResponseDto>();

        return new StationResponseDto
        {
            Id = station.Id,
            Name = station.Name,
            Address = station.Address,
            Latitude = station.Latitude,
            Longitude = station.Longitude,
            PhoneNumber = station.PhoneNumber,
            IsActive = station.IsActive,
            TotalUnitsCount = units.Count,
            ActiveUnitsCount = units.Count(u => u.Status == RescueUnitStatus.Available),
            RescueUnits = units,
            CreatedAt = station.CreatedAt
        };
    }
}
