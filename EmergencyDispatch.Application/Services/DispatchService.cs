using System.Text.Json;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Dispatch;
using EmergencyDispatch.Application.DTOs.Notification;
using EmergencyDispatch.Application.DTOs.RescueUnit;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Application.Services;

public class DispatchService : IDispatchService
{
    private const int MaxUnitsLevel3 = 5;
    private const int MaxUnitsDefault = 1;

    // Số xe gần nhất (chim bay) được tra thời gian đường bộ thật qua OSRM /table
    private const int MaxRoadLookups = 25;

    // Thời gian chuẩn bị xuất xe cộng vào ETA
    private const double DispatchPrepMinutes = 2.0;

    private readonly IDispatchAssignmentRepository _assignmentRepository;
    private readonly IRescueUnitRepository _rescueUnitRepository;
    private readonly IIncidentRepository _incidentRepository;
    private readonly IUserRepository _userRepository;
    private readonly IRealtimeNotifier _realtimeNotifier;
    private readonly ISystemSettingsService _settingsService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<DispatchService> _logger;
    private readonly IRoutingService _routingService;
    private readonly ICurrentUserContext _currentUser;
    private readonly IEscalationRepository _escalationRepository;

    public DispatchService(
        IDispatchAssignmentRepository assignmentRepository,
        IRescueUnitRepository rescueUnitRepository,
        IIncidentRepository incidentRepository,
        IUserRepository userRepository,
        IRealtimeNotifier realtimeNotifier,
        ISystemSettingsService settingsService,
        INotificationService notificationService,
        ILogger<DispatchService> logger,
        IRoutingService routingService,
        ICurrentUserContext currentUser,
        IEscalationRepository escalationRepository)
    {
        _escalationRepository = escalationRepository;
        _routingService = routingService;
        _currentUser = currentUser;
        _assignmentRepository = assignmentRepository;
        _rescueUnitRepository = rescueUnitRepository;
        _incidentRepository = incidentRepository;
        _userRepository = userRepository;
        _realtimeNotifier = realtimeNotifier;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<ApiResponseDto<List<RecommendedUnitResponseDto>>> GetRecommendationsAsync(
        Guid incidentId,
        DispatchAlgorithm algorithm = DispatchAlgorithm.NearestAvailable,
        RescueUnitType? unitType = null,
        double? maxDistanceKm = null,
        CancellationToken cancellationToken = default)
    {
        var incident = await _incidentRepository.GetIncidentWithDetailsAsync(incidentId, cancellationToken);
        if (incident == null)
        {
            return ApiResponseDto<List<RecommendedUnitResponseDto>>.FailureResult("Không tìm thấy sự cố cần điều phối.");
        }

        var scopeError = _currentUser.CheckIncident(incident.ResponsibleStationId);
        if (scopeError != null)
        {
            return ApiResponseDto<List<RecommendedUnitResponseDto>>.ForbiddenResult(scopeError);
        }

        var dispatchSettings = await _settingsService.GetDispatchSettingsAsync(cancellationToken);
        var escalationSettings = await _settingsService.GetEscalationSettingsAsync(cancellationToken);

        // BR-13: khớp loại xe theo nhánh sự cố (hoặc theo loại Operator chỉ định)
        var matchingTypes = unitType.HasValue
            ? new HashSet<RescueUnitType> { unitType.Value }
            : DispatchScoring.MatchingUnitTypes(
                incident.EventType != IncidentEventType.None
                    ? incident.EventType
                    : incident.AiClassification?.EventType ?? IncidentEventType.None,
                incident.AiClassification?.HazardTags ?? new List<string>(),
                $"{incident.Title} {incident.Description}",
                incident.Severity);

        // BR-12: nấc bán kính tự mở rộng; Operator chỉ định bán kính thì quét đúng một nấc
        var radii = maxDistanceKm.HasValue
            ? new List<double> { maxDistanceKm.Value }
            : DispatchScoring.BuildSearchRadii(
                dispatchSettings.RadiusStepsKm,
                incident.Status == IncidentStatus.Escalated,
                escalationSettings.RadiusMultiplier,
                escalationSettings.MaxRadiusKm);

        var allUnits = (await _rescueUnitRepository.GetAllWithStationAsync()).Where(u => !u.IsDeleted).ToList();
        var activeAssignments = await _assignmentRepository.GetAllActiveAsync();

        // LoadRatio(S): tỷ lệ xe đang bận của trạm; ActiveMissions(S): số sự cố trạm đang xử lý
        var unitById = allUnits.ToDictionary(u => u.Id);
        var stationStats = allUnits
            .GroupBy(u => u.StationId)
            .ToDictionary(g => g.Key, g =>
            {
                var operational = g.Count(u => u.Status != RescueUnitStatus.Maintenance && u.Status != RescueUnitStatus.OutOfService);
                var busy = g.Count(u => u.Status is RescueUnitStatus.Dispatched or RescueUnitStatus.OnScene
                                         or RescueUnitStatus.Returning or RescueUnitStatus.CoolDown);
                var missions = activeAssignments
                    .Where(a => unitById.TryGetValue(a.RescueUnitId, out var au) && au.StationId == g.Key)
                    .Select(a => a.IncidentId)
                    .Distinct()
                    .Count();
                return (Load: operational > 0 ? busy / (double)operational : 0.0, Missions: missions);
            });
        var tasksByUnit = activeAssignments.GroupBy(a => a.RescueUnitId).ToDictionary(g => g.Key, g => g.Count());

        // BR-DSP-01 / BR-20: chỉ xe Available, đúng chủng loại, không có nhiệm vụ đang chạy mới được gợi ý
        var eligible = allUnits
            .Where(u => u.Status == RescueUnitStatus.Available && !tasksByUnit.ContainsKey(u.Id))
            .Where(u => matchingTypes == null || matchingTypes.Contains(u.UnitType))
            .Select(u => (Unit: u, DistanceKm: CalculateDistanceKm(incident.Latitude, incident.Longitude, u.CurrentLat, u.CurrentLng)))
            .ToList();

        // Quãng đường / thời gian theo đường bộ thật cho các xe gần nhất (một lần gọi OSRM /table)
        var roadLegs = new Dictionary<Guid, RoadLeg>();
        var lookup = eligible.Where(c => c.DistanceKm <= radii[^1]).OrderBy(c => c.DistanceKm).Take(MaxRoadLookups).ToList();
        var legs = await _routingService.GetLegsToAsync(
            lookup.Select(c => (c.Unit.CurrentLat, c.Unit.CurrentLng)).ToList(), incident.Latitude, incident.Longitude, cancellationToken);
        if (legs != null)
        {
            for (var i = 0; i < lookup.Count && i < legs.Count; i++)
            {
                if (legs[i] != null) roadLegs[lookup[i].Unit.Id] = legs[i]!;
            }
        }

        var firstRadius = radii[0];
        foreach (var radiusKm in radii)
        {
            var inRadius = eligible.Where(c => c.DistanceKm <= radiusKm).ToList();
            if (inRadius.Count == 0) continue;

            var candidates = inRadius.Select(c =>
            {
                var stats = stationStats.TryGetValue(c.Unit.StationId, out var st) ? st : (Load: 0.0, Missions: 0);
                var hasRoad = roadLegs.TryGetValue(c.Unit.Id, out var leg);
                var travelKm = hasRoad ? leg!.DistanceKm : c.DistanceKm;
                var cost = DispatchScoring.LoadAwareCost(travelKm, radiusKm, stats.Load, stats.Missions, dispatchSettings.Weights);
                var eta = hasRoad ? Math.Round(leg!.DurationMinutes + DispatchPrepMinutes, 1) : EstimateEtaMinutes(c.DistanceKm);
                var distanceText = hasRoad ? $"{travelKm:F2} km đường bộ" : $"~{travelKm:F2} km (chim bay)";
                var reason = algorithm == DispatchAlgorithm.NearestAvailable
                    ? $"Nearest-Available: {distanceText} (~{eta:F0} phút)."
                    : $"Load-Aware: {distanceText}, tải trạm {stats.Load:P0}, trạm đang xử lý {stats.Missions} sự cố. Cost = {cost:F3}.";

                return new RecommendedUnitResponseDto
                {
                    RescueUnitId = c.Unit.Id,
                    PlateNumber = c.Unit.PlateNumber,
                    UnitType = c.Unit.UnitType,
                    Status = c.Unit.Status,
                    StationId = c.Unit.StationId,
                    StationName = c.Unit.Station?.Name ?? "Trạm chưa xác định",
                    CurrentLat = c.Unit.CurrentLat,
                    CurrentLng = c.Unit.CurrentLng,
                    DistanceKm = Math.Round(c.DistanceKm, 3),
                    EstimatedEtaMinutes = eta,
                    ActiveTasksCount = 0,
                    StationLoad = Math.Round(stats.Load, 3),
                    StationActiveMissions = stats.Missions,
                    Cost = Math.Round(cost, 4),
                    Score = Math.Round((1 - cost) * 100, 2),
                    SearchRadiusKm = radiusKm,
                    RadiusExpanded = radiusKm > firstRadius,
                    AlgorithmUsed = algorithm,
                    RecommendationReason = reason,
                    RoadDistanceKm = hasRoad ? leg!.DistanceKm : null,
                    RoadDurationMinutes = hasRoad ? leg!.DurationMinutes : null,
                    IsApproximate = !hasRoad,
                    IsSupportUnit = incident.ResponsibleStationId.HasValue && c.Unit.StationId != incident.ResponsibleStationId
                };
            });

            var ranked = algorithm == DispatchAlgorithm.NearestAvailable
                ? candidates.OrderBy(c => c.RoadDistanceKm ?? c.DistanceKm).ThenBy(c => c.Cost).ToList()
                : candidates.OrderBy(c => c.Cost).ThenBy(c => c.DistanceKm).ToList();

            return ApiResponseDto<List<RecommendedUnitResponseDto>>.SuccessResult(
                ranked,
                $"Đề xuất theo '{algorithm}' trong bán kính {radiusKm} km: {ranked.Count} phương tiện phù hợp.");
        }

        return ApiResponseDto<List<RecommendedUnitResponseDto>>.SuccessResult(
            new List<RecommendedUnitResponseDto>(),
            $"Không có phương tiện phù hợp đang sẵn sàng trong bán kính {radii[^1]} km.");
    }

    public async Task<ApiResponseDto<List<RecommendedUnitResponseDto>>> GetAvailableUnitsNearbyAsync(
        Guid incidentId,
        double? maxDistanceKm = null,
        CancellationToken cancellationToken = default)
    {
        return await GetRecommendationsAsync(
            incidentId,
            DispatchAlgorithm.NearestAvailable,
            null,
            maxDistanceKm,
            cancellationToken);
    }

    public async Task<ApiResponseDto<List<DispatchAssignmentResponseDto>>> GetActiveAssignmentsAsync(CancellationToken cancellationToken = default)
    {
        if (_currentUser.IsStationScoped() && !_currentUser.StationId.HasValue)
        {
            return ApiResponseDto<List<DispatchAssignmentResponseDto>>.ForbiddenResult(OperatorScope.NoStationMessage);
        }

        var items = await _assignmentRepository.GetActiveWithDetailsAsync(_currentUser.FilterStationId(), cancellationToken);
        var dtos = items.Select(a => MapToAssignmentDto(a, a.Incident, a.RescueUnit)).ToList();
        return ApiResponseDto<List<DispatchAssignmentResponseDto>>.SuccessResult(dtos, $"Có {dtos.Count} lượt điều phối đang thực hiện.");
    }

    /// <summary>
    /// FR-SYS-02: sự cố đã leo thang mà có đơn vị tiếp nhận → hạ thang (ghi audit, đóng lượt leo thang). Chưa SaveChanges.
    /// </summary>
    private async Task DeEscalateAsync(Incident incident, Guid actorId, string reason, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var resolved = await _escalationRepository.ResolveOpenLogsAsync(incident.Id, now, cancellationToken);
        await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
        {
            IncidentId = incident.Id,
            ActorId = actorId,
            ActorRole = _currentUser.Role?.ToString(),
            Action = AuditAction.DeEscalated,
            Description = $"Hạ thang: {reason}. Đóng {resolved} lượt cảnh báo leo thang.",
            OldStateJson = JsonSerializer.Serialize(new { status = IncidentStatus.Escalated.ToString() }),
            NewStateJson = JsonSerializer.Serialize(new { status = incident.Status.ToString() }),
            Timestamp = now
        }, cancellationToken);
    }

    public async Task<ApiResponseDto<List<DispatchAssignmentResponseDto>>> AssignUnitAsync(
        AssignDispatchDto dto,
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        var unitIds = dto.GetUnitIds();
        if (unitIds.Count == 0)
        {
            return ApiResponseDto<List<DispatchAssignmentResponseDto>>.FailureResult("Cần chọn ít nhất một phương tiện cứu hộ.");
        }

        var incident = await _incidentRepository.GetIncidentWithDetailsAsync(dto.IncidentId, cancellationToken);
        if (incident == null)
        {
            return ApiResponseDto<List<DispatchAssignmentResponseDto>>.NotFoundResult("Không tìm thấy sự cố cần điều phối.");
        }

        var scopeError = _currentUser.CheckIncident(incident.ResponsibleStationId);
        if (scopeError != null)
        {
            return ApiResponseDto<List<DispatchAssignmentResponseDto>>.ForbiddenResult(scopeError);
        }

        if (incident.Status is IncidentStatus.Completed or IncidentStatus.Cancelled)
        {
            return ApiResponseDto<List<DispatchAssignmentResponseDto>>.FailureResult($"Không thể điều phối cho sự cố đã ở trạng thái '{incident.Status}'.");
        }

        // BR-01 / FR-OP-04: mọi sự cố (kể cả Level 0) phải được Operator xác minh trước khi điều xe
        if (incident.VerifiedAt == null || incident.Severity == SeverityLevel.Unclassified)
        {
            return ApiResponseDto<List<DispatchAssignmentResponseDto>>.FailureResult(
                "Sự cố phải được xác minh và phân loại Level 1–3 trước khi điều phối đội cứu hộ.");
        }

        // BR-15: Operator khác đang giữ khóa xử lý sự cố
        if (incident.IsLockedByOther(operatorId, DateTime.UtcNow))
        {
            return ApiResponseDto<List<DispatchAssignmentResponseDto>>.ConflictResult(
                $"Sự cố đang được xử lý bởi {incident.LockedByOperator?.FullName ?? "điều phối viên khác"}.");
        }

        // BR-14: Level 3 điều tối đa 5 xe; Level 1–2 mặc định 1 xe (vượt giới hạn phải có lý do)
        var activeCount = incident.DispatchAssignments.Count(a => a.IsActive);
        var maxUnits = incident.Severity == SeverityLevel.Level3 ? MaxUnitsLevel3 : MaxUnitsDefault;
        var overrideAllowed = dto.AllowExtraUnits && (dto.Notes?.Trim().Length ?? 0) >= 5 && incident.Severity != SeverityLevel.Level3;
        if (activeCount + unitIds.Count > maxUnits && !overrideAllowed)
        {
            return ApiResponseDto<List<DispatchAssignmentResponseDto>>.FailureResult(
                $"Sự cố {incident.Severity} chỉ được điều tối đa {maxUnits} xe (đang có {activeCount} xe). " +
                (incident.Severity == SeverityLevel.Level3 ? string.Empty : "Đặt allowExtraUnits = true và ghi lý do vào notes nếu cần thêm xe."));
        }

        var activeUnitIds = incident.DispatchAssignments.Where(a => a.IsActive).Select(a => a.RescueUnitId).ToHashSet();
        var units = new List<RescueUnit>();
        foreach (var unitId in unitIds)
        {
            var unit = await _rescueUnitRepository.GetByIdWithDetailsAsync(unitId);
            if (unit == null || unit.IsDeleted)
            {
                return ApiResponseDto<List<DispatchAssignmentResponseDto>>.NotFoundResult($"Không tìm thấy phương tiện cứu hộ {unitId}.");
            }

            if (activeUnitIds.Contains(unit.Id))
            {
                return ApiResponseDto<List<DispatchAssignmentResponseDto>>.FailureResult($"Phương tiện {unit.PlateNumber} đã được phát lệnh cho sự cố này trước đó.");
            }

            units.Add(unit);
        }

        // BR-DSP-01 / MSG32: giữ chỗ từng xe một cách nguyên tử; nếu một xe vừa bị Operator khác giao thì hoàn tác toàn bộ
        var reserved = new List<RescueUnit>();
        foreach (var unit in units)
        {
            if (!await _rescueUnitRepository.TryReserveForDispatchAsync(unit.Id, cancellationToken))
            {
                foreach (var r in reserved)
                {
                    await _rescueUnitRepository.ReleaseReservationAsync(r.Id, cancellationToken);
                }

                return ApiResponseDto<List<DispatchAssignmentResponseDto>>.ConflictResult(
                    $"Phương tiện {unit.PlateNumber} không còn sẵn sàng (trạng thái {unit.Status}) hoặc vừa được điều phối cho sự cố khác. Vui lòng tải lại danh sách gợi ý.");
            }

            reserved.Add(unit);
            unit.Status = RescueUnitStatus.Dispatched;
        }

        var now = DateTime.UtcNow;
        var oldIncidentStatus = incident.Status;
        if (incident.Status != IncidentStatus.InProgress)
        {
            incident.Status = IncidentStatus.Dispatched;
        }
        incident.UpdatedAt = now;

        // Telemetry RQ3: thời gian từ khi tạo sự cố đến lần phát lệnh điều phối đầu tiên
        incident.DispatchTimeMs ??= (long)(now - incident.CreatedAt).TotalMilliseconds;

        var assignLegs = await _routingService.GetLegsToAsync(
            units.Select(u => (u.CurrentLat, u.CurrentLng)).ToList(), incident.Latitude, incident.Longitude, cancellationToken);

        var assignments = new List<DispatchAssignment>();
        for (var index = 0; index < units.Count; index++)
        {
            var unit = units[index];
            var roadLeg = assignLegs != null && index < assignLegs.Count ? assignLegs[index] : null;
            var distanceKm = roadLeg?.DistanceKm ?? CalculateDistanceKm(incident.Latitude, incident.Longitude, unit.CurrentLat, unit.CurrentLng);
            var assignment = new DispatchAssignment
            {
                IncidentId = incident.Id,
                RescueUnitId = unit.Id,
                DispatchedAt = now,
                Status = DispatchAssignmentStatus.Dispatched,
                Notes = dto.Notes?.Trim(),
                AssignedByUserId = operatorId,
                DispatchAlgorithm = dto.Algorithm?.ToString(),
                DistanceKm = Math.Round(distanceKm, 3),
                EstimatedArrivalMinutes = roadLeg != null
                    ? Math.Round(roadLeg.DurationMinutes + DispatchPrepMinutes, 1)
                    : EstimateEtaMinutes(distanceKm)
            };

            assignment.StatusHistories.Add(new AssignmentStatusHistory
            {
                AssignmentId = assignment.Id,
                OldStatus = DispatchAssignmentStatus.Dispatched,
                NewStatus = DispatchAssignmentStatus.Dispatched,
                ChangedByUserId = operatorId,
                Notes = "Phát lệnh điều phối",
                ChangedAt = now
            });

            incident.DispatchAssignments.Add(assignment);
            assignments.Add(assignment);
        }

        var plates = string.Join(", ", units.Select(u => u.PlateNumber));
        if (oldIncidentStatus == IncidentStatus.Escalated)
        {
            await DeEscalateAsync(incident, operatorId, $"đã điều xe {plates}", cancellationToken);
        }
        await _incidentRepository.AddStatusHistoryAsync(new IncidentStatusHistory
        {
            IncidentId = incident.Id,
            OldStatus = oldIncidentStatus,
            NewStatus = incident.Status,
            ChangedByUserId = operatorId,
            ChangedByRole = UserRole.Operator.ToString(),
            Notes = $"Phát lệnh điều phối {units.Count} phương tiện: {plates}."
        }, cancellationToken);

        await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
        {
            IncidentId = incident.Id,
            ActorId = operatorId,
            ActorRole = UserRole.Operator.ToString(),
            Action = activeCount > 0 ? AuditAction.ReDispatched : AuditAction.Dispatched,
            Description = $"Điều phối {units.Count} phương tiện: {plates}",
            NewStateJson = JsonSerializer.Serialize(new
            {
                unitIds = units.Select(u => u.Id),
                plates = units.Select(u => u.PlateNumber),
                algorithm = dto.Algorithm?.ToString(),
                extraUnitsOverride = overrideAllowed && activeCount + unitIds.Count > maxUnits,
                dispatchTimeMs = incident.DispatchTimeMs
            })
        }, cancellationToken);

        await _incidentRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Operator {OpId} đã điều phối {Count} xe ({Plates}) tới sự cố {IncId} (DispatchTime={Ms}ms)",
            operatorId, units.Count, plates, incident.Id, incident.DispatchTimeMs);

        var dtos = assignments
            .Select(a => MapToAssignmentDto(a, incident, units.First(u => u.Id == a.RescueUnitId)))
            .ToList();

        foreach (var assignmentDto in dtos)
        {
            await _realtimeNotifier.JobAssignedAsync(assignmentDto, cancellationToken);
        }
        foreach (var unit in units)
        {
            await PublishUnitStatusAsync(unit, RescueUnitStatus.Available, cancellationToken);
        }
        await _realtimeNotifier.IncidentUpdatedAsync(MapIncidentToSlimDto(incident), cancellationToken);

        // FR-RS-01 / BR-21: còi hú tới đội cứu hộ của từng trạm; FR-CIT-04 (MSG37): báo người dân
        foreach (var stationGroup in units.GroupBy(u => u.StationId))
        {
            await _notificationService.NotifyStationStaffAsync(stationGroup.Key, new NotificationRequest
            {
                Type = NotificationType.JobAssigned,
                Title = $"LỆNH ĐIỀU PHỐI {incident.Severity}",
                Message = $"{incident.Title} — {incident.LocationAddress}. Xe: {string.Join(", ", stationGroup.Select(u => u.PlateNumber))}.",
                IncidentId = incident.Id,
                Data = new { assignmentIds = assignments.Where(a => stationGroup.Any(u => u.Id == a.RescueUnitId)).Select(a => a.Id) },
                HighPriority = true
            }, cancellationToken);
        }

        // Điều xe trạm khác (Load-Aware xuyên trạm): báo Operator trạm có xe
        foreach (var supportGroup in units.Where(u => incident.ResponsibleStationId.HasValue && u.StationId != incident.ResponsibleStationId).GroupBy(u => u.StationId))
        {
            var supportPlates = string.Join(", ", supportGroup.Select(u => u.PlateNumber));
            foreach (var assignmentDto in dtos.Where(d => supportGroup.Any(u => u.Id == d.RescueUnitId)))
            {
                await _realtimeNotifier.SupportDispatchedAsync(supportGroup.Key, assignmentDto, cancellationToken);
            }
            var supportOperators = (await _userRepository.GetActiveByRolesAsync(new[] { UserRole.Operator }))
                .Where(u => u.StationId == supportGroup.Key)
                .Select(u => u.Id)
                .ToList();
            if (supportOperators.Count > 0)
            {
                await _notificationService.NotifyUsersAsync(supportOperators, new NotificationRequest
                {
                    Type = NotificationType.JobAssigned,
                    Title = "XE TRẠM BẠN ĐƯỢC ĐIỀU HỖ TRỢ",
                    Message = $"Xe {supportPlates} được điều tới sự cố {incident.Title} ({incident.ResponsibleStation?.Name ?? "trạm khác"} phụ trách) — {incident.LocationAddress}.",
                    IncidentId = incident.Id,
                    HighPriority = true
                }, cancellationToken);
            }
        }

        if (incident.ReportedByUserId.HasValue)
        {
            await _notificationService.NotifyUsersAsync(new[] { incident.ReportedByUserId.Value }, new NotificationRequest
            {
                Type = NotificationType.StatusUpdate,
                Title = "Đã điều động đội cứu hộ",
                Message = $"Trung tâm đã điều {units.Count} xe ({plates}) tới vị trí của bạn.",
                IncidentId = incident.Id
            }, cancellationToken);
        }

        return ApiResponseDto<List<DispatchAssignmentResponseDto>>.SuccessResult(
            dtos,
            $"Đã phát lệnh điều phối {units.Count} phương tiện: {plates}.");
    }

    public async Task<ApiResponseDto<DispatchAssignmentResponseDto>> RejectAssignmentAsync(
        Guid id,
        RejectAssignmentDto dto,
        Guid rescuerUserId,
        CancellationToken cancellationToken = default)
    {
        if (dto.Reason == RejectionReason.Other && (dto.Notes?.Trim().Length ?? 0) < 5)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult("Vui lòng mô tả lý do từ chối (tối thiểu 5 ký tự).");
        }

        var assignment = await _assignmentRepository.GetByIdWithDetailsAsync(id);
        if (assignment == null)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.NotFoundResult("Không tìm thấy thông tin phân công.");
        }

        var rescuer = await _userRepository.GetByIdAsync(rescuerUserId);
        var isCommandCenter = rescuer?.Role is UserRole.Operator or UserRole.Admin;
        var unit = assignment.RescueUnit ?? await _rescueUnitRepository.GetByIdWithDetailsAsync(assignment.RescueUnitId);
        var incident = assignment.Incident ?? await _incidentRepository.GetByIdAsync(assignment.IncidentId);

        if (!isCommandCenter && (unit == null || rescuer?.StationId != unit.StationId))
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.ForbiddenResult("Bạn chỉ được từ chối nhiệm vụ của xe thuộc trạm mình.");
        }

        if (!assignment.CanTransitionTo(DispatchAssignmentStatus.Rejected))
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult(
                $"Không thể từ chối nhiệm vụ đang ở trạng thái '{assignment.Status}'.");
        }

        var now = DateTime.UtcNow;
        var oldStatus = assignment.Status;
        assignment.Status = DispatchAssignmentStatus.Rejected;
        assignment.RejectionReason = dto.Reason;
        assignment.RejectionNotes = dto.Notes?.Trim();
        assignment.RejectedAt = now;
        assignment.UpdatedAt = now;

        await _assignmentRepository.AddStatusHistoryAsync(new AssignmentStatusHistory
        {
            AssignmentId = assignment.Id,
            OldStatus = oldStatus,
            NewStatus = DispatchAssignmentStatus.Rejected,
            ChangedByUserId = rescuerUserId,
            RejectionReason = dto.Reason,
            Notes = dto.Notes?.Trim(),
            ChangedAt = now
        });

        // FR-RS-02 (EC-R01): xe hỏng → chuyển bảo trì; lý do khác → xe trở lại sẵn sàng
        if (unit != null)
        {
            unit.Status = dto.Reason == RejectionReason.VehicleBreakdown ? RescueUnitStatus.Maintenance : RescueUnitStatus.Available;
            unit.UpdatedAt = now;
            await SaveUnitAsync(unit, cancellationToken);
        }

        var remainingActive = 0;
        if (incident != null)
        {
            var siblings = await _assignmentRepository.GetByIncidentIdAsync(incident.Id);
            remainingActive = siblings.Count(a => a.Id != assignment.Id && a.IsActive);

            // Không còn xe nào đang xử lý → trả sự cố về hàng chờ điều phối để Operator giao xe thay thế
            if (remainingActive == 0 && incident.Status is IncidentStatus.Dispatched or IncidentStatus.InProgress)
            {
                var oldIncidentStatus = incident.Status;
                incident.Status = IncidentStatus.Verified;
                incident.UpdatedAt = now;

                await _incidentRepository.AddStatusHistoryAsync(new IncidentStatusHistory
                {
                    IncidentId = incident.Id,
                    OldStatus = oldIncidentStatus,
                    NewStatus = IncidentStatus.Verified,
                    ChangedByUserId = rescuerUserId,
                    ChangedByRole = rescuer?.Role.ToString(),
                    Notes = $"Xe {unit?.PlateNumber} từ chối nhiệm vụ ({dto.Reason}). Cần điều phối xe thay thế."
                }, cancellationToken);
            }

            await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
            {
                IncidentId = incident.Id,
                ActorId = rescuerUserId,
                ActorRole = rescuer?.Role.ToString() ?? UserRole.RescueStaff.ToString(),
                Action = AuditAction.AssignmentRejected,
                Description = $"Xe {unit?.PlateNumber} từ chối nhiệm vụ: {dto.Reason}",
                NewStateJson = JsonSerializer.Serialize(new
                {
                    assignmentId = assignment.Id,
                    reason = dto.Reason.ToString(),
                    notes = dto.Notes,
                    remainingActiveAssignments = remainingActive
                })
            }, cancellationToken);
        }

        await _assignmentRepository.UpdateAsync(assignment);
        await _incidentRepository.SaveChangesAsync(cancellationToken);

        _logger.LogWarning("Nhiệm vụ {AssignmentId} bị từ chối bởi {User}: {Reason}", assignment.Id, rescuer?.FullName, dto.Reason);

        var assignmentDto = MapToAssignmentDto(assignment, incident, unit);
        await _realtimeNotifier.AssignmentStatusChangedAsync(assignmentDto, cancellationToken);
        if (incident != null)
        {
            await _realtimeNotifier.IncidentUpdatedAsync(MapIncidentToSlimDto(incident), cancellationToken);
        }

        // BR-22: cảnh báo trung tâm để điều xe thay thế ngay
        await _notificationService.NotifyRolesAsync(new[] { UserRole.Operator, UserRole.Admin }, new NotificationRequest
        {
            Type = NotificationType.StatusUpdate,
            Title = "Đội cứu hộ từ chối nhiệm vụ",
            Message = $"Xe {unit?.PlateNumber} từ chối sự cố \"{incident?.Title}\" ({dto.Reason}). " +
                      (remainingActive == 0 ? "Cần điều xe thay thế." : $"Còn {remainingActive} xe đang xử lý."),
            IncidentId = incident?.Id,
            Data = new { assignmentId = assignment.Id, reason = dto.Reason.ToString() },
            HighPriority = remainingActive == 0
        }, cancellationToken);

        return ApiResponseDto<DispatchAssignmentResponseDto>.SuccessResult(
            assignmentDto,
            remainingActive == 0
                ? "Đã từ chối nhiệm vụ. Trung tâm điều phối sẽ giao xe thay thế."
                : "Đã từ chối nhiệm vụ.");
    }

    public async Task<ApiResponseDto<DispatchAssignmentResponseDto>> GetAssignmentByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var assignment = await _assignmentRepository.GetByIdWithDetailsAsync(id);
        if (assignment == null)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult("Không tìm thấy thông tin phân công.");
        }

        return ApiResponseDto<DispatchAssignmentResponseDto>.SuccessResult(MapToAssignmentDto(assignment, assignment.Incident, assignment.RescueUnit));
    }

    public async Task<ApiResponseDto<List<DispatchAssignmentResponseDto>>> GetAssignmentsByIncidentIdAsync(
        Guid incidentId,
        CancellationToken cancellationToken = default)
    {
        var list = await _assignmentRepository.GetByIncidentIdAsync(incidentId);
        var incident = await _incidentRepository.GetByIdAsync(incidentId);

        var dtos = list.Select(a => MapToAssignmentDto(a, incident, a.RescueUnit)).ToList();
        return ApiResponseDto<List<DispatchAssignmentResponseDto>>.SuccessResult(dtos);
    }

    public async Task<ApiResponseDto<List<DispatchAssignmentResponseDto>>> GetMyAssignmentsAsync(
        Guid rescuerUserId,
        CancellationToken cancellationToken = default)
    {
        var rescuer = await _userRepository.GetByIdAsync(rescuerUserId);
        if (rescuer == null)
        {
            return ApiResponseDto<List<DispatchAssignmentResponseDto>>.FailureResult("Không tìm thấy thông tin tài khoản.");
        }

        IReadOnlyList<DispatchAssignment> list;
        if (rescuer.StationId.HasValue)
        {
            // Nếu là tài khoản gắn với một trạm, lấy tất cả nhiệm vụ thuộc các xe trong trạm đó
            var stationUnits = await _rescueUnitRepository.GetByStationIdAsync(rescuer.StationId.Value);
            var unitIds = stationUnits.Select(u => u.Id).ToHashSet();

            var allActive = new List<DispatchAssignment>();
            foreach (var uid in unitIds)
            {
                var assignments = await _assignmentRepository.GetByRescueUnitIdAsync(uid);
                allActive.AddRange(assignments);
            }
            list = allActive.OrderByDescending(a => a.DispatchedAt).ToList();
        }
        else
        {
            list = new List<DispatchAssignment>();
        }

        var dtos = list.Select(a => MapToAssignmentDto(a, a.Incident, a.RescueUnit)).ToList();
        return ApiResponseDto<List<DispatchAssignmentResponseDto>>.SuccessResult(dtos);
    }

    public async Task<ApiResponseDto<DispatchAssignmentResponseDto>> UpdateAssignmentStatusAsync(
        Guid id,
        UpdateAssignmentStatusDto dto,
        Guid rescuerUserId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await _assignmentRepository.GetByIdWithDetailsAsync(id);
        if (assignment == null)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult("Không tìm thấy thông tin phân công.");
        }

        if (!assignment.IsActive)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult($"Nhiệm vụ đã ở trạng thái '{assignment.Status}', không thể thay đổi.");
        }

        if (dto.Status == DispatchAssignmentStatus.Completed)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult("Hoàn tất nhiệm vụ phải gửi báo cáo nghiệm thu qua API resolve.");
        }

        if (dto.Status == DispatchAssignmentStatus.Rejected)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult("Từ chối nhiệm vụ phải dùng API reject kèm lý do (BR-22).");
        }

        var rescuer = await _userRepository.GetByIdAsync(rescuerUserId);
        var actorName = rescuer?.FullName ?? $"Rescuer {rescuerUserId}";
        var isCommandCenter = rescuer?.Role is UserRole.Operator or UserRole.Admin;

        var incident = assignment.Incident ?? await _incidentRepository.GetByIdAsync(assignment.IncidentId);
        var unit = assignment.RescueUnit ?? await _rescueUnitRepository.GetByIdAsync(assignment.RescueUnitId);

        if (!isCommandCenter)
        {
            if (unit == null || rescuer?.StationId != unit.StationId)
            {
                return ApiResponseDto<DispatchAssignmentResponseDto>.ForbiddenResult("Bạn chỉ được cập nhật nhiệm vụ của xe thuộc trạm mình.");
            }

            if (dto.Status == DispatchAssignmentStatus.Cancelled)
            {
                return ApiResponseDto<DispatchAssignmentResponseDto>.ForbiddenResult("Chỉ trung tâm điều phối mới được hủy lệnh điều phối.");
            }
        }

        // BR-24: trạng thái phải đi tuần tự, không nhảy cóc hoặc lùi lại
        if (!assignment.CanTransitionTo(dto.Status))
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult(
                $"Không thể chuyển nhiệm vụ từ '{assignment.Status}' sang '{dto.Status}'. Thứ tự hợp lệ: Dispatched → Accepted → EnRoute → OnScene.");
        }

        var oldStatus = assignment.Status;
        assignment.Status = dto.Status;
        assignment.UpdatedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(dto.Notes))
        {
            assignment.Notes = dto.Notes.Trim();
        }

        switch (dto.Status)
        {
            case DispatchAssignmentStatus.Accepted:
                assignment.AcceptedAt = DateTime.UtcNow;
                // FR-RS-01: đội đã nhận lệnh → sự cố chuyển sang đang xử lý
                if (incident != null && incident.Status is IncidentStatus.Dispatched or IncidentStatus.Escalated)
                {
                    if (incident.Status == IncidentStatus.Escalated)
                    {
                        incident.Status = IncidentStatus.InProgress;
                        await DeEscalateAsync(incident, rescuerUserId, $"xe {unit?.PlateNumber} đã nhận lệnh", cancellationToken);
                    }
                    incident.Status = IncidentStatus.InProgress;
                    incident.UpdatedAt = DateTime.UtcNow;
                    await _incidentRepository.UpdateAsync(incident);
                }
                break;

            case DispatchAssignmentStatus.Cancelled:
                if (unit != null)
                {
                    unit.Status = RescueUnitStatus.Available;
                    unit.UpdatedAt = DateTime.UtcNow;
                    await SaveUnitAsync(unit, cancellationToken);
                }
                break;

            case DispatchAssignmentStatus.EnRoute:
                assignment.EnRouteAt = DateTime.UtcNow;
                if (unit != null)
                {
                    unit.Status = RescueUnitStatus.Dispatched;
                    unit.UpdatedAt = DateTime.UtcNow;
                    await SaveUnitAsync(unit, cancellationToken);
                }
                break;

            case DispatchAssignmentStatus.OnScene:
                assignment.ArrivedAt = DateTime.UtcNow;
                if (unit != null)
                {
                    unit.Status = RescueUnitStatus.OnScene;
                    unit.UpdatedAt = DateTime.UtcNow;
                    await SaveUnitAsync(unit, cancellationToken);
                }
                if (incident != null && incident.Status != IncidentStatus.InProgress)
                {
                    incident.Status = IncidentStatus.InProgress;
                    incident.UpdatedAt = DateTime.UtcNow;
                    await _incidentRepository.UpdateAsync(incident);
                }
                break;
        }

        await _assignmentRepository.AddStatusHistoryAsync(new AssignmentStatusHistory
        {
            AssignmentId = assignment.Id,
            OldStatus = oldStatus,
            NewStatus = dto.Status,
            ChangedByUserId = rescuerUserId,
            Notes = dto.Notes?.Trim(),
            ChangedAt = DateTime.UtcNow
        });

        await _assignmentRepository.UpdateAsync(assignment);

        if (incident != null)
        {
            await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
            {
                IncidentId = incident.Id,
                ActorId = rescuerUserId,
                ActorRole = rescuer?.Role.ToString() ?? UserRole.RescueStaff.ToString(),
                Action = AuditAction.StatusChanged,
                Description = $"Đội cứu hộ {unit?.PlateNumber} cập nhật trạng thái nhiệm vụ: {oldStatus} ➔ {dto.Status}",
                NewStateJson = JsonSerializer.Serialize(new { assignmentId = assignment.Id, status = dto.Status.ToString() })
            }, cancellationToken);
            await _incidentRepository.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("Nhiệm vụ {AssignmentId} được {Actor} chuyển trạng thái sang {NewStatus}",
            id, actorName, dto.Status);

        var assignmentDto = MapToAssignmentDto(assignment, incident, unit);

        // Bắn SignalR
        await _realtimeNotifier.AssignmentStatusChangedAsync(assignmentDto, cancellationToken);
        if (incident != null)
        {
            await _realtimeNotifier.IncidentUpdatedAsync(MapIncidentToSlimDto(incident), cancellationToken);
        }

        // FR-CIT-04 (MSG37, MSG40): báo người dân khi xe xuất phát và khi tới hiện trường
        if (incident?.ReportedByUserId != null && dto.Status is DispatchAssignmentStatus.EnRoute or DispatchAssignmentStatus.OnScene)
        {
            await _notificationService.NotifyUsersAsync(new[] { incident.ReportedByUserId.Value }, new NotificationRequest
            {
                Type = NotificationType.StatusUpdate,
                Title = dto.Status == DispatchAssignmentStatus.EnRoute ? "Xe cứu hộ đang trên đường" : "Đội cứu hộ đã tới hiện trường",
                Message = dto.Status == DispatchAssignmentStatus.EnRoute
                    ? $"Xe {unit?.PlateNumber} đang di chuyển tới vị trí của bạn (dự kiến ~{assignment.EstimatedArrivalMinutes ?? 0:F0} phút)."
                    : $"Xe {unit?.PlateNumber} đã có mặt tại hiện trường. Đội cứu hộ đang triển khai.",
                IncidentId = incident.Id,
                Data = new { assignmentId = assignment.Id, status = dto.Status.ToString() }
            }, cancellationToken);
        }

        return ApiResponseDto<DispatchAssignmentResponseDto>.SuccessResult(
            assignmentDto,
            $"Cập nhật trạng thái nhiệm vụ thành '{dto.Status}' thành công.");
    }

    public async Task<ApiResponseDto<DispatchAssignmentResponseDto>> ResolveAssignmentAsync(
        Guid id,
        ResolveDispatchDto dto,
        Guid rescuerUserId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await _assignmentRepository.GetByIdWithDetailsAsync(id);
        if (assignment == null)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult("Không tìm thấy thông tin phân công.");
        }

        if (!assignment.IsActive)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult($"Nhiệm vụ đã ở trạng thái '{assignment.Status}', không thể nghiệm thu.");
        }

        var incident = assignment.Incident ?? await _incidentRepository.GetByIdAsync(assignment.IncidentId);
        if (incident == null)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult("Không tìm thấy thông tin sự cố.");
        }

        var unit = assignment.RescueUnit ?? await _rescueUnitRepository.GetByIdAsync(assignment.RescueUnitId);
        var rescuer = await _userRepository.GetByIdAsync(rescuerUserId);
        var actorRole = rescuer?.Role.ToString() ?? UserRole.RescueStaff.ToString();
        var isCommandCenter = rescuer?.Role is UserRole.Operator or UserRole.Admin;

        if (!isCommandCenter)
        {
            if (unit == null || rescuer?.StationId != unit.StationId)
            {
                return ApiResponseDto<DispatchAssignmentResponseDto>.ForbiddenResult("Bạn chỉ được nghiệm thu nhiệm vụ của xe thuộc trạm mình.");
            }

            // BR-24: đội cứu hộ chỉ nghiệm thu được sau khi đã có mặt tại hiện trường
            if (!assignment.CanTransitionTo(DispatchAssignmentStatus.Completed))
            {
                return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult(
                    $"Nhiệm vụ đang ở trạng thái '{assignment.Status}'. Chỉ nghiệm thu được sau khi xác nhận OnScene.");
            }
        }

        var now = DateTime.UtcNow;
        var evidenceUrls = (dto.EvidenceMediaUrls ?? new List<string>())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .ToList();
        if (evidenceUrls.Count == 0)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult("Cần ít nhất một ảnh hiện trường sau xử lý (MSG42).");
        }

        // 1. Hoàn tất lượt phân công của xe này
        var oldAssignmentStatus = assignment.Status;
        assignment.Status = DispatchAssignmentStatus.Completed;
        assignment.CompletedAt = now;
        assignment.UpdatedAt = now;

        // FR-RS-03 / BR-25 / BR-26: lưu báo cáo nghiệm thu kèm ảnh bằng chứng và thông tin bàn giao
        var report = new CompletionReport
        {
            AssignmentId = assignment.Id,
            SubmittedByUserId = rescuerUserId,
            ActionsSummary = dto.ResolutionSummary.Trim(),
            VictimsAssistedCount = dto.VictimsAssistedCount ?? 0,
            EquipmentUsed = dto.EquipmentUsed?.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).ToList() ?? new List<string>(),
            HandoverEntity = dto.HandoverEntity?.Trim(),
            HandoverContactName = dto.HandoverContactName?.Trim(),
            HandoverContactPhone = dto.HandoverContactPhone?.Trim(),
            HandoverNotes = dto.HandoverNotes?.Trim(),
            SubmittedAt = now
        };
        foreach (var url in evidenceUrls)
        {
            report.MediaItems.Add(new CompletionReportMedia { MediaUrl = url, MediaType = MediaType.Photo });
        }
        await _assignmentRepository.AddCompletionReportAsync(report);
        assignment.CompletionReport = report;

        await _assignmentRepository.AddStatusHistoryAsync(new AssignmentStatusHistory
        {
            AssignmentId = assignment.Id,
            OldStatus = oldAssignmentStatus,
            NewStatus = DispatchAssignmentStatus.Completed,
            ChangedByUserId = rescuerUserId,
            Notes = "Nộp báo cáo nghiệm thu",
            ChangedAt = now
        });
        await _assignmentRepository.UpdateAsync(assignment);

        // 2. BR-27: xe hồi sức (CoolDown) trước khi quay lại nhận lệnh mới
        if (unit != null)
        {
            var dispatchSettings = await _settingsService.GetDispatchSettingsAsync(cancellationToken);
            unit.Status = RescueUnitStatus.CoolDown;
            unit.CoolDownUntil = now.AddMinutes(dispatchSettings.RecoveryMinutes);
            unit.UpdatedAt = now;
            await SaveUnitAsync(unit, cancellationToken);
        }


        // 3. BR-14: sự cố nhiều xe chỉ đóng khi mọi xe còn hiệu lực đều đã nghiệm thu
        var siblingAssignments = await _assignmentRepository.GetByIncidentIdAsync(incident.Id);
        var remainingActive = siblingAssignments.Count(a => a.Id != assignment.Id && a.IsActive);
        var incidentClosed = remainingActive == 0;
        var oldIncidentStatus = incident.Status;

        if (incidentClosed)
        {
            incident.Close(rescuerUserId, dto.ResolutionSummary.Trim());
            incident.ResolutionTimeMs = (long)(DateTime.UtcNow - incident.CreatedAt).TotalMilliseconds;

            await _incidentRepository.AddStatusHistoryAsync(new IncidentStatusHistory
            {
                IncidentId = incident.Id,
                OldStatus = oldIncidentStatus,
                NewStatus = IncidentStatus.Completed,
                ChangedByUserId = rescuerUserId,
                ChangedByRole = actorRole,
                Notes = $"Nghiệm thu đóng sự cố: {dto.ResolutionSummary.Trim()}"
            }, cancellationToken);
        }
        else if (incident.Status != IncidentStatus.InProgress)
        {
            incident.Status = IncidentStatus.InProgress;
            incident.UpdatedAt = DateTime.UtcNow;
        }

        await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
        {
            IncidentId = incident.Id,
            ActorId = rescuerUserId,
            ActorRole = actorRole,
            Action = incidentClosed ? AuditAction.Completed : AuditAction.StatusChanged,
            Description = incidentClosed
                ? "Nghiệm thu hiện trường và hoàn tất sự cố"
                : $"Xe {unit?.PlateNumber} nghiệm thu xong, còn {remainingActive} xe đang xử lý",
            NewStateJson = JsonSerializer.Serialize(new
            {
                assignmentId = assignment.Id,
                incidentClosed,
                remainingActiveAssignments = remainingActive,
                resolutionTimeMs = incident.ResolutionTimeMs,
                closedBy = incidentClosed ? rescuerUserId : (Guid?)null,
                evidencePhotosCount = evidenceUrls.Count,
                victimsAssisted = report.VictimsAssistedCount
            })
        }, cancellationToken);

        await _incidentRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Nhiệm vụ {AssignmentId} nghiệm thu xong. Sự cố {IncidentId} {State} (T_Response={Ms}ms)",
            assignment.Id, incident.Id, incidentClosed ? "đã đóng" : $"còn {remainingActive} xe", incident.ResolutionTimeMs);

        var assignmentDto = MapToAssignmentDto(assignment, incident, unit);

        // Bắn SignalR
        await _realtimeNotifier.AssignmentStatusChangedAsync(assignmentDto, cancellationToken);
        await _realtimeNotifier.IncidentUpdatedAsync(MapIncidentToSlimDto(incident), cancellationToken);

        // FR-CIT-04: báo người dân khi toàn bộ cứu hộ hoàn tất
        if (incidentClosed && incident.ReportedByUserId.HasValue)
        {
            await _notificationService.NotifyUsersAsync(new[] { incident.ReportedByUserId.Value }, new NotificationRequest
            {
                Type = NotificationType.StatusUpdate,
                Title = "Cứu hộ đã hoàn tất",
                Message = $"Sự cố \"{incident.Title}\" đã được xử lý xong. Cảm ơn bạn đã báo tin.",
                IncidentId = incident.Id
            }, cancellationToken);
        }

        return ApiResponseDto<DispatchAssignmentResponseDto>.SuccessResult(
            assignmentDto,
            incidentClosed
                ? "Nghiệm thu hiện trường thành công. Hồ sơ sự cố đã được đóng và ghi nhận KPI phản hồi."
                : $"Nghiệm thu xe thành công. Sự cố vẫn mở vì còn {remainingActive} xe đang xử lý.");
    }

    public async Task<ApiResponseDto<CitizenTrackingResponseDto>> GetCitizenTrackingAsync(
        Guid incidentId,
        CancellationToken cancellationToken = default)
    {
        var incident = await _incidentRepository.GetIncidentWithDetailsAsync(incidentId, cancellationToken);
        if (incident == null)
        {
            return ApiResponseDto<CitizenTrackingResponseDto>.NotFoundResult("Không tìm thấy thông tin sự cố.");
        }

        var result = new CitizenTrackingResponseDto
        {
            IncidentId = incident.Id,
            IncidentTitle = incident.Title,
            IncidentAddress = incident.LocationAddress,
            IncidentLat = incident.Latitude,
            IncidentLng = incident.Longitude,
            IncidentStatus = incident.Status.ToString(),
            IncidentSeverity = incident.Severity.ToString(),
            DispatchStatus = "PendingDispatch"
        };

        // FR-CIT-03: hiển thị mọi xe đang xử lý; nếu đã xong hết thì hiển thị các xe đã hoàn thành
        var assignments = await _assignmentRepository.GetByIncidentIdAsync(incidentId);
        var shown = assignments.Where(a => a.IsActive).ToList();
        if (shown.Count == 0)
        {
            shown = assignments.Where(a => a.Status == DispatchAssignmentStatus.Completed).ToList();
        }

        if (shown.Count == 0)
        {
            return ApiResponseDto<CitizenTrackingResponseDto>.SuccessResult(result,
                "Sự cố đang được Điều phối viên tiếp nhận và phân công đội cứu hộ.");
        }

        foreach (var assignment in shown.OrderBy(a => a.DispatchedAt))
        {
            var unit = assignment.RescueUnit ?? await _rescueUnitRepository.GetByIdWithDetailsAsync(assignment.RescueUnitId);

            var vehicleLat = (unit != null && unit.CurrentLat != 0) ? unit.CurrentLat : (unit?.Station?.Latitude ?? incident.Latitude);
            var vehicleLng = (unit != null && unit.CurrentLng != 0) ? unit.CurrentLng : (unit?.Station?.Longitude ?? incident.Longitude);
            var arrived = assignment.Status is DispatchAssignmentStatus.OnScene or DispatchAssignmentStatus.Completed;
            var route = arrived
                ? null
                : await _routingService.GetRouteAsync(vehicleLat, vehicleLng, incident.Latitude, incident.Longitude, cancellationToken);

            result.Units.Add(new TrackedUnitDto
            {
                AssignmentId = assignment.Id,
                RescueUnitId = unit?.Id ?? assignment.RescueUnitId,
                PlateNumber = unit?.PlateNumber,
                UnitTypeName = unit?.UnitType.ToString(),
                StationName = unit?.Station?.Name,
                StationPhoneNumber = unit?.Station?.PhoneNumber,
                VehicleLat = vehicleLat,
                VehicleLng = vehicleLng,
                LastLocationUpdateAt = unit?.LastLocationUpdateAt ?? assignment.DispatchedAt,
                RouteTrajectory = route?.Points ?? new List<RoutePointDto>(),
                IsApproximateRoute = route?.IsApproximate ?? false,
                DistanceKm = route?.DistanceKm ?? 0,
                EstimatedArrivalMinutes = route == null ? 0 : (int)Math.Max(1, Math.Round(route.DurationMinutes + DispatchPrepMinutes)),
                DispatchStatus = assignment.Status.ToString(),
                DispatchedAt = assignment.DispatchedAt,
                AcceptedAt = assignment.AcceptedAt,
                EnRouteAt = assignment.EnRouteAt,
                ArrivedAt = assignment.ArrivedAt,
                CompletedAt = assignment.CompletedAt,
                Notes = assignment.Notes
            });
        }

        // Giữ các trường đơn (xe đầu tiên) để tương thích với client cũ
        var primary = result.Units[0];
        result.RescueUnitId = primary.RescueUnitId;
        result.PlateNumber = primary.PlateNumber;
        result.UnitTypeName = primary.UnitTypeName;
        result.StationName = primary.StationName ?? string.Empty;
        result.StationPhoneNumber = primary.StationPhoneNumber;
        result.VehicleLat = primary.VehicleLat;
        result.VehicleLng = primary.VehicleLng;
        result.LastLocationUpdateAt = primary.LastLocationUpdateAt;
        result.RouteTrajectory = primary.RouteTrajectory;
        result.IsApproximateRoute = primary.IsApproximateRoute;
        result.DistanceKm = primary.DistanceKm;
        result.EstimatedArrivalMinutes = primary.EstimatedArrivalMinutes;
        result.DispatchStatus = primary.DispatchStatus;
        result.DispatchedAt = primary.DispatchedAt;
        result.AcceptedAt = primary.AcceptedAt;
        result.ArrivedAt = primary.ArrivedAt;
        result.CompletedAt = primary.CompletedAt;
        result.Notes = primary.Notes;

        return ApiResponseDto<CitizenTrackingResponseDto>.SuccessResult(result,
            $"Lấy thông tin lộ trình di chuyển của {result.Units.Count} xe cứu hộ thành công.");
    }

    /// <summary>
    /// Lưu xe và phát ReceiveRescueUnitStatus để live map / danh sách xe cập nhật ngay
    /// </summary>
    private async Task SaveUnitAsync(RescueUnit unit, CancellationToken cancellationToken)
    {
        await _rescueUnitRepository.UpdateAsync(unit);
        await PublishUnitStatusAsync(unit, null, cancellationToken);
    }

    private Task PublishUnitStatusAsync(RescueUnit unit, RescueUnitStatus? previous, CancellationToken cancellationToken)
        => _realtimeNotifier.RescueUnitStatusChangedAsync(new EmergencyDispatch.Application.DTOs.Incident.RescueUnitStatusEventDto
        {
            RescueUnitId = unit.Id,
            PlateNumber = unit.PlateNumber,
            StationId = unit.StationId,
            Status = unit.Status,
            PreviousStatus = previous,
            CoolDownUntil = unit.CoolDownUntil,
            ChangedAt = DateTime.UtcNow
        }, cancellationToken);

    #region Private Helper Methods

    private static double CalculateDistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        if (lat1 == 0 || lon1 == 0 || lat2 == 0 || lon2 == 0) return 999.0;

        const double R = 6371.0;
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return Math.Round(R * c, 2);
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static double EstimateEtaMinutes(double distanceKm)
    {
        const double avgSpeedKmh = 35.0; // Vận tốc trung bình trong KCNC (đã tính qua các chốt)
        var drivingMinutes = (distanceKm / avgSpeedKmh) * 60.0;
        return Math.Round(drivingMinutes + 2.0, 1); // +2 phút thời gian chuẩn bị xuất xe
    }

    private static DispatchAssignmentResponseDto MapToAssignmentDto(
        DispatchAssignment assignment,
        Incident? incident,
        RescueUnit? unit)
    {
        RescueUnitResponseDto? unitDto = null;
        if (unit != null)
        {
            unitDto = new RescueUnitResponseDto
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
                CreatedAt = unit.CreatedAt,
                UpdatedAt = unit.UpdatedAt
            };
        }

        return new DispatchAssignmentResponseDto
        {
            Id = assignment.Id,
            IncidentId = assignment.IncidentId,
            ResponsibleStationId = incident?.ResponsibleStationId,
            IncidentTitle = incident?.Title ?? string.Empty,
            IncidentAddress = incident?.LocationAddress ?? string.Empty,
            IncidentLat = incident?.Latitude ?? 0,
            IncidentLng = incident?.Longitude ?? 0,
            IncidentSeverity = incident?.Severity ?? SeverityLevel.Unclassified,
            RescueUnitId = assignment.RescueUnitId,
            RescueUnit = unitDto,
            DispatchedAt = assignment.DispatchedAt,
            AcceptedAt = assignment.AcceptedAt,
            ArrivedAt = assignment.ArrivedAt,
            CompletedAt = assignment.CompletedAt,
            EnRouteAt = assignment.EnRouteAt,
            Status = assignment.Status,
            Notes = assignment.Notes,
            DispatchAlgorithm = assignment.DispatchAlgorithm,
            DistanceKm = assignment.DistanceKm,
            EstimatedArrivalMinutes = assignment.EstimatedArrivalMinutes,
            AssignedByUserId = assignment.AssignedByUserId,
            RejectionReason = assignment.RejectionReason,
            RejectionNotes = assignment.RejectionNotes,
            RejectedAt = assignment.RejectedAt,
            CompletionReport = assignment.CompletionReport == null ? null : new CompletionReportDto
            {
                Id = assignment.CompletionReport.Id,
                ActionsSummary = assignment.CompletionReport.ActionsSummary,
                VictimsAssistedCount = assignment.CompletionReport.VictimsAssistedCount,
                EquipmentUsed = assignment.CompletionReport.EquipmentUsed,
                EvidenceMediaUrls = assignment.CompletionReport.MediaItems.Select(m => m.MediaUrl).ToList(),
                HandoverEntity = assignment.CompletionReport.HandoverEntity,
                HandoverContactName = assignment.CompletionReport.HandoverContactName,
                HandoverContactPhone = assignment.CompletionReport.HandoverContactPhone,
                HandoverNotes = assignment.CompletionReport.HandoverNotes,
                SubmittedByUserId = assignment.CompletionReport.SubmittedByUserId,
                SubmittedAt = assignment.CompletionReport.SubmittedAt
            }
        };
    }

    private static EmergencyDispatch.Application.DTOs.Incident.IncidentResponseDto MapIncidentToSlimDto(Incident incident)
    {
        return new EmergencyDispatch.Application.DTOs.Incident.IncidentResponseDto
        {
            Id = incident.Id,
            Title = incident.Title,
            Description = incident.Description,
            Latitude = incident.Latitude,
            Longitude = incident.Longitude,
            LocationAddress = incident.LocationAddress,
            Status = incident.Status,
            Severity = incident.Severity,
            ReportedByUserId = incident.ReportedByUserId,
            ReporterName = incident.ReporterName,
            ReporterPhone = PhoneMasking.Mask(incident.ReporterPhone),
            VerifiedByUserId = incident.VerifiedByUserId,
            VerifiedAt = incident.VerifiedAt,
            OperatorNotes = incident.OperatorNotes,
            CreatedAt = incident.CreatedAt,
            UpdatedAt = incident.UpdatedAt,
            ResponsibleStationId = incident.ResponsibleStationId,
            ResponsibleStationName = incident.ResponsibleStation?.Name,
            IsOutOfServiceArea = incident.IsOutOfServiceArea,
            DistanceOutsideAreaKm = incident.DistanceOutsideAreaKm
        };
    }

    #endregion
}
