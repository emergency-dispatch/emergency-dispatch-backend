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
            stations = await _stationRepository.GetAllWithUnitsAndStaffAsync();
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
            CoverageRadiusKm = dto.CoverageRadiusKm,
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
        station.CoverageRadiusKm = dto.CoverageRadiusKm;
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

        // BR-AD-03: không xóa trạm khi còn xe đang làm nhiệm vụ
        if (station.RescueUnits.Any(r => !r.IsDeleted && RescueUnitService.IsOnMission(r.Status)))
        {
            throw new InvalidOperationException("Không thể xóa hoặc tạm ngưng trạm đang có xe thực hiện nhiệm vụ cứu nạn.");
        }

        // BR-AD-02: xóa mềm trạm → xe trực thuộc chuyển OutOfService, nhân sự được gỡ khỏi trạm
        var now = DateTime.UtcNow;
        foreach (var unit in station.RescueUnits.Where(r => !r.IsDeleted))
        {
            unit.Status = RescueUnitStatus.OutOfService;
            unit.UpdatedAt = now;
        }
        foreach (var staff in station.StaffMembers.ToList())
        {
            staff.StationId = null;
            staff.UpdatedAt = now;
            if (staff.TeamMembership != null)
            {
                staff.TeamMembership.IsDeleted = true;
            }
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

        var staff = station.StaffMembers?.Select(u => new StationStaffMemberDto
        {
            Id = u.Id,
            FullName = u.FullName,
            Email = u.Email,
            PhoneNumber = u.PhoneNumber,
            AvatarUrl = u.AvatarUrl,
            Role = u.Role.ToString(),
            Status = u.Status.ToString(),
            TeamName = u.TeamMembership?.Team?.Name
        }).ToList() ?? new List<StationStaffMemberDto>();

        return new StationResponseDto
        {
            Id = station.Id,
            Name = station.Name,
            Address = station.Address,
            Latitude = station.Latitude,
            Longitude = station.Longitude,
            PhoneNumber = station.PhoneNumber,
            IsActive = station.IsActive,
            CoverageRadiusKm = station.CoverageRadiusKm,
            TotalUnitsCount = units.Count,
            ActiveUnitsCount = units.Count(u => u.Status == RescueUnitStatus.Available),
            StaffCount = staff.Count,
            RescueUnits = units,
            StaffMembers = staff,
            CreatedAt = station.CreatedAt
        };
    }
}
