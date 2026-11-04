using EmergencyDispatch.Application.DTOs.Audit;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Equipment;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;

namespace EmergencyDispatch.Application.Services;

/// <summary>
/// Quản lý thiết bị cứu hộ của trạm (Phiếu đăng ký §3.2.c: trạng thái Available / Maintenance).
/// Admin: mọi trạm. Operator: thêm / sửa / xóa thiết bị trạm mình. RescueStaff: xem và đổi trạng thái thiết bị trạm mình.
/// </summary>
public class EquipmentService : IEquipmentService
{
    public const string NotFoundMessage = "Không tìm thấy thiết bị.";
    public const string OtherStationMessage = "Bạn chỉ được thao tác thiết bị của trạm mình.";
    public const string StationNotFoundMessage = "Không tìm thấy trạm cứu hộ.";
    public const string UnitNotFoundMessage = "Không tìm thấy xe cứu hộ được chọn.";
    public const string UnitOtherStationMessage = "Xe được chọn không thuộc trạm sở hữu thiết bị.";
    public const string OnlyAdminMovesStationMessage = "Chỉ Admin được chuyển thiết bị sang trạm khác.";
    public const string MaintenanceReasonMessage = "Vui lòng nhập lý do bảo trì (tối thiểu 5 ký tự).";
    private const int MinReasonLength = 5;

    private readonly IEquipmentRepository _equipmentRepository;
    private readonly IStationRepository _stationRepository;
    private readonly IRescueUnitRepository _rescueUnitRepository;
    private readonly ICurrentUserContext _currentUser;
    private readonly ISystemAuditService _auditService;

    public EquipmentService(
        IEquipmentRepository equipmentRepository,
        IStationRepository stationRepository,
        IRescueUnitRepository rescueUnitRepository,
        ICurrentUserContext currentUser,
        ISystemAuditService auditService)
    {
        _equipmentRepository = equipmentRepository;
        _stationRepository = stationRepository;
        _rescueUnitRepository = rescueUnitRepository;
        _currentUser = currentUser;
        _auditService = auditService;
    }

    public async Task<ApiResponseDto<List<EquipmentDto>>> GetListAsync(EquipmentFilterDto filter, CancellationToken cancellationToken = default)
    {
        // Operator / RescueStaff chỉ xem trạm mình: bỏ trống bộ lọc trạm thì tự lọc theo trạm mình
        var stationId = filter.StationId;
        if (IsStationBound())
        {
            stationId ??= _currentUser.StationId;
            var error = CheckStationAccess(stationId);
            if (error != null)
            {
                return ApiResponseDto<List<EquipmentDto>>.ForbiddenResult(error);
            }
        }

        var items = await _equipmentRepository.GetListAsync(stationId, filter.RescueUnitId, filter.Status, filter.Category, cancellationToken);
        return ApiResponseDto<List<EquipmentDto>>.SuccessResult(items.Select(Map).ToList(), "Lấy danh sách thiết bị thành công.");
    }

    public async Task<ApiResponseDto<EquipmentDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var equipment = await _equipmentRepository.GetWithDetailsAsync(id, cancellationToken);
        if (equipment == null)
        {
            return ApiResponseDto<EquipmentDto>.NotFoundResult(NotFoundMessage);
        }

        var error = CheckStationAccess(equipment.StationId);
        return error != null
            ? ApiResponseDto<EquipmentDto>.ForbiddenResult(error)
            : ApiResponseDto<EquipmentDto>.SuccessResult(Map(equipment), "Lấy thông tin thiết bị thành công.");
    }

    public async Task<ApiResponseDto<EquipmentDto>> CreateAsync(CreateEquipmentDto dto, CancellationToken cancellationToken = default)
    {
        var accessError = CheckStationAccess(dto.StationId);
        if (accessError != null)
        {
            return ApiResponseDto<EquipmentDto>.ForbiddenResult(accessError);
        }

        var placementError = await ValidatePlacementAsync(dto.StationId, dto.RescueUnitId);
        if (placementError != null)
        {
            return placementError;
        }

        var now = DateTime.UtcNow;
        var equipment = new Equipment
        {
            Name = dto.Name.Trim(),
            Category = dto.Category,
            Quantity = dto.Quantity,
            StationId = dto.StationId,
            RescueUnitId = dto.RescueUnitId,
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
            Status = EquipmentStatus.Available,
            StatusChangedAt = now,
            CreatedAt = now
        };

        await _equipmentRepository.AddAsync(equipment);
        await _auditService.LogAsync(SystemAuditAction.Created, AuditEntityTypes.Equipment, equipment.Id, equipment.Name,
            $"Thêm thiết bị \"{equipment.Name}\" (x{equipment.Quantity})", newValue: Snapshot(equipment), cancellationToken: cancellationToken);

        return await ReloadAsync(equipment, "Thêm thiết bị thành công.", cancellationToken);
    }

    public async Task<ApiResponseDto<EquipmentDto>> UpdateAsync(Guid id, UpdateEquipmentDto dto, CancellationToken cancellationToken = default)
    {
        var equipment = await _equipmentRepository.GetWithDetailsAsync(id, cancellationToken);
        if (equipment == null)
        {
            return ApiResponseDto<EquipmentDto>.NotFoundResult(NotFoundMessage);
        }

        var accessError = CheckStationAccess(equipment.StationId);
        if (accessError != null)
        {
            return ApiResponseDto<EquipmentDto>.ForbiddenResult(accessError);
        }
        if (dto.StationId != equipment.StationId && _currentUser.Role != UserRole.Admin)
        {
            return ApiResponseDto<EquipmentDto>.ForbiddenResult(OnlyAdminMovesStationMessage);
        }

        var placementError = await ValidatePlacementAsync(dto.StationId, dto.RescueUnitId);
        if (placementError != null)
        {
            return placementError;
        }

        var before = Snapshot(equipment);
        equipment.Name = dto.Name.Trim();
        equipment.Category = dto.Category;
        equipment.Quantity = dto.Quantity;
        equipment.StationId = dto.StationId;
        equipment.RescueUnitId = dto.RescueUnitId;
        equipment.Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();

        await _equipmentRepository.UpdateAsync(equipment);
        await _auditService.LogAsync(SystemAuditAction.Updated, AuditEntityTypes.Equipment, equipment.Id, equipment.Name,
            $"Cập nhật thiết bị \"{equipment.Name}\"", before, Snapshot(equipment), cancellationToken);

        return await ReloadAsync(equipment, "Cập nhật thiết bị thành công.", cancellationToken);
    }

    public async Task<ApiResponseDto<EquipmentDto>> UpdateStatusAsync(Guid id, UpdateEquipmentStatusDto dto, CancellationToken cancellationToken = default)
    {
        var equipment = await _equipmentRepository.GetWithDetailsAsync(id, cancellationToken);
        if (equipment == null)
        {
            return ApiResponseDto<EquipmentDto>.NotFoundResult(NotFoundMessage);
        }

        var accessError = CheckStationAccess(equipment.StationId);
        if (accessError != null)
        {
            return ApiResponseDto<EquipmentDto>.ForbiddenResult(accessError);
        }

        var note = dto.Note?.Trim();
        if (dto.Status == EquipmentStatus.Maintenance && (note == null || note.Length < MinReasonLength))
        {
            return ApiResponseDto<EquipmentDto>.FailureResult(MaintenanceReasonMessage);
        }
        if (dto.Status == equipment.Status)
        {
            return ApiResponseDto<EquipmentDto>.SuccessResult(Map(equipment), "Trạng thái thiết bị không thay đổi.");
        }

        var oldStatus = equipment.Status;
        equipment.Status = dto.Status;
        equipment.StatusNote = string.IsNullOrEmpty(note) ? null : note;
        equipment.StatusChangedAt = DateTime.UtcNow;

        await _equipmentRepository.UpdateAsync(equipment);
        await _auditService.LogAsync(SystemAuditAction.StatusChanged, AuditEntityTypes.Equipment, equipment.Id, equipment.Name,
            $"Đổi trạng thái thiết bị \"{equipment.Name}\": {oldStatus} → {equipment.Status}" + (equipment.StatusNote != null ? $" ({equipment.StatusNote})" : string.Empty),
            new { Status = oldStatus.ToString() }, new { Status = equipment.Status.ToString(), equipment.StatusNote }, cancellationToken);

        return ApiResponseDto<EquipmentDto>.SuccessResult(Map(equipment), "Cập nhật trạng thái thiết bị thành công.");
    }

    public async Task<ApiResponseDto<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var equipment = await _equipmentRepository.GetWithDetailsAsync(id, cancellationToken);
        if (equipment == null)
        {
            return ApiResponseDto<bool>.NotFoundResult(NotFoundMessage);
        }

        var accessError = CheckStationAccess(equipment.StationId);
        if (accessError != null)
        {
            return ApiResponseDto<bool>.ForbiddenResult(accessError);
        }

        await _equipmentRepository.DeleteAsync(equipment);
        await _auditService.LogAsync(SystemAuditAction.Deleted, AuditEntityTypes.Equipment, equipment.Id, equipment.Name,
            $"Xóa thiết bị \"{equipment.Name}\"", Snapshot(equipment), cancellationToken: cancellationToken);
        return ApiResponseDto<bool>.SuccessResult(true, "Xóa thiết bị thành công.");
    }

    /// <summary>
    /// Operator và RescueStaff gắn với trạm mình; Admin (và tác vụ nền) không giới hạn
    /// </summary>
    private bool IsStationBound() => _currentUser.Role is UserRole.Operator or UserRole.RescueStaff;

    private string? CheckStationAccess(Guid? stationId)
    {
        if (!IsStationBound())
        {
            return null;
        }
        if (!_currentUser.StationId.HasValue)
        {
            return OperatorScope.NoStationMessage;
        }
        return stationId == _currentUser.StationId ? null : OtherStationMessage;
    }

    /// <summary>
    /// Trạm phải tồn tại; xe (nếu chọn) phải tồn tại và thuộc đúng trạm sở hữu
    /// </summary>
    private async Task<ApiResponseDto<EquipmentDto>?> ValidatePlacementAsync(Guid stationId, Guid? rescueUnitId)
    {
        if (!await _stationRepository.ExistsAsync(s => s.Id == stationId))
        {
            return ApiResponseDto<EquipmentDto>.NotFoundResult(StationNotFoundMessage);
        }
        if (!rescueUnitId.HasValue)
        {
            return null;
        }

        var unit = await _rescueUnitRepository.GetByIdAsync(rescueUnitId.Value);
        if (unit == null || unit.IsDeleted)
        {
            return ApiResponseDto<EquipmentDto>.NotFoundResult(UnitNotFoundMessage);
        }
        return unit.StationId == stationId ? null : ApiResponseDto<EquipmentDto>.FailureResult(UnitOtherStationMessage);
    }

    private async Task<ApiResponseDto<EquipmentDto>> ReloadAsync(Equipment equipment, string message, CancellationToken cancellationToken)
    {
        var fresh = await _equipmentRepository.GetWithDetailsAsync(equipment.Id, cancellationToken) ?? equipment;
        return ApiResponseDto<EquipmentDto>.SuccessResult(Map(fresh), message);
    }

    private static object Snapshot(Equipment e) => new
    {
        e.Name,
        Category = e.Category.ToString(),
        e.Quantity,
        e.StationId,
        e.RescueUnitId,
        Status = e.Status.ToString(),
        e.Notes
    };

    internal static EquipmentDto Map(Equipment e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Category = e.Category,
        Status = e.Status,
        Quantity = e.Quantity,
        StationId = e.StationId,
        StationName = e.Station?.Name,
        RescueUnitId = e.RescueUnitId,
        RescueUnitPlate = e.RescueUnit?.PlateNumber,
        Notes = e.Notes,
        StatusNote = e.StatusNote,
        StatusChangedAt = e.StatusChangedAt,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt
    };
}
