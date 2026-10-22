using EmergencyDispatch.Application.DTOs.Audit;
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
    private readonly IDispatchAssignmentRepository _assignmentRepository;
    private readonly ISystemAuditService _auditService;
    private readonly ICurrentUserContext _currentUser;
    private readonly IRealtimeNotifier _realtimeNotifier;

    public RescueUnitService(
        IRescueUnitRepository rescueUnitRepository,
        IStationRepository stationRepository,
        IDispatchAssignmentRepository assignmentRepository,
        ISystemAuditService auditService,
        ICurrentUserContext currentUser,
        IRealtimeNotifier realtimeNotifier)
    {
        _currentUser = currentUser;
        _realtimeNotifier = realtimeNotifier;
        _rescueUnitRepository = rescueUnitRepository;
        _stationRepository = stationRepository;
        _assignmentRepository = assignmentRepository;
        _auditService = auditService;
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
        EnsureOwnStation(dto.StationId);
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
        await _auditService.LogAsync(SystemAuditAction.Created, AuditEntityTypes.RescueUnit, unit.Id, unit.PlateNumber,
            $"Thêm xe {unit.PlateNumber} ({unit.UnitType}) vào trạm \"{station.Name}\"", newValue: Snapshot(unit));
        return MapToDto(unit);
    }

    public async Task<RescueUnitResponseDto> UpdateAsync(Guid id, UpdateRescueUnitDto dto)
    {
        var unit = await _rescueUnitRepository.GetByIdWithDetailsAsync(id);
        if (unit == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy phương tiện cứu hộ với ID: {id}");
        }

        // Operator chỉ sửa xe trạm mình; chuyển xe sang trạm khác chỉ Admin
        EnsureOwnStation(unit.StationId);
        if (_currentUser.IsStationScoped() && dto.StationId != unit.StationId)
        {
            throw new UnauthorizedAccessException("Chỉ Admin được chuyển xe sang trạm khác.");
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

        // BR-AD-03: xe đang làm nhiệm vụ không được đổi trạm, đổi trạng thái tay hay đưa đi bảo trì
        var onMission = IsOnMission(unit.Status) || await _assignmentRepository.GetActiveAssignmentByUnitIdAsync(unit.Id) != null;
        if (onMission && (dto.StationId != unit.StationId || dto.Status != unit.Status))
        {
            throw new InvalidOperationException("Không thể đổi trạm hoặc trạng thái của xe đang thực hiện nhiệm vụ cứu nạn.");
        }
        EnsureManualStatusAllowed(dto.Status, unit.Status);

        var before = Snapshot(unit);
        // Chuyển trạm: xe về đỗ tại trạm mới → cập nhật vị trí để gợi ý xe / ETA tính đúng (xe đang làm nhiệm vụ đã bị chặn ở trên)
        if (dto.StationId != unit.StationId)
        {
            unit.CurrentLat = station.Latitude;
            unit.CurrentLng = station.Longitude;
            unit.LastLocationUpdateAt = DateTime.UtcNow;
        }

        unit.PlateNumber = dto.PlateNumber.Trim().ToUpperInvariant();
        unit.UnitType = dto.UnitType;
        unit.Status = dto.Status;
        unit.StationId = dto.StationId;
        unit.Station = station;
        unit.UpdatedAt = DateTime.UtcNow;

        await _rescueUnitRepository.UpdateAsync(unit);
        await _auditService.LogAsync(SystemAuditAction.Updated, AuditEntityTypes.RescueUnit, unit.Id, unit.PlateNumber,
            $"Cập nhật xe {unit.PlateNumber}", before, Snapshot(unit));
        await PublishStatusAsync(unit, null, "Cập nhật thông tin xe");
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

        EnsureOwnStation(unit.StationId);
        if (dto.Status != unit.Status)
        {
            var hasActiveAssignment = await _assignmentRepository.GetActiveAssignmentByUnitIdAsync(unit.Id) != null;
            if (IsOnMission(unit.Status) || hasActiveAssignment)
            {
                throw new InvalidOperationException("Xe đang thực hiện nhiệm vụ: trạng thái được cập nhật tự động theo tiến độ nhiệm vụ.");
            }
            EnsureManualStatusAllowed(dto.Status, unit.Status);
        }

        var oldStatus = unit.Status;
        unit.UpdateStatus(dto.Status);
        if (dto.Status != RescueUnitStatus.CoolDown)
        {
            unit.CoolDownUntil = null;
        }
        await _rescueUnitRepository.UpdateAsync(unit);

        if (oldStatus != dto.Status)
        {
            await PublishStatusAsync(unit, oldStatus, "Đổi trạng thái thủ công");
            await _auditService.LogAsync(SystemAuditAction.StatusChanged, AuditEntityTypes.RescueUnit, unit.Id, unit.PlateNumber,
                $"Đổi trạng thái xe {unit.PlateNumber}: {oldStatus} → {dto.Status}",
                new { status = oldStatus }, new { status = dto.Status });
        }

        return MapToDto(unit);
    }

    public async Task DeleteAsync(Guid id)
    {
        var unit = await _rescueUnitRepository.GetByIdAsync(id);
        if (unit == null)
        {
            throw new KeyNotFoundException($"Không tìm thấy phương tiện cứu hộ với ID: {id}");
        }

        if (IsOnMission(unit.Status) || await _assignmentRepository.GetActiveAssignmentByUnitIdAsync(unit.Id) != null)
        {
            throw new InvalidOperationException("Không thể xóa xe đang trong nhiệm vụ cứu hộ khẩn cấp.");
        }

        await _rescueUnitRepository.DeleteAsync(unit);
        await _auditService.LogAsync(SystemAuditAction.Deleted, AuditEntityTypes.RescueUnit, unit.Id, unit.PlateNumber,
            $"Xóa xe {unit.PlateNumber}", Snapshot(unit));
    }

    /// <summary>
    /// Operator chỉ quản lý xe trạm mình (Admin không giới hạn)
    /// </summary>
    private void EnsureOwnStation(Guid stationId)
    {
        var error = _currentUser.CheckStation(stationId);
        if (error != null)
        {
            throw new UnauthorizedAccessException(error);
        }
    }

    private Task PublishStatusAsync(RescueUnit unit, RescueUnitStatus? previous, string reason)
        => _realtimeNotifier.RescueUnitStatusChangedAsync(new EmergencyDispatch.Application.DTOs.Incident.RescueUnitStatusEventDto
        {
            RescueUnitId = unit.Id,
            PlateNumber = unit.PlateNumber,
            StationId = unit.StationId,
            Status = unit.Status,
            PreviousStatus = previous,
            CoolDownUntil = unit.CoolDownUntil,
            Reason = reason,
            ChangedAt = DateTime.UtcNow
        });

    private static object Snapshot(RescueUnit u) => new
    {
        u.PlateNumber,
        u.UnitType,
        u.Status,
        u.StationId,
        StationName = u.Station?.Name
    };

    /// <summary>
    /// Xe đang trong vòng đời nhiệm vụ (đã nhận lệnh, tại hiện trường, đang quay về)
    /// </summary>
    public static bool IsOnMission(RescueUnitStatus status)
        => status is RescueUnitStatus.Dispatched or RescueUnitStatus.OnScene or RescueUnitStatus.Returning;

    /// <summary>
    /// Trạng thái nhiệm vụ do hệ thống tự chuyển; quản trị viên chỉ được đặt Available / Maintenance / OutOfService
    /// </summary>
    private static void EnsureManualStatusAllowed(RescueUnitStatus newStatus, RescueUnitStatus currentStatus)
    {
        if (newStatus == currentStatus) return;
        if (newStatus is RescueUnitStatus.Dispatched or RescueUnitStatus.OnScene or RescueUnitStatus.CoolDown)
        {
            throw new InvalidOperationException($"Trạng thái '{newStatus}' do hệ thống tự cập nhật theo nhiệm vụ, không đặt thủ công được.");
        }
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
