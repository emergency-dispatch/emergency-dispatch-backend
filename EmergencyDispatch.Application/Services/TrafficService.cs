using EmergencyDispatch.Application.DTOs.Audit;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.DTOs.Notification;
using EmergencyDispatch.Application.DTOs.Traffic;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Localization;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Application.Services;

/// <summary>
/// Điểm nghẽn giao thông nội khu: luồng phụ không chia cấp độ.
/// Báo cáo → hiển thị trên bản đồ → (tùy chọn) Operator giao một xe / đội xuống phân luồng → giải tỏa, hoặc tự hết hạn khi chưa có đội.
/// </summary>
public class TrafficService : ITrafficService
{
    public const int MinUnassignReasonLength = 5;

    private readonly ITrafficHotspotRepository _trafficRepository;
    private readonly IUserRepository _userRepository;
    private readonly IRealtimeNotifier _realtimeNotifier;
    private readonly ILogger<TrafficService> _logger;
    private readonly IRescueUnitRepository _rescueUnitRepository;
    private readonly IDispatchAssignmentRepository _assignmentRepository;
    private readonly IStationRepository _stationRepository;
    private readonly ICurrentUserContext _currentUser;
    private readonly ISystemAuditService _auditService;
    private readonly INotificationService _notificationService;

    public TrafficService(
        ITrafficHotspotRepository trafficRepository,
        IUserRepository userRepository,
        IRealtimeNotifier realtimeNotifier,
        ILogger<TrafficService> logger,
        IRescueUnitRepository rescueUnitRepository,
        IDispatchAssignmentRepository assignmentRepository,
        IStationRepository stationRepository,
        ICurrentUserContext currentUser,
        ISystemAuditService auditService,
        INotificationService notificationService)
    {
        _trafficRepository = trafficRepository;
        _userRepository = userRepository;
        _realtimeNotifier = realtimeNotifier;
        _logger = logger;
        _rescueUnitRepository = rescueUnitRepository;
        _assignmentRepository = assignmentRepository;
        _stationRepository = stationRepository;
        _currentUser = currentUser;
        _auditService = auditService;
        _notificationService = notificationService;
    }

    public async Task<ApiResponseDto<TrafficHotspotResponseDto>> ReportHotspotAsync(
        ReportTrafficHotspotDto dto,
        Guid? currentUserId = null,
        CancellationToken cancellationToken = default)
    {
        User? user = null;
        if (currentUserId.HasValue)
        {
            user = await _userRepository.GetByIdAsync(currentUserId.Value);
        }

        var reporterName = dto.ReporterName?.Trim() ?? user?.FullName ?? "Người dân / Công nhân";
        var reporterPhone = dto.ReporterPhone?.Trim() ?? user?.PhoneNumber;
        var ttl = Math.Clamp(dto.TtlMinutes, 10, 180);
        var now = DateTime.UtcNow;

        // Trạm phụ trách = trạm gần nhất (giống cách giao sự cố) → Operator trạm đó giao đội xử lý
        var station = StationAssignment.PickResponsibleStation(
            await _stationRepository.GetAllAsync(s => s.IsActive), dto.Latitude, dto.Longitude);

        var hotspot = new TrafficHotspot
        {
            Title = dto.Title.Trim(),
            Description = dto.Description?.Trim(),
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            LocationAddress = dto.LocationAddress.Trim(),
            CongestionLevel = dto.CongestionLevel,
            MediaUrl = dto.MediaUrl?.Trim(),
            ReportedByUserId = currentUserId,
            ReportedByUser = user,
            ReporterName = reporterName,
            ReporterPhone = reporterPhone,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(ttl),
            IsActive = true,
            Status = TrafficHotspotStatus.Active,
            ResponsibleStationId = station?.Id,
            ResponsibleStation = station
        };

        await _trafficRepository.AddAsync(hotspot);
        _logger.LogInformation("Điểm nghẽn mới '{Title}' tại {Address} (TTL {Ttl} phút, trạm {Station}).",
            hotspot.Title, hotspot.LocationAddress, ttl, station?.Name);

        var responseDto = MapToResponseDto(hotspot);
        await _realtimeNotifier.TrafficHotspotReportedAsync(responseDto, cancellationToken);

        return ApiResponseDto<TrafficHotspotResponseDto>.SuccessResult(
            responseDto,
            "Báo cáo điểm nghẽn giao thông thành công. Thông tin đã được cập nhật lên bản đồ điều phối.");
    }

    public async Task<ApiResponseDto<List<TrafficHotspotResponseDto>>> GetActiveHotspotsAsync(
        CancellationToken cancellationToken = default)
    {
        var list = await _trafficRepository.GetActiveHotspotsAsync(cancellationToken);
        var dtos = list.Select(MapToResponseDto).ToList();

        return ApiResponseDto<List<TrafficHotspotResponseDto>>.SuccessResult(
            dtos,
            LocalizationHelper.Localize("Lấy danh sách {0} điểm nghẽn đang hoạt động thành công.", dtos.Count));
    }

    public async Task<ApiResponseDto<TrafficHotspotResponseDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var hotspot = await _trafficRepository.GetWithDetailsAsync(id, cancellationToken);
        return hotspot == null
            ? ApiResponseDto<TrafficHotspotResponseDto>.NotFoundResult("Không tìm thấy điểm nghẽn.")
            : ApiResponseDto<TrafficHotspotResponseDto>.SuccessResult(MapToResponseDto(hotspot));
    }

    public async Task<ApiResponseDto<TrafficHotspotResponseDto>> AssignUnitAsync(
        Guid id,
        AssignTrafficHotspotDto dto,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var hotspot = await _trafficRepository.GetWithDetailsAsync(id, cancellationToken);
        if (hotspot == null)
        {
            return ApiResponseDto<TrafficHotspotResponseDto>.NotFoundResult("Không tìm thấy điểm nghẽn.");
        }

        var scopeError = _currentUser.CheckIncident(hotspot.ResponsibleStationId);
        if (scopeError != null)
        {
            return ApiResponseDto<TrafficHotspotResponseDto>.ForbiddenResult(scopeError);
        }

        var now = DateTime.UtcNow;
        if (hotspot.Status == TrafficHotspotStatus.Assigned)
        {
            return ApiResponseDto<TrafficHotspotResponseDto>.ConflictResult(
                LocalizationHelper.Localize("Điểm nghẽn đã được giao cho xe {0}. Rút xe hiện tại trước khi giao xe khác.",
                    hotspot.AssignedRescueUnit?.PlateNumber ?? string.Empty));
        }

        if (!hotspot.IsOpenAt(now))
        {
            return ApiResponseDto<TrafficHotspotResponseDto>.FailureResult("Điểm nghẽn đã được giải tỏa hoặc hết hạn, không thể giao đội xử lý.");
        }

        var unit = await _rescueUnitRepository.GetByIdWithDetailsAsync(dto.RescueUnitId);
        if (unit == null || unit.IsDeleted)
        {
            return ApiResponseDto<TrafficHotspotResponseDto>.NotFoundResult("Không tìm thấy phương tiện cứu hộ.");
        }

        // Operator chỉ giao xe của trạm mình (Admin giao được mọi trạm)
        var unitScopeError = _currentUser.CheckStation(unit.StationId);
        if (unitScopeError != null)
        {
            return ApiResponseDto<TrafficHotspotResponseDto>.ForbiddenResult(unitScopeError);
        }

        if (unit.Status != RescueUnitStatus.Available
            || await _assignmentRepository.GetActiveAssignmentByUnitIdAsync(unit.Id) != null
            || await _trafficRepository.IsUnitOnHotspotAsync(unit.Id, cancellationToken))
        {
            return ApiResponseDto<TrafficHotspotResponseDto>.ConflictResult(
                LocalizationHelper.Localize("Phương tiện {0} không còn sẵn sàng (trạng thái {1}).", unit.PlateNumber, unit.Status));
        }

        // Giữ xe trước (Available → Dispatched, atomic) để xe không bị gợi ý cho sự cố khác
        if (!await _rescueUnitRepository.TryReserveForDispatchAsync(unit.Id, cancellationToken))
        {
            return ApiResponseDto<TrafficHotspotResponseDto>.ConflictResult(
                LocalizationHelper.Localize("Phương tiện {0} vừa được điều cho nhiệm vụ khác. Vui lòng chọn xe khác.", unit.PlateNumber));
        }

        if (!await _trafficRepository.TryMarkAssignedAsync(hotspot.Id, unit.Id, actorUserId, now, cancellationToken))
        {
            await _rescueUnitRepository.ReleaseReservationAsync(unit.Id, cancellationToken);
            return ApiResponseDto<TrafficHotspotResponseDto>.ConflictResult("Điểm nghẽn vừa được người khác cập nhật. Vui lòng tải lại và thử lại.");
        }

        var previousStatus = unit.Status;
        unit.Status = RescueUnitStatus.Dispatched;
        hotspot.Status = TrafficHotspotStatus.Assigned;
        hotspot.AssignedRescueUnitId = unit.Id;
        hotspot.AssignedRescueUnit = unit;
        hotspot.AssignedByUserId = actorUserId;
        hotspot.AssignedAt = now;
        hotspot.UpdatedAt = now;

        var notes = dto.Notes?.Trim();
        var responseDto = MapToResponseDto(hotspot);
        await _auditService.LogAsync(SystemAuditAction.TrafficTaskAssigned, AuditEntityTypes.TrafficHotspot, hotspot.Id, hotspot.Title,
            $"Giao xe {unit.PlateNumber} giải tỏa điểm nghẽn \"{hotspot.Title}\" ({hotspot.LocationAddress})",
            new { status = TrafficHotspotStatus.Active.ToString() },
            new { status = TrafficHotspotStatus.Assigned.ToString(), rescueUnitId = unit.Id, unit.PlateNumber, notes },
            cancellationToken);

        await PublishTaskAsync("Assigned", unit, notes, responseDto, cancellationToken);
        await PublishUnitStatusAsync(unit, previousStatus, "Nhận nhiệm vụ giải tỏa điểm nghẽn", cancellationToken);
        await _notificationService.NotifyStationStaffAsync(unit.StationId, new NotificationRequest
        {
            Type = NotificationType.TrafficTask,
            Title = "NHIỆM VỤ GIẢI TỎA ÙN TẮC",
            Message = $"Xe {unit.PlateNumber}: phân luồng, giải tỏa \"{hotspot.Title}\" tại {hotspot.LocationAddress}."
                      + (string.IsNullOrEmpty(notes) ? string.Empty : $" Ghi chú: {notes}"),
            Data = new { hotspotId = hotspot.Id, rescueUnitId = unit.Id, action = "Assigned" },
            HighPriority = true
        }, cancellationToken);

        return ApiResponseDto<TrafficHotspotResponseDto>.SuccessResult(responseDto,
            LocalizationHelper.Localize("Đã giao xe {0} xử lý điểm nghẽn.", unit.PlateNumber));
    }

    public async Task<ApiResponseDto<TrafficHotspotResponseDto>> UnassignUnitAsync(
        Guid id,
        UnassignTrafficHotspotDto dto,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var reason = dto.Reason?.Trim() ?? string.Empty;
        if (reason.Length < MinUnassignReasonLength)
        {
            return ApiResponseDto<TrafficHotspotResponseDto>.FailureResult(
                LocalizationHelper.Localize("Vui lòng nhập lý do rút xe (tối thiểu {0} ký tự).", MinUnassignReasonLength));
        }

        var hotspot = await _trafficRepository.GetWithDetailsAsync(id, cancellationToken);
        if (hotspot == null)
        {
            return ApiResponseDto<TrafficHotspotResponseDto>.NotFoundResult("Không tìm thấy điểm nghẽn.");
        }

        var scopeError = _currentUser.CheckIncident(hotspot.ResponsibleStationId);
        if (scopeError != null)
        {
            return ApiResponseDto<TrafficHotspotResponseDto>.ForbiddenResult(scopeError);
        }

        if (hotspot.Status != TrafficHotspotStatus.Assigned || hotspot.AssignedRescueUnitId == null)
        {
            return ApiResponseDto<TrafficHotspotResponseDto>.FailureResult("Điểm nghẽn chưa được giao xe nào.");
        }

        var now = DateTime.UtcNow;
        var unit = hotspot.AssignedRescueUnit ?? await _rescueUnitRepository.GetByIdWithDetailsAsync(hotspot.AssignedRescueUnitId.Value);

        hotspot.Status = TrafficHotspotStatus.Active;
        hotspot.AssignedRescueUnitId = null;
        hotspot.AssignedRescueUnit = null;
        hotspot.AssignedByUserId = null;
        hotspot.AssignedAt = null;
        hotspot.UpdatedAt = now;
        await _trafficRepository.UpdateAsync(hotspot);

        if (unit != null)
        {
            await _rescueUnitRepository.ReleaseReservationAsync(unit.Id, cancellationToken);
            var previous = unit.Status;
            unit.Status = RescueUnitStatus.Available;
            await PublishUnitStatusAsync(unit, previous, "Rút khỏi nhiệm vụ giải tỏa điểm nghẽn", cancellationToken);
        }

        var responseDto = MapToResponseDto(hotspot);
        await _auditService.LogAsync(SystemAuditAction.TrafficTaskUnassigned, AuditEntityTypes.TrafficHotspot, hotspot.Id, hotspot.Title,
            $"Rút xe {unit?.PlateNumber} khỏi điểm nghẽn \"{hotspot.Title}\". Lý do: {reason}",
            new { status = TrafficHotspotStatus.Assigned.ToString(), rescueUnitId = unit?.Id },
            new { status = TrafficHotspotStatus.Active.ToString(), reason },
            cancellationToken);
        await _realtimeNotifier.TrafficHotspotReportedAsync(responseDto, cancellationToken);
        if (unit != null)
        {
            await PublishTaskAsync("Unassigned", unit, reason, responseDto, cancellationToken);
            await _notificationService.NotifyStationStaffAsync(unit.StationId, new NotificationRequest
            {
                Type = NotificationType.TrafficTask,
                Title = "RÚT LỆNH GIẢI TỎA ÙN TẮC",
                Message = $"Xe {unit.PlateNumber} được rút khỏi điểm nghẽn \"{hotspot.Title}\". Lý do: {reason}",
                Data = new { hotspotId = hotspot.Id, rescueUnitId = unit.Id, action = "Unassigned" },
                HighPriority = true
            }, cancellationToken);
        }

        return ApiResponseDto<TrafficHotspotResponseDto>.SuccessResult(responseDto, "Đã rút xe khỏi điểm nghẽn. Xe trở về trạng thái sẵn sàng.");
    }

    public async Task<ApiResponseDto<TrafficHotspotResponseDto>> ResolveHotspotAsync(
        Guid id,
        Guid resolverUserId,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        var hotspot = await _trafficRepository.GetWithDetailsAsync(id, cancellationToken);
        var now = DateTime.UtcNow;
        if (hotspot == null || !hotspot.IsOpenAt(now))
        {
            return ApiResponseDto<TrafficHotspotResponseDto>.FailureResult("Không tìm thấy điểm nghẽn hoặc điểm nghẽn đã hết hiệu lực.");
        }

        var resolver = await _userRepository.GetByIdAsync(resolverUserId);
        var isCommandCenter = resolver?.Role is UserRole.Operator or UserRole.Admin;
        var unit = hotspot.AssignedRescueUnit
                   ?? (hotspot.AssignedRescueUnitId.HasValue ? await _rescueUnitRepository.GetByIdWithDetailsAsync(hotspot.AssignedRescueUnitId.Value) : null);

        if (isCommandCenter)
        {
            var scopeError = _currentUser.CheckIncident(hotspot.ResponsibleStationId);
            if (scopeError != null)
            {
                return ApiResponseDto<TrafficHotspotResponseDto>.ForbiddenResult(scopeError);
            }
        }
        else if (hotspot.Status == TrafficHotspotStatus.Assigned
                 && (unit == null || resolver?.StationId == null || resolver.StationId != unit.StationId))
        {
            // Điểm nghẽn đã có đội: chỉ nhân viên trạm của xe được giao mới xác nhận giải tỏa
            return ApiResponseDto<TrafficHotspotResponseDto>.ForbiddenResult("Chỉ đội được giao hoặc trung tâm điều phối mới được xác nhận giải tỏa điểm nghẽn này.");
        }

        var wasAssigned = hotspot.Status == TrafficHotspotStatus.Assigned;
        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmedNote?.Length > 500) trimmedNote = trimmedNote[..500];

        hotspot.Status = TrafficHotspotStatus.Resolved;
        hotspot.IsActive = false;
        hotspot.ResolvedAt = now;
        hotspot.ResolvedByUserId = resolverUserId;
        hotspot.ResolutionNote = trimmedNote;
        hotspot.UpdatedAt = now;
        await _trafficRepository.UpdateAsync(hotspot);

        if (wasAssigned && unit != null)
        {
            await _rescueUnitRepository.ReleaseReservationAsync(unit.Id, cancellationToken);
            var previous = unit.Status;
            unit.Status = RescueUnitStatus.Available;
            await PublishUnitStatusAsync(unit, previous, "Hoàn tất giải tỏa điểm nghẽn", cancellationToken);
        }

        var responseDto = MapToResponseDto(hotspot);
        await _auditService.LogAsync(SystemAuditAction.TrafficResolved, AuditEntityTypes.TrafficHotspot, hotspot.Id, hotspot.Title,
            $"Giải tỏa điểm nghẽn \"{hotspot.Title}\"" + (wasAssigned ? $" (xe {unit?.PlateNumber})" : string.Empty)
            + (trimmedNote != null ? $". Ghi chú: {trimmedNote}" : string.Empty),
            new { status = wasAssigned ? TrafficHotspotStatus.Assigned.ToString() : TrafficHotspotStatus.Active.ToString() },
            new { status = TrafficHotspotStatus.Resolved.ToString(), note = trimmedNote },
            cancellationToken);
        await _realtimeNotifier.TrafficHotspotReportedAsync(responseDto, cancellationToken);
        if (wasAssigned && unit != null)
        {
            await PublishTaskAsync("Resolved", unit, trimmedNote, responseDto, cancellationToken);
        }

        return ApiResponseDto<TrafficHotspotResponseDto>.SuccessResult(
            responseDto,
            "Đã cập nhật giải tỏa điểm nghẽn thành công.");
    }

    public async Task<int> CleanupExpiredHotspotsAsync(CancellationToken cancellationToken = default)
    {
        var count = await _trafficRepository.DeactivateExpiredHotspotsAsync(cancellationToken);
        if (count > 0)
        {
            _logger.LogInformation("Đã tự động chuyển {Count} điểm nghẽn quá hạn TTL sang Expired.", count);
        }

        return count;
    }

    private async Task PublishTaskAsync(string action, RescueUnit unit, string? notes, TrafficHotspotResponseDto hotspot, CancellationToken cancellationToken)
    {
        await _realtimeNotifier.TrafficHotspotReportedAsync(hotspot, cancellationToken);
        await _realtimeNotifier.TrafficTaskChangedAsync(new TrafficTaskEventDto
        {
            Action = action,
            RescueUnitId = unit.Id,
            PlateNumber = unit.PlateNumber,
            UnitStationId = unit.StationId,
            Notes = notes,
            Hotspot = hotspot,
            ChangedAt = DateTime.UtcNow
        }, cancellationToken);
    }

    private Task PublishUnitStatusAsync(RescueUnit unit, RescueUnitStatus previous, string reason, CancellationToken cancellationToken)
        => _realtimeNotifier.RescueUnitStatusChangedAsync(new RescueUnitStatusEventDto
        {
            RescueUnitId = unit.Id,
            PlateNumber = unit.PlateNumber,
            StationId = unit.StationId,
            UnitType = unit.UnitType,
            Status = unit.Status,
            PreviousStatus = previous,
            Latitude = unit.CurrentLat,
            Longitude = unit.CurrentLng,
            Reason = reason,
            ChangedAt = DateTime.UtcNow
        }, cancellationToken);

    private static TrafficHotspotResponseDto MapToResponseDto(TrafficHotspot hotspot)
    {
        return new TrafficHotspotResponseDto
        {
            Id = hotspot.Id,
            Title = hotspot.Title,
            Description = hotspot.Description,
            Latitude = hotspot.Latitude,
            Longitude = hotspot.Longitude,
            LocationAddress = hotspot.LocationAddress,
            CongestionLevel = hotspot.CongestionLevel,
            MediaUrl = hotspot.MediaUrl,
            ReportedByUserId = hotspot.ReportedByUserId,
            ReporterName = hotspot.ReporterName ?? hotspot.ReportedByUser?.FullName,
            CreatedAt = hotspot.CreatedAt,
            ExpiresAt = hotspot.ExpiresAt,
            IsActive = hotspot.IsOpenAt(DateTime.UtcNow),
            Status = hotspot.Status,
            ResponsibleStationId = hotspot.ResponsibleStationId,
            ResponsibleStationName = hotspot.ResponsibleStation?.Name,
            AssignedRescueUnitId = hotspot.AssignedRescueUnitId,
            AssignedUnitPlate = hotspot.AssignedRescueUnit?.PlateNumber,
            AssignedUnitStationId = hotspot.AssignedRescueUnit?.StationId,
            AssignedAt = hotspot.AssignedAt,
            ResolvedAt = hotspot.ResolvedAt,
            ResolutionNote = hotspot.ResolutionNote,
            SourceIncidentId = hotspot.SourceIncidentId
        };
    }
}
