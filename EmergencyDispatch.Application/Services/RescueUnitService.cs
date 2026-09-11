using EmergencyDispatch.Application.DTOs.RescueUnit;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;

namespace EmergencyDispatch.Application.Services;

public class RescueUnitService : IRescueUnitService
{
    private readonly IRescueUnitRepository _rescueUnitRepository;
    private readonly IStationRepository _stationRepository;

    public RescueUnitService(
        IRescueUnitRepository rescueUnitRepository,
        IStationRepository stationRepository)
    {
        _rescueUnitRepository = rescueUnitRepository;
        _stationRepository = stationRepository;
    }

    public async Task<IReadOnlyList<RescueUnitResponseDto>> GetAllAsync()
    {
        var units = await _rescueUnitRepository.GetAllWithStationAsync();
        return units.Select(MapToDto).ToList();
    }

    public async Task<RescueUnitResponseDto> GetByIdAsync(Guid id)
    {
        var unit = await _rescueUnitRepository.GetByIdWithDetailsAsync(id);
        if (unit == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy phương tiện cứu hộ với ID: {id}");
        }

        return MapToDto(unit);
    }

    public async Task<IReadOnlyList<RescueUnitResponseDto>> GetByStationIdAsync(Guid stationId)
    {
        var station = await _stationRepository.GetByIdAsync(stationId);
        if (station == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy trạm cứu hộ với ID: {stationId}");
        }

        var units = await _rescueUnitRepository.GetByStationIdAsync(stationId);
        return units.Select(MapToDto).ToList();
    }

    public async Task<IReadOnlyList<RescueUnitResponseDto>> GetAvailableUnitsAsync(RescueUnitType? unitType = null)
    {
        var units = await _rescueUnitRepository.GetAvailableUnitsAsync(unitType);
        return units.Select(MapToDto).ToList();
    }

    public async Task<RescueUnitResponseDto> CreateAsync(CreateRescueUnitDto dto)
    {
        var station = await _stationRepository.GetByIdAsync(dto.StationId);
        if (station == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy trạm cứu hộ với ID: {dto.StationId}");
        }

        if (await _rescueUnitRepository.PlateNumberExistsAsync(dto.PlateNumber))
        {
            throw new InvalidOperationException($"Biển số / Mã hiệu xe '{dto.PlateNumber}' đã tồn tại trong hệ thống.");
        }

        // Nếu không cung cấp tọa độ ban đầu, mặc định lấy tọa độ của trạm
        var lat = dto.InitialLat ?? station.Latitude;
        var lng = dto.InitialLng ?? station.Longitude;

        var unit = new RescueUnit
        {
            PlateNumber = dto.PlateNumber.Trim().ToUpperInvariant(),
            UnitType = dto.UnitType,
            Status = RescueUnitStatus.Available,
            CurrentLat = lat,
            CurrentLng = lng,
            LastLocationUpdateAt = DateTime.UtcNow,
            StationId = dto.StationId,
            CreatedAt = DateTime.UtcNow
        };

        await _rescueUnitRepository.AddAsync(unit);

        // Nạp lại dữ liệu kèm Station name
        unit.Station = station;
        return MapToDto(unit);
    }

    public async Task<RescueUnitResponseDto> UpdateAsync(Guid id, UpdateRescueUnitDto dto)
    {
        var unit = await _rescueUnitRepository.GetByIdWithDetailsAsync(id);
        if (unit == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy phương tiện cứu hộ với ID: {id}");
        }

        var station = await _stationRepository.GetByIdAsync(dto.StationId);
        if (station == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy trạm cứu hộ với ID: {dto.StationId}");
        }

        if (await _rescueUnitRepository.PlateNumberExistsAsync(dto.PlateNumber, id))
        {
            throw new InvalidOperationException($"Biển số / Mã hiệu xe '{dto.PlateNumber}' đã được sử dụng bởi phương tiện khác.");
        }

        unit.PlateNumber = dto.PlateNumber.Trim().ToUpperInvariant();
        unit.UnitType = dto.UnitType;
        unit.Status = dto.Status;
        unit.StationId = dto.StationId;
        unit.Station = station;
        unit.UpdatedAt = DateTime.UtcNow;

        await _rescueUnitRepository.UpdateAsync(unit);
        return MapToDto(unit);
    }

    public async Task<RescueUnitResponseDto> UpdateLocationAsync(Guid id, UpdateRescueUnitLocationDto dto)
    {
        var unit = await _rescueUnitRepository.GetByIdWithDetailsAsync(id);
        if (unit == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy phương tiện cứu hộ với ID: {id}");
        }

        unit.UpdateLocation(dto.Latitude, dto.Longitude);
        await _rescueUnitRepository.UpdateAsync(unit);

        return MapToDto(unit);
    }

    public async Task<RescueUnitResponseDto> UpdateStatusAsync(Guid id, UpdateRescueUnitStatusDto dto)
    {
        var unit = await _rescueUnitRepository.GetByIdWithDetailsAsync(id);
        if (unit == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy phương tiện cứu hộ với ID: {id}");
        }

        unit.UpdateStatus(dto.Status);
        await _rescueUnitRepository.UpdateAsync(unit);

        return MapToDto(unit);
    }

    public async Task DeleteAsync(Guid id)
    {
        var unit = await _rescueUnitRepository.GetByIdAsync(id);
        if (unit == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy phương tiện cứu hộ với ID: {id}");
        }

        if (unit.Status == RescueUnitStatus.Dispatched || unit.Status == RescueUnitStatus.OnScene)
        {
            throw new InvalidOperationException("Không thể xóa xe đang trong nhiệm vụ cứu hộ khẩn cấp.");
        }

        await _rescueUnitRepository.DeleteAsync(unit);
    }

    private static RescueUnitResponseDto MapToDto(RescueUnit unit)
    {
        return new RescueUnitResponseDto
        {
            Id = unit.Id,
            PlateNumber = unit.PlateNumber,
            UnitType = unit.UnitType,
            Status = unit.Status,
            CurrentLat = unit.CurrentLat,
            CurrentLng = unit.CurrentLng,
            LastLocationUpdateAt = unit.LastLocationUpdateAt,
            StationId = unit.StationId,
            StationName = unit.Station?.Name ?? string.Empty,
            CreatedAt = unit.CreatedAt
        };
    }
}
