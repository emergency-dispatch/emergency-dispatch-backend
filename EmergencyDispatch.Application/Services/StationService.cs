using EmergencyDispatch.Application.DTOs.Audit;
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
    private readonly IUserRepository _userRepository;
    private readonly ISystemAuditService _auditService;
    private readonly IUserSessionInvalidator _sessionInvalidator;

    public StationService(IStationRepository stationRepository, IUserRepository userRepository, ISystemAuditService auditService, IUserSessionInvalidator sessionInvalidator)
    {
        _stationRepository = stationRepository;
        _userRepository = userRepository;
        _auditService = auditService;
        _sessionInvalidator = sessionInvalidator;
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
        var headOperator = await ResolveHeadOperatorAsync(dto.HeadOperatorId, null);
        var station = new Station
        {
            Name = dto.Name.Trim(),
            Address = dto.Address.Trim(),
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            PhoneNumber = dto.PhoneNumber?.Trim(),
            IsActive = dto.IsActive,
            CoverageRadiusKm = dto.CoverageRadiusKm,
            HeadOperatorId = headOperator?.Id,
            HeadOperator = headOperator,
            CreatedAt = DateTime.UtcNow
        };

        await _stationRepository.AddAsync(station);
        await SyncHeadOperatorAsync(station, null, headOperator);
        await _auditService.LogAsync(SystemAuditAction.Created, AuditEntityTypes.Station, station.Id, station.Name,
            $"Tạo trạm \"{station.Name}\"", newValue: Snapshot(station));
        return MapToDto(station);
    }

    public async Task<StationResponseDto> UpdateAsync(Guid id, UpdateStationDto dto)
    {
        var station = await _stationRepository.GetByIdWithUnitsAndStaffAsync(id);
        if (station == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy trạm cứu hộ với ID: {id}");
        }

        var before = Snapshot(station);
        var oldHeadId = station.HeadOperatorId;
        var headOperator = await ResolveHeadOperatorAsync(dto.HeadOperatorId, station.Id);

        station.Name = dto.Name.Trim();
        station.Address = dto.Address.Trim();
        station.Latitude = dto.Latitude;
        station.Longitude = dto.Longitude;
        station.PhoneNumber = dto.PhoneNumber?.Trim();
        station.IsActive = dto.IsActive;
        station.CoverageRadiusKm = dto.CoverageRadiusKm;
        station.HeadOperatorId = headOperator?.Id;
        station.HeadOperator = headOperator;
        station.UpdatedAt = DateTime.UtcNow;

        await _stationRepository.UpdateAsync(station);
        await SyncHeadOperatorAsync(station, oldHeadId, headOperator);
        await _auditService.LogAsync(SystemAuditAction.Updated, AuditEntityTypes.Station, station.Id, station.Name,
            $"Cập nhật trạm \"{station.Name}\"", before, Snapshot(station));
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

        var affectedUnits = station.RescueUnits.Where(r => !r.IsDeleted).Select(r => r.PlateNumber).ToList();
        var affectedStaff = station.StaffMembers.Count;
        var oldHeadId = station.HeadOperatorId;
        station.HeadOperatorId = null;

        await _stationRepository.DeleteAsync(station);
        foreach (var userId in station.StaffMembers.Select(u => u.Id).Append(oldHeadId ?? Guid.Empty).Where(id => id != Guid.Empty).Distinct())
        {
            await _sessionInvalidator.InvalidateAsync(userId, "Trạm của bạn đã ngừng hoạt động. Vui lòng đăng nhập lại.");
        }
        await _auditService.LogAsync(SystemAuditAction.Deleted, AuditEntityTypes.Station, station.Id, station.Name,
            $"Xóa trạm \"{station.Name}\": {affectedUnits.Count} xe chuyển OutOfService, {affectedStaff} nhân sự được gỡ khỏi trạm",
            Snapshot(station), new { outOfServiceUnits = affectedUnits, unassignedStaffCount = affectedStaff });
    }

    /// <summary>
    /// Người phụ trách trạm phải là tài khoản Operator đang hoạt động và chưa phụ trách trạm khác (mỗi Operator 1 trạm)
    /// </summary>
    private async Task<User?> ResolveHeadOperatorAsync(Guid? headOperatorId, Guid? stationId)
    {
        if (!headOperatorId.HasValue || headOperatorId == Guid.Empty)
        {
            return null;
        }

        var user = await _userRepository.GetByIdAsync(headOperatorId.Value)
            ?? throw new InvalidOperationException("Không tìm thấy tài khoản Operator được chọn phụ trách trạm.");
        if (user.Role != UserRole.Operator)
        {
            throw new InvalidOperationException("Người phụ trách trạm phải là tài khoản Điều phối viên (Operator).");
        }
        if (user.Status != UserStatus.Active)
        {
            throw new InvalidOperationException("Tài khoản Operator được chọn đang bị khóa hoặc chưa kích hoạt.");
        }

        var otherStation = (await _stationRepository.GetAllAsync(s => s.HeadOperatorId == user.Id && s.Id != stationId)).FirstOrDefault();
        if (otherStation != null)
        {
            throw new InvalidOperationException($"{user.FullName} đang phụ trách trạm \"{otherStation.Name}\". Mỗi Operator chỉ phụ trách 1 trạm, hãy gỡ khỏi trạm đó trước.");
        }

        return user;
    }

    /// <summary>
    /// Đồng bộ trạm của Operator với người phụ trách trạm: Operator mới thuộc trạm này, Operator cũ bỏ trạm.
    /// Token cũ của hai người bị hủy để lấy claim StationId mới.
    /// </summary>
    private async Task SyncHeadOperatorAsync(Station station, Guid? oldHeadId, User? newHead)
    {
        if (oldHeadId.HasValue && oldHeadId != newHead?.Id)
        {
            var oldHead = await _userRepository.GetByIdAsync(oldHeadId.Value);
            if (oldHead != null && oldHead.StationId == station.Id)
            {
                oldHead.StationId = null;
                oldHead.UpdatedAt = DateTime.UtcNow;
                await _userRepository.UpdateAsync(oldHead);
            }
            await _sessionInvalidator.InvalidateAsync(oldHeadId.Value, $"Bạn không còn phụ trách trạm \"{station.Name}\". Vui lòng đăng nhập lại.");
        }

        if (newHead != null && newHead.StationId != station.Id)
        {
            newHead.StationId = station.Id;
            newHead.UpdatedAt = DateTime.UtcNow;
            await _userRepository.UpdateAsync(newHead);
            await _sessionInvalidator.InvalidateAsync(newHead.Id, $"Bạn được phân công phụ trách trạm \"{station.Name}\". Vui lòng đăng nhập lại.");
        }
    }

    private static object Snapshot(Station s) => new
    {
        s.Name,
        s.Address,
        s.Latitude,
        s.Longitude,
        s.PhoneNumber,
        s.IsActive,
        s.CoverageRadiusKm,
        s.HeadOperatorId,
        HeadOperatorName = s.HeadOperator?.FullName
    };

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
            HeadOperatorId = station.HeadOperatorId,
            HeadOperatorName = station.HeadOperator?.FullName,
            HeadOperatorPhone = station.HeadOperator?.PhoneNumber,
            TotalUnitsCount = units.Count,
            ActiveUnitsCount = units.Count(u => u.Status == RescueUnitStatus.Available),
            StaffCount = staff.Count,
            RescueUnits = units,
            StaffMembers = staff,
            CreatedAt = station.CreatedAt
        };
    }
}
