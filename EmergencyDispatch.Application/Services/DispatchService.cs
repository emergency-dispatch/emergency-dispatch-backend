using EmergencyDispatch.Application.Localization;
using System.Text.Json;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Dispatch;
using EmergencyDispatch.Application.DTOs.Notification;
using EmergencyDispatch.Application.DTOs.RescueUnit;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Validators;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Exceptions;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
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
    private readonly IIncidentFollowUpService _followUpService;
    private readonly IFieldRequestService _fieldRequestService;
    private readonly IConfiguration _configuration;
    private readonly IStationTeamRepository _teamRepository;
    private readonly ITypeCatalogRepository? _typeCatalog;

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
        IEscalationRepository escalationRepository,
        IIncidentFollowUpService followUpService,
        IFieldRequestService fieldRequestService,
        IConfiguration configuration,
        IStationTeamRepository teamRepository,
        ITypeCatalogRepository? typeCatalog = null)
    {
        _typeCatalog = typeCatalog;
        _configuration = configuration;
        _teamRepository = teamRepository;
        _followUpService = followUpService;
        _fieldRequestService = fieldRequestService;
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
        string? unitType = null,
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

        // BR-13: khớp loại xe theo quy tắc của loại sự cố (hoặc theo loại Operator chỉ định)
        var matchingTypes = !string.IsNullOrWhiteSpace(unitType)
            ? new HashSet<string> { unitType.Trim() }
            : await MatchingUnitTypesAsync(incident, cancellationToken);

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

        // Giao theo tổ: chỉ tổ có nhân viên cứu hộ đang hoạt động và không đang làm nhiệm vụ khác mới được gợi ý
        var teamPool = await LoadTeamPoolAsync(allUnits, activeAssignments, cancellationToken);

        // BR-DSP-01 / BR-20: chỉ xe Available, đúng chủng loại, không có nhiệm vụ đang chạy mới được gợi ý
        // Nearest-Available: khoảng cách từ PostGIS (ST_DWithin trong nấc bán kính lớn nhất + ST_Distance);
        // không có truy vấn không gian (VD test) → Haversine
        var postgisMeters = await _rescueUnitRepository.GetDistancesMetersAsync(incident.Latitude, incident.Longitude, radii[^1] * 1000, cancellationToken);
        var eligible = allUnits
            .Where(u => u.Status == RescueUnitStatus.Available && !tasksByUnit.ContainsKey(u.Id))
            .Where(u => matchingTypes == null || matchingTypes.Contains(u.UnitType))
            .Where(u => teamPool.Problem(u) == TeamProblem.None)
            .Where(u => postgisMeters == null || postgisMeters.ContainsKey(u.Id))
            .Select(u => (Unit: u, DistanceKm: postgisMeters != null
                ? postgisMeters[u.Id] / 1000.0
                : CalculateDistanceKm(incident.Latitude, incident.Longitude, u.CurrentLat, u.CurrentLng)))
            // Mỗi tổ một dòng cho mỗi chức năng: giữ xe gần nhất của tổ thuộc loại đó
            .GroupBy(c => (c.Unit.StationTeamId, c.Unit.UnitType))
            .Select(g => g.OrderBy(c => c.DistanceKm).First())
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

                var team = teamPool.Teams[c.Unit.StationTeamId!.Value];
                return new RecommendedUnitResponseDto
                {
                    StationTeamId = team.TeamId,
                    StationTeamName = team.Name,
                    TeamStaffCount = team.ActiveStaffCount,
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
                LocalizationHelper.Localize("Đề xuất theo '{0}' trong bán kính {1} km: {2} tổ phù hợp.", algorithm, radiusKm, ranked.Count));
        }

        return ApiResponseDto<List<RecommendedUnitResponseDto>>.SuccessResult(
            new List<RecommendedUnitResponseDto>(),
            LocalizationHelper.Localize("Không có tổ phù hợp (có nhân viên, có xe đúng chức năng đang sẵn sàng) trong bán kính {0} km.", radii[^1]));
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
        return ApiResponseDto<List<DispatchAssignmentResponseDto>>.SuccessResult(dtos, LocalizationHelper.Localize("Có {0} lượt điều phối đang thực hiện.", dtos.Count));
    }

    /// <summary>
    /// FR-SYS-02: sự cố đã leo thang mà có đơn vị tiếp nhận → hạ thang (ghi audit, đóng lượt leo thang). Chưa SaveChanges.
    /// </summary>
    private async Task DeEscalateAsync(Incident incident, Guid? actorId, string reason, CancellationToken cancellationToken)
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

    public Task<ApiResponseDto<List<DispatchAssignmentResponseDto>>> AssignUnitAsync(
        AssignDispatchDto dto,
        Guid operatorId,
        CancellationToken cancellationToken = default)
        => AssignCoreAsync(dto, operatorId, UserRole.Operator.ToString(), cancellationToken);

    /// <summary>
    /// Vai trò ghi trong lịch sử / audit khi hệ thống (AI tự điều phối) phát lệnh thay Operator
    /// </summary>
    public const string AutoDispatchActorRole = "AI_AutoDispatch";

    /// <summary>
    /// AI tự điều phối: phát lệnh như Operator nhưng không có người thao tác (AssignedByUserId = null)
    /// </summary>
    public Task<ApiResponseDto<List<DispatchAssignmentResponseDto>>> AutoAssignAsync(
        AssignDispatchDto dto,
        CancellationToken cancellationToken = default)
        => AssignCoreAsync(dto, null, AutoDispatchActorRole, cancellationToken);

    private async Task<ApiResponseDto<List<DispatchAssignmentResponseDto>>> AssignCoreAsync(
        AssignDispatchDto dto,
        Guid? operatorId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        var unitIds = dto.GetUnitIds();
        var teamItems = (dto.Teams ?? new List<TeamAssignmentDto>()).Where(t => t.TeamId != Guid.Empty).ToList();
        if (unitIds.Count == 0 && teamItems.Count == 0)
        {
            return ApiResponseDto<List<DispatchAssignmentResponseDto>>.FailureResult("Cần chọn ít nhất một tổ cứu hộ.");
        }
        var requestedCount = unitIds.Count + teamItems.Count;

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
            return ApiResponseDto<List<DispatchAssignmentResponseDto>>.FailureResult(LocalizationHelper.Localize("Không thể điều phối cho sự cố đã ở trạng thái '{0}'.", incident.Status));
        }

        // BR-01 / FR-OP-04: mọi sự cố (kể cả Level 0) phải được Operator xác minh trước khi điều xe
        if (incident.VerifiedAt == null || incident.Severity == SeverityLevel.Unclassified)
        {
            return ApiResponseDto<List<DispatchAssignmentResponseDto>>.FailureResult(
                "Sự cố phải được xác minh và phân loại Level 1–3 trước khi điều phối đội cứu hộ.");
        }

        // BR-15: Operator khác đang giữ khóa xử lý sự cố
        if (incident.IsLockedByOther(operatorId ?? Guid.Empty, DateTime.UtcNow))
        {
            return ApiResponseDto<List<DispatchAssignmentResponseDto>>.ConflictResult(
                LocalizationHelper.Localize("Sự cố đang được xử lý bởi {0}.", incident.LockedByOperator?.FullName ?? LocalizationHelper.Localize("điều phối viên khác")));
        }

        // BR-14: số xe chuẩn của mức = tổng kế hoạch điều xe Admin cấu hình cho loại sự cố (tối đa 5);
        // chưa cấu hình → Level 3 tối đa 5 xe, Level 1–2 một xe. Level 1–2 vượt số chuẩn phải có lý do; Level 3 không vượt 5
        var activeCount = incident.DispatchAssignments.Count(a => a.IsActive);
        var planned = DispatchScoring.PlannedUnitCount(await GetUnitRulesAsync(incident, cancellationToken), incident.Severity);
        var maxUnits = incident.Severity == SeverityLevel.Level3
            ? MaxUnitsLevel3
            : planned ?? MaxUnitsDefault;
        // Giao xe theo yêu cầu chi viện (Backup) của đội hiện trường = lý do vượt giới hạn hợp lệ
        if (dto.FieldRequestId is { } fieldRequestId)
        {
            var requestError = await _fieldRequestService.ValidateForDispatchAsync(fieldRequestId, incident.Id, cancellationToken);
            if (requestError != null)
            {
                return ApiResponseDto<List<DispatchAssignmentResponseDto>>.FailureResult(requestError);
            }
        }

        var overrideAllowed = incident.Severity != SeverityLevel.Level3
                              && (dto.FieldRequestId.HasValue || (dto.AllowExtraUnits && (dto.Notes?.Trim().Length ?? 0) >= 5));
        if (activeCount + requestedCount > maxUnits && !overrideAllowed)
        {
            return ApiResponseDto<List<DispatchAssignmentResponseDto>>.FailureResult(
                LocalizationHelper.Localize("Sự cố {0} chỉ được giao tối đa {1} tổ (đang có {2} tổ).", incident.Severity, maxUnits, activeCount) +
                (incident.Severity == SeverityLevel.Level3 ? string.Empty : " " + LocalizationHelper.Localize("Đặt allowExtraUnits = true và ghi lý do vào notes nếu cần thêm xe.")));
        }

        var activeUnitIds = incident.DispatchAssignments.Where(a => a.IsActive).Select(a => a.RescueUnitId).ToHashSet();
        var units = new List<RescueUnit>();
        foreach (var unitId in unitIds)
        {
            var unit = await _rescueUnitRepository.GetByIdWithDetailsAsync(unitId);
            if (unit == null || unit.IsDeleted)
            {
                return ApiResponseDto<List<DispatchAssignmentResponseDto>>.NotFoundResult(LocalizationHelper.Localize("Không tìm thấy phương tiện cứu hộ {0}.", unitId));
            }

            if (activeUnitIds.Contains(unit.Id))
            {
                return ApiResponseDto<List<DispatchAssignmentResponseDto>>.FailureResult(LocalizationHelper.Localize("Phương tiện {0} đã được phát lệnh cho sự cố này trước đó.", unit.PlateNumber));
            }

            units.Add(unit);
        }

        // Giao theo tổ: tổ phải có nhân viên cứu hộ, không đang làm nhiệm vụ khác; mỗi tổ đảm nhận một chức năng (một xe)
        var allUnits = (await _rescueUnitRepository.GetAllWithStationAsync()).Where(u => !u.IsDeleted).ToList();
        var activeAll = await _assignmentRepository.GetAllActiveAsync();
        var teamPool = await LoadTeamPoolAsync(allUnits, activeAll, cancellationToken);
        if (teamItems.Count > 0)
        {
            var busyUnitIds = activeAll.Select(a => a.RescueUnitId).ToHashSet();
            var planTypes = await MatchingUnitTypesAsync(incident, cancellationToken);
            foreach (var item in teamItems)
            {
                var type = string.IsNullOrWhiteSpace(item.UnitType) ? null : item.UnitType.Trim();
                var teamUnits = allUnits.Where(u => u.StationTeamId == item.TeamId).ToList();
                if (teamUnits.Count == 0)
                {
                    return ApiResponseDto<List<DispatchAssignmentResponseDto>>.FailureResult(
                        LocalizationHelper.Localize("Tổ \"{0}\" không tồn tại hoặc chưa phụ trách xe nào nên không có chức năng để nhận sự cố.", item.TeamId));
                }

                var pick = teamUnits
                    .Where(u => u.Status == RescueUnitStatus.Available && !busyUnitIds.Contains(u.Id) && units.All(x => x.Id != u.Id))
                    .Where(u => type != null ? u.UnitType == type : planTypes == null || planTypes.Contains(u.UnitType))
                    .OrderBy(u => CalculateDistanceKm(incident.Latitude, incident.Longitude, u.CurrentLat, u.CurrentLng))
                    .FirstOrDefault();
                if (pick == null)
                {
                    return ApiResponseDto<List<DispatchAssignmentResponseDto>>.FailureResult(
                        LocalizationHelper.Localize("Tổ \"{0}\" không có xe {1} đang sẵn sàng.", teamPool.NameOf(item.TeamId), type ?? LocalizationHelper.Localize("đúng chức năng")));
                }

                units.Add(await _rescueUnitRepository.GetByIdWithDetailsAsync(pick.Id) ?? pick);
            }
        }

        var assignedTeams = new HashSet<Guid>();
        foreach (var unit in units)
        {
            switch (teamPool.Problem(unit))
            {
                case TeamProblem.NoTeam:
                    return ApiResponseDto<List<DispatchAssignmentResponseDto>>.FailureResult(
                        LocalizationHelper.Localize("Xe {0} chưa thuộc tổ nào. Sự cố được giao theo tổ: gán xe vào một tổ có nhân viên trước khi điều phối.", unit.PlateNumber));
                case TeamProblem.NoStaff:
                    return ApiResponseDto<List<DispatchAssignmentResponseDto>>.FailureResult(
                        LocalizationHelper.Localize("Tổ \"{0}\" chưa có nhân viên cứu hộ đang hoạt động nên không thể nhận phân công.", teamPool.NameOf(unit.StationTeamId)));
                case TeamProblem.Busy:
                    return ApiResponseDto<List<DispatchAssignmentResponseDto>>.ConflictResult(
                        LocalizationHelper.Localize("Tổ \"{0}\" đang thực hiện nhiệm vụ khác (hoặc xe của tổ chưa về trạm), không nhận thêm sự cố.", teamPool.NameOf(unit.StationTeamId)));
            }

            if (!assignedTeams.Add(unit.StationTeamId!.Value))
            {
                return ApiResponseDto<List<DispatchAssignmentResponseDto>>.FailureResult(
                    LocalizationHelper.Localize("Tổ \"{0}\" được chọn nhiều lần. Mỗi tổ chỉ đảm nhận một chức năng (một xe) trong sự cố.", teamPool.NameOf(unit.StationTeamId)));
            }
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
                    LocalizationHelper.Localize("Phương tiện {0} không còn sẵn sàng (trạng thái {1}) hoặc vừa được điều phối cho sự cố khác. Vui lòng tải lại danh sách gợi ý.", unit.PlateNumber, unit.Status));
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
                StationTeamId = unit.StationTeamId,
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
        var teamsText = string.Join(", ", units.Select(u => $"{teamPool.NameOf(u.StationTeamId)} ({u.PlateNumber})"));
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
            ChangedByRole = actorRole,
            Notes = $"Giao sự cố cho {units.Count} tổ: {teamsText}."
        }, cancellationToken);

        await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
        {
            IncidentId = incident.Id,
            ActorId = operatorId,
            ActorRole = actorRole,
            Action = activeCount > 0 ? AuditAction.ReDispatched : AuditAction.Dispatched,
            Description = $"Giao sự cố cho {units.Count} tổ: {teamsText}",
            NewStateJson = JsonSerializer.Serialize(new
            {
                teamIds = units.Select(u => u.StationTeamId),
                unitIds = units.Select(u => u.Id),
                plates = units.Select(u => u.PlateNumber),
                algorithm = dto.Algorithm?.ToString(),
                extraUnitsOverride = overrideAllowed && activeCount + units.Count > maxUnits,
                dispatchTimeMs = incident.DispatchTimeMs
            })
        }, cancellationToken);

        try
        {
            await _incidentRepository.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // Sự cố vừa bị hủy / đổi trạng thái bởi người khác: trả xe đã giữ chỗ về Available, không để xe kẹt ở Dispatched
            foreach (var r in reserved)
            {
                await _rescueUnitRepository.ReleaseReservationAsync(r.Id, cancellationToken);
            }

            _logger.LogWarning("Điều phối sự cố {IncId} bị hủy do xung đột đồng thời; đã nhả {Count} xe.", incident.Id, reserved.Count);
            return ApiResponseDto<List<DispatchAssignmentResponseDto>>.ConflictResult(
                "Sự cố vừa được người khác cập nhật. Vui lòng tải lại và thử lại.");
        }

        _logger.LogInformation("Operator {OpId} đã điều phối {Count} xe ({Plates}) tới sự cố {IncId} (DispatchTime={Ms}ms)",
            operatorId, units.Count, plates, incident.Id, incident.DispatchTimeMs);

        var dtos = assignments
            .Select(a => MapToAssignmentDto(a, incident, units.First(u => u.Id == a.RescueUnitId)))
            .ToList();
        foreach (var assignmentDto in dtos)
        {
            assignmentDto.StationTeamName ??= teamPool.NameOf(assignmentDto.StationTeamId);
        }

        foreach (var assignmentDto in dtos)
        {
            var assignedUnit = units.First(u => u.Id == assignmentDto.RescueUnitId);
            await _realtimeNotifier.JobAssignedAsync(assignmentDto, await CrewIdsAsync(assignedUnit, cancellationToken), cancellationToken);
        }
        foreach (var unit in units)
        {
            await PublishUnitStatusAsync(unit, RescueUnitStatus.Available, cancellationToken);
        }
        await _realtimeNotifier.IncidentUpdatedAsync(MapIncidentToSlimDto(incident), cancellationToken);

        // FR-RS-01 / BR-21: còi hú chỉ tới tổ được giao (xe chưa gán tổ → nhân viên trạm); FR-CIT-04 (MSG37): báo người dân
        foreach (var crewGroup in units.GroupBy(u => (u.StationId, u.StationTeamId)))
        {
            await _notificationService.NotifyCrewAsync(crewGroup.Key.StationId, crewGroup.Key.StationTeamId, new NotificationRequest
            {
                Type = NotificationType.JobAssigned,
                Title = new LocalizedText("LỆNH ĐIỀU PHỐI {0}", incident.Severity),
                Message = new LocalizedText("{0} — {1}. {2}, xe: {3}.", incident.Title, incident.LocationAddress,
                    teamPool.NameOf(crewGroup.Key.StationTeamId), string.Join(", ", crewGroup.Select(u => u.PlateNumber))),
                IncidentId = incident.Id,
                Data = new { assignmentIds = assignments.Where(a => crewGroup.Any(u => u.Id == a.RescueUnitId)).Select(a => a.Id) },
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
                    Message = new LocalizedText("Xe {0} được điều tới sự cố {1} ({2} phụ trách) — {3}.", supportPlates, incident.Title,
                        incident.ResponsibleStation?.Name ?? "trạm khác", incident.LocationAddress),
                    IncidentId = incident.Id,
                    HighPriority = true
                }, cancellationToken);
            }
        }

        if (dto.FieldRequestId is { } fulfilledRequestId)
        {
            await _fieldRequestService.MarkFulfilledByDispatchAsync(fulfilledRequestId, units.Select(u => u.PlateNumber).ToList(), operatorId ?? Guid.Empty, cancellationToken);
        }

        // Chưa báo biển số cho người dân: tổ có thể từ chối → chỉ báo đã giao, biển số gửi khi tổ nhận lệnh (Accepted)
        if (incident.ReportedByUserId.HasValue)
        {
            await _notificationService.NotifyUsersAsync(new[] { incident.ReportedByUserId.Value }, new NotificationRequest
            {
                Type = NotificationType.StatusUpdate,
                Title = "Đã giao sự cố cho đội cứu hộ",
                Message = new LocalizedText("[{0}] Trung tâm đã giao sự cố cho đội cứu hộ, đang chờ đội xác nhận nhận lệnh.", incident.Code),
                IncidentId = incident.Id
            }, cancellationToken);
        }

        return ApiResponseDto<List<DispatchAssignmentResponseDto>>.SuccessResult(
            dtos,
            LocalizationHelper.Localize("Đã giao sự cố cho {0} tổ: {1}.", units.Count, teamsText));
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

        if (isCommandCenter && CommandCenterScopeError(rescuer!, incident, unit) is { } rejectScopeError)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.ForbiddenResult(rejectScopeError);
        }
        if (!isCommandCenter && !await IsCrewOfAsync(rescuer, assignment, unit, cancellationToken))
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.ForbiddenResult(CrewForbiddenMessage(unit));
        }

        if (!assignment.CanTransitionTo(DispatchAssignmentStatus.Rejected))
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult(
                LocalizationHelper.Localize("Không thể từ chối nhiệm vụ đang ở trạng thái '{0}'.", assignment.Status));
        }

        // Lượt + xe + audit ghi trong một transaction (khóa sự cố): lỗi xung đột thì xe không bị đổi trạng thái lệch với lượt
        var changedUnits = new List<RescueUnit>();
        var remainingActive = 0;
        await using (var writeLock = incident == null ? null : await _incidentRepository.BeginLockedTransactionAsync(incident.Id, cancellationToken))
        {
            if (incident != null)
            {
                await _incidentRepository.ReloadAsync(incident, cancellationToken);
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
                await SaveUnitAsync(unit, changedUnits, cancellationToken);
            }

            if (incident != null)
            {
                var siblings = await _assignmentRepository.GetByIncidentIdAsync(incident.Id);
                remainingActive = siblings.Count(a => a.Id != assignment.Id && a.IsActive);

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
            if (writeLock != null)
            {
                await writeLock.CommitAsync(cancellationToken);
            }
        }
        await PublishUnitsAsync(changedUnits, cancellationToken);

        _logger.LogWarning("Nhiệm vụ {AssignmentId} bị từ chối bởi {User}: {Reason}", assignment.Id, rescuer?.FullName, dto.Reason);
        if (incident != null)
        {
            await _fieldRequestService.CancelOpenAsync(incident.Id, new[] { assignment.Id }, "Tự hủy: tổ đã từ chối nhiệm vụ.", cancellationToken);
        }

        // Lượt cuối kết thúc: đã có tổ nghiệm thu → đóng sự cố; chưa có → về hàng chờ điều phối (Verified)
        var settled = incident == null
            ? IncidentSettlement.StillActive
            : await SettleIncidentAfterAssignmentEndedAsync(incident, rescuerUserId, rescuer?.Role.ToString() ?? UserRole.RescueStaff.ToString(),
                new LocalizedText("Xe {0} từ chối nhiệm vụ ({1})", unit?.PlateNumber, dto.Reason), cancellationToken);
        if (settled != IncidentSettlement.StillActive) remainingActive = 0;

        var assignmentDto = MapToAssignmentDto(assignment, incident, unit);
        await _realtimeNotifier.AssignmentStatusChangedAsync(assignmentDto, await CrewIdsAsync(unit, cancellationToken, CrewTeamOf(assignment, unit)), cancellationToken);
        if (incident != null)
        {
            await _realtimeNotifier.IncidentUpdatedAsync(MapIncidentToSlimDto(incident), cancellationToken);
        }

        // BR-22: cảnh báo trung tâm để điều xe thay thế ngay
        await _notificationService.NotifyRolesAsync(new[] { UserRole.Operator, UserRole.Admin }, new NotificationRequest
        {
            Type = NotificationType.StatusUpdate,
            Title = "Đội cứu hộ từ chối nhiệm vụ",
            Message = new LocalizedText("[{0}] Xe {1} từ chối sự cố \"{2}\" ({3}). {4}", incident?.Code, unit?.PlateNumber, incident?.Title, dto.Reason,
                settled == IncidentSettlement.Closed
                    ? new LocalizedText("Sự cố đã tự đóng vì tổ khác đã nghiệm thu.")
                    : remainingActive > 0 ? new LocalizedText("Còn {0} xe đang xử lý.", remainingActive)
                    : incident?.Status == IncidentStatus.Escalated ? new LocalizedText("Sự cố đang leo thang — cần điều xe thay thế ngay.")
                    : new LocalizedText("Cần điều xe thay thế.")),
            IncidentId = incident?.Id,
            Data = new { assignmentId = assignment.Id, reason = dto.Reason.ToString(), incidentClosed = settled == IncidentSettlement.Closed },
            HighPriority = settled == IncidentSettlement.ReturnedToQueue
        }, cancellationToken);

        return ApiResponseDto<DispatchAssignmentResponseDto>.SuccessResult(
            assignmentDto,
            settled switch
            {
                IncidentSettlement.Closed => "Đã từ chối nhiệm vụ. Sự cố đã được đóng vì tổ khác đã nghiệm thu.",
                IncidentSettlement.ReturnedToQueue => "Đã từ chối nhiệm vụ. Trung tâm điều phối sẽ giao xe thay thế.",
                _ => "Đã từ chối nhiệm vụ."
            });
    }

    public async Task<ApiResponseDto<DispatchAssignmentResponseDto>> GetAssignmentByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var assignment = await _assignmentRepository.GetForViewAsync(id, cancellationToken);
        if (assignment == null)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult("Không tìm thấy thông tin phân công.");
        }

        return ApiResponseDto<DispatchAssignmentResponseDto>.SuccessResult(MapToAssignmentDto(assignment, assignment.Incident, assignment.RescueUnit));
    }

    public const string InvalidRouteOriginMessage = "Tọa độ xuất phát không hợp lệ: cần gửi đủ fromLat và fromLng trong phạm vi hợp lệ.";

    public async Task<ApiResponseDto<AssignmentRouteDto>> GetAssignmentRouteAsync(
        Guid assignmentId, double? fromLat, double? fromLng, CancellationToken cancellationToken = default)
    {
        if (fromLat.HasValue != fromLng.HasValue
            || fromLat is < -90 or > 90 || fromLng is < -180 or > 180)
        {
            return ApiResponseDto<AssignmentRouteDto>.FailureResult(InvalidRouteOriginMessage);
        }

        var assignment = await _assignmentRepository.GetForViewAsync(assignmentId, cancellationToken);
        if (assignment?.Incident == null)
        {
            return ApiResponseDto<AssignmentRouteDto>.NotFoundResult("Không tìm thấy thông tin phân công.");
        }

        // Ưu tiên GPS app gửi lên (mới nhất); không có thì vị trí xe gần nhất; xe chưa gửi GPS thì vị trí trạm
        var unit = assignment.RescueUnit;
        var originLat = fromLat ?? (unit is { CurrentLat: not 0 } ? unit.CurrentLat : unit?.Station?.Latitude ?? assignment.Incident.Latitude);
        var originLng = fromLng ?? (unit is { CurrentLng: not 0 } ? unit.CurrentLng : unit?.Station?.Longitude ?? assignment.Incident.Longitude);
        var route = await _routingService.GetNavigationRouteAsync(originLat, originLng,
            assignment.Incident.Latitude, assignment.Incident.Longitude, cancellationToken);

        return ApiResponseDto<AssignmentRouteDto>.SuccessResult(new AssignmentRouteDto
        {
            AssignmentId = assignment.Id,
            FromLat = originLat,
            FromLng = originLng,
            ToLat = assignment.Incident.Latitude,
            ToLng = assignment.Incident.Longitude,
            Points = route.Points,
            Steps = route.Steps,
            DistanceKm = route.DistanceKm,
            DurationMinutes = route.DurationMinutes,
            IsApproximate = route.IsApproximate
        });
    }

    public async Task<bool> IsCrewOfAssignmentAsync(Guid assignmentId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Bản xem: xe / tổ đã xóa mềm vẫn được nạp, nên tổ vẫn xem lại được nhiệm vụ của xe đã xóa
        var assignment = await _assignmentRepository.GetForViewAsync(assignmentId, cancellationToken);
        var user = await _userRepository.GetByIdAsync(userId);
        if (assignment == null || user is not { Role: UserRole.RescueStaff })
        {
            return false;
        }
        return assignment.CompletionReport?.SubmittedByUserId == userId
               || await IsCrewOfAsync(user, assignment, assignment.RescueUnit, cancellationToken);
    }

    public async Task<ApiResponseDto<List<DispatchAssignmentResponseDto>>> GetAssignmentsByIncidentIdAsync(
        Guid incidentId,
        CancellationToken cancellationToken = default)
    {
        var list = await _assignmentRepository.GetByIncidentForViewAsync(incidentId, cancellationToken);
        var incident = await _incidentRepository.GetByIdAsync(incidentId);

        var dtos = list.Select(a => MapToAssignmentDto(a, incident ?? a.Incident, a.RescueUnit)).ToList();
        return ApiResponseDto<List<DispatchAssignmentResponseDto>>.SuccessResult(dtos);
    }

    public const int MyAssignmentsDefaultLimit = 50;
    public const int MyAssignmentsMaxPageSize = 100;
    public const string InvalidAssignmentScopeMessage = "Giá trị scope không hợp lệ (active | history | all).";

    public async Task<ApiResponseDto<List<DispatchAssignmentResponseDto>>> GetMyAssignmentsAsync(
        Guid rescuerUserId,
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        bool? active;
        switch (scope?.Trim().ToLowerInvariant())
        {
            case null or "" or "all": active = null; break;
            case "active": active = true; break;
            case "history": active = false; break;
            default: return ApiResponseDto<List<DispatchAssignmentResponseDto>>.FailureResult(InvalidAssignmentScopeMessage);
        }

        var rescuer = await _userRepository.GetByIdAsync(rescuerUserId);
        if (rescuer == null)
        {
            return ApiResponseDto<List<DispatchAssignmentResponseDto>>.FailureResult("Không tìm thấy thông tin tài khoản.");
        }
        if (!rescuer.StationId.HasValue)
        {
            return ApiResponseDto<List<DispatchAssignmentResponseDto>>.SuccessResult(new List<DispatchAssignmentResponseDto>());
        }

        // Lượt đang chạy luôn đủ (mỗi tổ tối đa một lượt); danh sách có lịch sử giới hạn 50 lượt mới nhất — xem trang /history
        var teamId = await _userRepository.GetTeamIdAsync(rescuer.Id, cancellationToken);
        var (items, _) = await _assignmentRepository.GetCrewAssignmentsAsync(
            rescuer.StationId.Value, teamId, CrewSubmitter(rescuer), active, null, null, 0,
            active == true ? MyAssignmentsMaxPageSize : MyAssignmentsDefaultLimit, cancellationToken);

        var dtos = items.Select(a => MapToAssignmentDto(a, a.Incident, a.RescueUnit)).ToList();
        return ApiResponseDto<List<DispatchAssignmentResponseDto>>.SuccessResult(dtos);
    }

    public async Task<ApiResponseDto<PaginatedResultDto<DispatchAssignmentResponseDto>>> GetMyAssignmentHistoryAsync(
        Guid rescuerUserId,
        MyAssignmentHistoryFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        if (filter.From.HasValue && filter.To.HasValue && filter.From > filter.To)
        {
            return ApiResponseDto<PaginatedResultDto<DispatchAssignmentResponseDto>>.FailureResult("Thời gian bắt đầu phải trước thời gian kết thúc.");
        }

        var pageIndex = Math.Max(1, filter.PageIndex);
        var pageSize = Math.Clamp(filter.PageSize, 1, MyAssignmentsMaxPageSize);
        var rescuer = await _userRepository.GetByIdAsync(rescuerUserId);
        if (rescuer == null)
        {
            return ApiResponseDto<PaginatedResultDto<DispatchAssignmentResponseDto>>.FailureResult("Không tìm thấy thông tin tài khoản.");
        }
        if (!rescuer.StationId.HasValue)
        {
            return ApiResponseDto<PaginatedResultDto<DispatchAssignmentResponseDto>>.SuccessResult(
                new PaginatedResultDto<DispatchAssignmentResponseDto>(new List<DispatchAssignmentResponseDto>(), 0, pageIndex, pageSize));
        }

        var teamId = await _userRepository.GetTeamIdAsync(rescuer.Id, cancellationToken);
        var (items, total) = await _assignmentRepository.GetCrewAssignmentsAsync(
            rescuer.StationId.Value, teamId, CrewSubmitter(rescuer), false, filter.From, filter.To,
            (pageIndex - 1) * pageSize, pageSize, cancellationToken);

        var dtos = items.Select(a => MapToAssignmentDto(a, a.Incident, a.RescueUnit)).ToList();
        return ApiResponseDto<PaginatedResultDto<DispatchAssignmentResponseDto>>.SuccessResult(
            new PaginatedResultDto<DispatchAssignmentResponseDto>(dtos, total, pageIndex, pageSize));
    }

    /// <summary>
    /// Nhân viên cứu hộ xem lại được nhiệm vụ có báo cáo do mình nộp (kể cả khi tổ đã giải thể); Operator / Admin không cần
    /// </summary>
    private static Guid? CrewSubmitter(User viewer) => viewer.Role == UserRole.RescueStaff ? viewer.Id : null;

    public const string CompletionReportForbiddenMessage = "Bạn không có quyền xem báo cáo nghiệm thu này.";
    private const int MaxCompletionReportPageSize = 100;

    /// <summary>
    /// Phạm vi xem báo cáo nghiệm thu của người dùng hiện tại. Error != null = không được xem
    /// </summary>
    private async Task<(string? Error, Guid? StationId, Guid? CrewTeamId, Guid? CrewUserId, Guid? CrewStationId)> CompletionReportScopeAsync(
        Guid viewerUserId, Guid? requestedStationId, CancellationToken cancellationToken)
    {
        switch (_currentUser.Role)
        {
            case UserRole.Admin:
                return (null, requestedStationId, null, null, null);
            case UserRole.Operator:
                return _currentUser.StationId.HasValue
                    ? (null, _currentUser.StationId, null, null, null)
                    : (OperatorScope.NoStationMessage, null, null, null, null);
            case UserRole.RescueStaff:
                // Tổ mình + báo cáo do mình nộp + báo cáo của xe chưa gán tổ ở trạm mình (xe đó ai trong trạm cũng làm được)
                var teamId = await _userRepository.GetTeamIdAsync(viewerUserId, cancellationToken);
                return (null, null, teamId, viewerUserId, _currentUser.StationId);
            default:
                return (CompletionReportForbiddenMessage, null, null, null, null);
        }
    }

    public async Task<ApiResponseDto<PaginatedResultDto<CompletionReportHistoryItemDto>>> GetCompletionReportsAsync(
        CompletionReportFilterDto filter,
        Guid viewerUserId,
        CancellationToken cancellationToken = default)
    {
        if (filter.From.HasValue && filter.To.HasValue && filter.From > filter.To)
        {
            return ApiResponseDto<PaginatedResultDto<CompletionReportHistoryItemDto>>.FailureResult("Thời gian bắt đầu phải trước thời gian kết thúc.");
        }

        var scope = await CompletionReportScopeAsync(viewerUserId, filter.StationId, cancellationToken);
        if (scope.Error != null)
        {
            return ApiResponseDto<PaginatedResultDto<CompletionReportHistoryItemDto>>.ForbiddenResult(scope.Error);
        }

        var pageIndex = Math.Max(1, filter.PageIndex);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxCompletionReportPageSize);
        var (items, total) = await _assignmentRepository.GetCompletionReportsAsync(
            scope.StationId, filter.TeamId, filter.IncidentId, scope.CrewTeamId, scope.CrewUserId, scope.CrewStationId,
            filter.From, filter.To, filter.Search, pageIndex, pageSize, cancellationToken);

        var page = new PaginatedResultDto<CompletionReportHistoryItemDto>(items.Select(MapCompletionReportHistory).ToList(), total, pageIndex, pageSize);
        return ApiResponseDto<PaginatedResultDto<CompletionReportHistoryItemDto>>.SuccessResult(page,
            LocalizationHelper.Localize("Có {0} báo cáo nghiệm thu.", total));
    }

    public async Task<ApiResponseDto<CompletionReportHistoryItemDto>> GetCompletionReportByIdAsync(
        Guid id,
        Guid viewerUserId,
        CancellationToken cancellationToken = default)
    {
        var report = await _assignmentRepository.GetCompletionReportByIdAsync(id, cancellationToken);
        if (report == null)
        {
            return ApiResponseDto<CompletionReportHistoryItemDto>.NotFoundResult("Không tìm thấy báo cáo nghiệm thu.");
        }

        var scope = await CompletionReportScopeAsync(viewerUserId, null, cancellationToken);
        var allowed = scope.Error == null && (scope.CrewUserId.HasValue
            ? (scope.CrewTeamId.HasValue && report.StationTeamId == scope.CrewTeamId)
              || report.SubmittedByUserId == scope.CrewUserId
              || (scope.CrewStationId.HasValue && report.StationTeamId == null
                  && report.Assignments.Any(a => a.RescueUnit?.StationId == scope.CrewStationId))
            : !scope.StationId.HasValue
              || report.StationTeam?.StationId == scope.StationId
              || report.Incident?.ResponsibleStationId == scope.StationId
              || report.Assignments.Any(a => a.RescueUnit?.StationId == scope.StationId));
        if (!allowed)
        {
            return ApiResponseDto<CompletionReportHistoryItemDto>.ForbiddenResult(scope.Error ?? CompletionReportForbiddenMessage);
        }

        return ApiResponseDto<CompletionReportHistoryItemDto>.SuccessResult(MapCompletionReportHistory(report));
    }

    private static CompletionReportHistoryItemDto MapCompletionReportHistory(CompletionReport r)
    {
        var firstUnitStation = r.Assignments.Select(a => a.RescueUnit?.Station).FirstOrDefault(s => s != null);
        return new CompletionReportHistoryItemDto
        {
            Id = r.Id,
            StationTeamId = r.StationTeamId,
            StationTeamName = r.StationTeam?.Name,
            StationTeamDeleted = r.StationTeam?.IsDeleted == true,
            ActionsSummary = r.ActionsSummary,
            VictimsAssistedCount = r.VictimsAssistedCount,
            EquipmentUsed = r.EquipmentUsed,
            EvidenceMediaUrls = r.MediaItems.Select(m => m.MediaUrl).ToList(),
            HandoverEntity = r.HandoverEntity,
            HandoverContactName = r.HandoverContactName,
            HandoverContactPhone = r.HandoverContactPhone,
            HandoverNotes = r.HandoverNotes,
            SignatureUrl = r.SignatureUrl,
            SignerName = r.SignerName,
            SignedAt = r.SignedAt,
            SubmittedByUserId = r.SubmittedByUserId,
            SubmittedByName = r.SubmittedByUser?.FullName ?? r.SignerName,
            SubmittedAt = r.SubmittedAt,
            IncidentId = r.IncidentId,
            IncidentCode = r.Incident?.Code ?? string.Empty,
            IncidentTitle = r.Incident?.Title ?? string.Empty,
            IncidentAddress = r.Incident?.LocationAddress ?? string.Empty,
            IncidentStatus = r.Incident?.Status,
            IncidentSeverity = r.Incident?.Severity,
            IncidentClosedAt = r.Incident?.ClosedAt,
            StationId = r.StationTeam?.StationId ?? firstUnitStation?.Id,
            StationName = r.StationTeam?.Station?.Name ?? firstUnitStation?.Name,
            Units = r.Assignments
                .OrderBy(a => a.DispatchedAt)
                .Select(a => new CompletionReportUnitDto
                {
                    AssignmentId = a.Id,
                    RescueUnitId = a.RescueUnitId,
                    PlateNumber = a.RescueUnit?.PlateNumber ?? string.Empty,
                    UnitType = a.RescueUnit?.UnitType ?? string.Empty,
                    DispatchedAt = a.DispatchedAt,
                    ArrivedAt = a.ArrivedAt,
                    CompletedAt = a.CompletedAt
                })
                .ToList()
        };
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
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult(LocalizationHelper.Localize("Nhiệm vụ đã ở trạng thái '{0}', không thể thay đổi.", assignment.Status));
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

        if (isCommandCenter && CommandCenterScopeError(rescuer!, incident, unit) is { } statusScopeError)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.ForbiddenResult(statusScopeError);
        }

        if (!isCommandCenter)
        {
            if (!await IsCrewOfAsync(rescuer, assignment, unit, cancellationToken))
            {
                return ApiResponseDto<DispatchAssignmentResponseDto>.ForbiddenResult(CrewForbiddenMessage(unit));
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
                LocalizationHelper.Localize("Không thể chuyển nhiệm vụ từ '{0}' sang '{1}'. Thứ tự hợp lệ: Dispatched → Accepted → EnRoute → OnScene.", assignment.Status, dto.Status));
        }

        // Tiến độ theo tổ: một thành viên cập nhật → mọi xe của tổ đang làm sự cố này (cùng trạng thái) cùng chuyển,
        // các thành viên khác chỉ cần xem. Xe chưa gán tổ → chỉ xe này.
        var crewItems = new List<(DispatchAssignment Item, RescueUnit? Unit)> { (assignment, unit) };
        if (CrewTeamOf(assignment, unit) is Guid crewTeamId && incident != null)
        {
            var siblings = await _assignmentRepository.GetByIncidentIdAsync(incident.Id) ?? new List<DispatchAssignment>();
            crewItems.AddRange(siblings
                .Where(a => a.Id != assignment.Id && a.IsActive && CrewTeamOf(a, a.RescueUnit) == crewTeamId
                            && a.Status == assignment.Status && a.CanTransitionTo(dto.Status))
                .Select(a => (a, a.RescueUnit)));
        }

        // Mọi lượt của tổ + xe + sự cố ghi trong một transaction (khóa sự cố): xung đột giữa chừng thì rollback hết,
        // không còn cảnh xe đã đổi trạng thái mà lượt thì chưa
        var changedUnits = new List<RescueUnit>();
        await using (var writeLock = incident == null ? null : await _incidentRepository.BeginLockedTransactionAsync(incident.Id, cancellationToken))
        {
            if (incident != null)
            {
                await _incidentRepository.ReloadAsync(incident, cancellationToken);
            }
            foreach (var (item, itemUnit) in crewItems)
            {
                await ApplyStatusAsync(item, itemUnit);
            }
            if (writeLock != null)
            {
                await writeLock.CommitAsync(cancellationToken);
            }
        }
        await PublishUnitsAsync(changedUnits, cancellationToken);

        async Task ApplyStatusAsync(DispatchAssignment item, RescueUnit? itemUnit)
        {
            var oldStatus = item.Status;
            item.Status = dto.Status;
            item.UpdatedAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(dto.Notes))
            {
                item.Notes = dto.Notes.Trim();
            }

            switch (dto.Status)
            {
                case DispatchAssignmentStatus.Accepted:
                    item.AcceptedAt = DateTime.UtcNow;
                    // FR-RS-01: đội đã nhận lệnh → sự cố chuyển sang đang xử lý
                    if (incident != null && incident.Status is IncidentStatus.Dispatched or IncidentStatus.Escalated)
                    {
                        if (incident.Status == IncidentStatus.Escalated)
                        {
                            incident.Status = IncidentStatus.InProgress;
                            await DeEscalateAsync(incident, rescuerUserId, $"xe {itemUnit?.PlateNumber} đã nhận lệnh", cancellationToken);
                        }
                        incident.Status = IncidentStatus.InProgress;
                        incident.UpdatedAt = DateTime.UtcNow;
                        await _incidentRepository.UpdateAsync(incident);
                    }
                    break;

                case DispatchAssignmentStatus.Cancelled:
                    if (itemUnit != null)
                    {
                        itemUnit.Status = RescueUnitStatus.Available;
                        itemUnit.UpdatedAt = DateTime.UtcNow;
                        await SaveUnitAsync(itemUnit, changedUnits, cancellationToken);
                    }
                    break;

                case DispatchAssignmentStatus.EnRoute:
                    item.EnRouteAt = DateTime.UtcNow;
                    if (itemUnit != null)
                    {
                        itemUnit.Status = RescueUnitStatus.Dispatched;
                        itemUnit.UpdatedAt = DateTime.UtcNow;
                        await SaveUnitAsync(itemUnit, changedUnits, cancellationToken);
                    }
                    break;

                case DispatchAssignmentStatus.OnScene:
                    item.ArrivedAt = DateTime.UtcNow;
                    if (itemUnit != null)
                    {
                        itemUnit.Status = RescueUnitStatus.OnScene;
                        itemUnit.UpdatedAt = DateTime.UtcNow;
                        await SaveUnitAsync(itemUnit, changedUnits, cancellationToken);
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
                AssignmentId = item.Id,
                OldStatus = oldStatus,
                NewStatus = dto.Status,
                ChangedByUserId = rescuerUserId,
                Notes = dto.Notes?.Trim(),
                ChangedAt = DateTime.UtcNow
            });

            await _assignmentRepository.UpdateAsync(item);

            if (incident != null)
            {
                await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
                {
                    IncidentId = incident.Id,
                    ActorId = rescuerUserId,
                    ActorRole = rescuer?.Role.ToString() ?? UserRole.RescueStaff.ToString(),
                    Action = AuditAction.StatusChanged,
                    Description = item.Id == assignment.Id
                        ? $"Đội cứu hộ {itemUnit?.PlateNumber} cập nhật trạng thái nhiệm vụ: {oldStatus} ➔ {dto.Status}"
                        : $"Đội cứu hộ {itemUnit?.PlateNumber} cập nhật trạng thái nhiệm vụ: {oldStatus} ➔ {dto.Status} (cùng tổ với xe {unit?.PlateNumber}, do {actorName} cập nhật)",
                    NewStateJson = JsonSerializer.Serialize(new { assignmentId = item.Id, status = dto.Status.ToString() })
                }, cancellationToken);
                await _incidentRepository.SaveChangesAsync(cancellationToken);
            }
        }

        _logger.LogInformation("Nhiệm vụ {AssignmentId} được {Actor} chuyển trạng thái sang {NewStatus} ({Count} xe của tổ)",
            id, actorName, dto.Status, crewItems.Count);

        // Thu hồi lượt cuối: đã có tổ nghiệm thu → đóng sự cố; chưa có → về hàng chờ điều phối (Verified)
        var settled = IncidentSettlement.StillActive;
        if (dto.Status == DispatchAssignmentStatus.Cancelled && incident != null)
        {
            await _fieldRequestService.CancelOpenAsync(incident.Id, crewItems.Select(c => c.Item.Id).ToList(), "Tự hủy: xe đã bị thu hồi.", cancellationToken);
            settled = await SettleIncidentAfterAssignmentEndedAsync(incident, rescuerUserId, rescuer?.Role.ToString() ?? UserRole.Operator.ToString(),
                new LocalizedText("{0} thu hồi xe {1}", actorName, string.Join(", ", crewItems.Select(c => c.Unit?.PlateNumber))), cancellationToken);
        }

        var assignmentDto = MapToAssignmentDto(assignment, incident, unit);

        // Bắn SignalR: cả tổ thấy tiến độ mới của mọi xe vừa chuyển; tổ khác cùng trạm không nhận
        var crewIds = await CrewIdsAsync(unit, cancellationToken, CrewTeamOf(assignment, unit));
        foreach (var (item, itemUnit) in crewItems)
        {
            var itemDto = item.Id == assignment.Id ? assignmentDto : MapToAssignmentDto(item, incident, itemUnit);
            await _realtimeNotifier.AssignmentStatusChangedAsync(itemDto, crewIds, cancellationToken);
        }
        if (incident != null)
        {
            await _realtimeNotifier.IncidentUpdatedAsync(MapIncidentToSlimDto(incident), cancellationToken);
        }

        // FR-CIT-04 (MSG37, MSG40): báo người dân khi tổ nhận lệnh (lần đầu biết biển số), khi xe xuất phát và khi tới hiện trường
        if (incident?.ReportedByUserId != null && dto.Status is DispatchAssignmentStatus.Accepted or DispatchAssignmentStatus.EnRoute or DispatchAssignmentStatus.OnScene)
        {
            var crewPlates = string.Join(", ", crewItems.Select(c => c.Unit?.PlateNumber).Where(p => !string.IsNullOrWhiteSpace(p)));
            await _notificationService.NotifyUsersAsync(new[] { incident.ReportedByUserId.Value }, new NotificationRequest
            {
                Type = NotificationType.StatusUpdate,
                Title = dto.Status switch
                {
                    DispatchAssignmentStatus.Accepted => "Đội cứu hộ đã nhận lệnh",
                    DispatchAssignmentStatus.EnRoute => "Xe cứu hộ đang trên đường",
                    _ => "Đội cứu hộ đã tới hiện trường"
                },
                Message = dto.Status switch
                {
                    DispatchAssignmentStatus.Accepted => new LocalizedText("[{0}] Xe {1} đã nhận lệnh và đang chuẩn bị xuất phát tới vị trí của bạn.", incident.Code, crewPlates),
                    DispatchAssignmentStatus.EnRoute => new LocalizedText("[{0}] Xe {1} đang di chuyển tới vị trí của bạn (dự kiến ~{2:F0} phút).", incident.Code, unit?.PlateNumber, assignment.EstimatedArrivalMinutes ?? 0),
                    _ => new LocalizedText("[{0}] Xe {1} đã có mặt tại hiện trường. Đội cứu hộ đang triển khai.", incident.Code, unit?.PlateNumber)
                },
                IncidentId = incident.Id,
                Data = new { assignmentId = assignment.Id, status = dto.Status.ToString() }
            }, cancellationToken);
        }

        return ApiResponseDto<DispatchAssignmentResponseDto>.SuccessResult(
            assignmentDto,
            settled switch
            {
                IncidentSettlement.Closed => "Đã thu hồi xe. Sự cố đã được đóng vì các tổ khác đã nghiệm thu.",
                IncidentSettlement.ReturnedToQueue => "Đã thu hồi xe. Sự cố không còn tổ nào xử lý, đã trả về hàng chờ điều phối.",
                _ => LocalizationHelper.Localize("Cập nhật trạng thái nhiệm vụ thành '{0}' thành công.", dto.Status)
            });
    }

    private enum IncidentSettlement { StillActive, Closed, ReturnedToQueue }

    /// <summary>
    /// Sau khi một lượt kết thúc không qua nghiệm thu (thu hồi / từ chối): khóa sự cố, đọc lại từ DB rồi xét
    /// — còn lượt đang chạy → giữ nguyên; hết lượt và đã có lượt Completed → đóng sự cố như nghiệm thu;
    /// hết lượt và chưa có ai nghiệm thu → trả về Verified để giao tổ khác.
    /// Khóa giống nghiệm thu nên hai tổ kết thúc cùng lúc vẫn đóng đúng một lần.
    /// </summary>
    private async Task<IncidentSettlement> SettleIncidentAfterAssignmentEndedAsync(
        Incident incident, Guid actorId, string actorRole, LocalizedText endedBy, CancellationToken cancellationToken)
    {
        await using var lockScope = await _incidentRepository.BeginLockedTransactionAsync(incident.Id, cancellationToken);
        await _incidentRepository.ReloadAsync(incident, cancellationToken);
        if (incident.Status is IncidentStatus.Completed or IncidentStatus.Cancelled)
        {
            return IncidentSettlement.StillActive;
        }

        var fresh = await _assignmentRepository.GetFreshByIncidentIdAsync(incident.Id, cancellationToken) ?? Array.Empty<DispatchAssignment>();
        if (fresh.Any(a => a.IsActive))
        {
            return IncidentSettlement.StillActive;
        }

        var now = DateTime.UtcNow;
        var oldStatus = incident.Status;
        var completed = fresh.Where(a => a.Status == DispatchAssignmentStatus.Completed).ToList();
        if (completed.Count > 0)
        {
            var summary = completed
                .Where(a => a.CompletionReport != null)
                .OrderByDescending(a => a.CompletionReport!.SubmittedAt)
                .Select(a => a.CompletionReport!.ActionsSummary)
                .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "Các tổ đã nghiệm thu hoàn tất.";
            incident.Close(actorId, summary);
            incident.ResolutionTimeMs = (long)(now - incident.CreatedAt).TotalMilliseconds;

            await _incidentRepository.AddStatusHistoryAsync(new IncidentStatusHistory
            {
                IncidentId = incident.Id,
                OldStatus = oldStatus,
                NewStatus = IncidentStatus.Completed,
                ChangedByUserId = actorId,
                ChangedByRole = actorRole,
                Notes = $"Tự đóng sự cố: {endedBy}; các tổ còn lại đã nghiệm thu."
            }, cancellationToken);
            await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
            {
                IncidentId = incident.Id,
                ActorId = actorId,
                ActorRole = actorRole,
                Action = AuditAction.Completed,
                Description = $"Sự cố tự đóng sau khi {endedBy}: không còn tổ nào đang xử lý, {completed.Count} lượt đã nghiệm thu",
                NewStateJson = JsonSerializer.Serialize(new
                {
                    autoClosed = true,
                    completedAssignments = completed.Count,
                    resolutionTimeMs = incident.ResolutionTimeMs,
                    closedBy = actorId
                })
            }, cancellationToken);
            await _incidentRepository.SaveChangesAsync(cancellationToken);
            await lockScope.CommitAsync(cancellationToken);

            _logger.LogInformation("Sự cố {IncidentId} tự đóng sau khi {EndedBy} (T_Response={Ms}ms)", incident.Id, endedBy, incident.ResolutionTimeMs);
            await _realtimeNotifier.IncidentUpdatedAsync(MapIncidentToSlimDto(incident), cancellationToken);
            await AfterIncidentClosedAsync(incident, actorId, cancellationToken);
            await NotifyCommandCenterAsync(incident, "SỰ CỐ ĐÃ TỰ ĐÓNG",
                new LocalizedText("[{0}] {1} — {2}. Các tổ còn lại đã nghiệm thu nên sự cố được đóng.", incident.Code, incident.Title, endedBy), false, cancellationToken);
            return IncidentSettlement.Closed;
        }

        // Sự cố đã leo thang (quá hạn mà tổ chưa nhận lệnh) rồi mất tổ cuối: giữ Escalated để vẫn là ưu tiên khẩn trong hàng chờ,
        // log leo thang còn mở; chỉ ghi nhật ký và báo trung tâm cần điều tổ thay thế ngay
        if (incident.Status == IncidentStatus.Escalated)
        {
            await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
            {
                IncidentId = incident.Id,
                ActorId = actorId,
                ActorRole = actorRole,
                Action = AuditAction.StatusChanged,
                Description = $"{endedBy}. Không còn tổ nào xử lý, sự cố vẫn đang leo thang — cần điều phối tổ thay thế ngay.",
                NewStateJson = JsonSerializer.Serialize(new { status = IncidentStatus.Escalated.ToString(), activeAssignments = 0 })
            }, cancellationToken);
            await _incidentRepository.SaveChangesAsync(cancellationToken);
            await lockScope.CommitAsync(cancellationToken);
            await _realtimeNotifier.IncidentUpdatedAsync(MapIncidentToSlimDto(incident), cancellationToken);
            return IncidentSettlement.ReturnedToQueue;
        }

        if (incident.Status is not (IncidentStatus.Dispatched or IncidentStatus.InProgress))
        {
            return IncidentSettlement.StillActive;
        }

        incident.Status = IncidentStatus.Verified;
        incident.UpdatedAt = now;
        await _incidentRepository.AddStatusHistoryAsync(new IncidentStatusHistory
        {
            IncidentId = incident.Id,
            OldStatus = oldStatus,
            NewStatus = IncidentStatus.Verified,
            ChangedByUserId = actorId,
            ChangedByRole = actorRole,
            Notes = $"{endedBy}. Không còn tổ nào xử lý, cần điều phối tổ thay thế."
        }, cancellationToken);
        await _incidentRepository.SaveChangesAsync(cancellationToken);
        await lockScope.CommitAsync(cancellationToken);
        await _realtimeNotifier.IncidentUpdatedAsync(MapIncidentToSlimDto(incident), cancellationToken);
        return IncidentSettlement.ReturnedToQueue;
    }

    /// <summary>
    /// Việc sau khi sự cố đóng (nghiệm thu hoặc tự đóng): đóng báo cáo đã gom (BR-07), báo người dân (FR-CIT-04)
    /// </summary>
    private async Task AfterIncidentClosedAsync(Incident incident, Guid actorId, CancellationToken cancellationToken)
    {
        if (incident.LinkedReportCount > 0)
        {
            await _followUpService.CloseLinkedReportsAsync(incident.Id, actorId, cancellationToken);
        }

        // Yêu cầu hiện trường còn mở của sự cố đã đóng không còn ý nghĩa
        await _fieldRequestService.CancelOpenAsync(incident.Id, null, "Tự hủy: sự cố đã đóng.", cancellationToken);

        // Phòng hờ: sự cố đã đóng mà còn log leo thang mở (dữ liệu lệch) → đóng luôn
        if (await _escalationRepository.ResolveOpenLogsAsync(incident.Id, DateTime.UtcNow, cancellationToken) > 0)
        {
            await _incidentRepository.SaveChangesAsync(cancellationToken);
        }

        if (incident.ReportedByUserId.HasValue)
        {
            await _notificationService.NotifyUsersAsync(new[] { incident.ReportedByUserId.Value }, new NotificationRequest
            {
                Type = NotificationType.StatusUpdate,
                Title = "Cứu hộ đã hoàn tất",
                Message = new LocalizedText("[{0}] Sự cố \"{1}\" đã được xử lý xong. Cảm ơn bạn đã báo tin.", incident.Code, incident.Title),
                IncidentId = incident.Id
            }, cancellationToken);
        }
    }

    /// <summary>
    /// Báo Operator trạm phụ trách + Admin
    /// </summary>
    private async Task NotifyCommandCenterAsync(Incident incident, LocalizedText title, LocalizedText message, bool highPriority, CancellationToken cancellationToken)
    {
        var recipients = (await _userRepository.GetActiveByRolesAsync(new[] { UserRole.Operator, UserRole.Admin }) ?? new List<User>())
            .Where(u => u.Role == UserRole.Admin || incident.ResponsibleStationId == null || u.StationId == incident.ResponsibleStationId)
            .Select(u => u.Id)
            .ToList();
        if (recipients.Count == 0) return;
        await _notificationService.NotifyUsersAsync(recipients, new NotificationRequest
        {
            Type = NotificationType.StatusUpdate,
            Title = title,
            Message = message,
            IncidentId = incident.Id,
            HighPriority = highPriority
        }, cancellationToken);
    }

    public const string SignatureRequiredMessage = "Cần chữ ký xác nhận của thành viên tổ trước khi nộp báo cáo nghiệm thu.";

    /// <summary>
    /// Nghiệm thu theo tổ: một thành viên bất kỳ của tổ (đại diện) chụp ảnh + ký → hoàn tất mọi xe của tổ đang làm sự cố này,
    /// không phân biệt xe đã tới hiện trường hay chưa. Xe chưa gán tổ thì nghiệm thu riêng xe đó.
    /// Sự cố nhiều tổ: mỗi tổ tự nộp; sự cố chỉ đóng khi tổ (xe) cuối cùng nộp (BR-14).
    /// </summary>
    public async Task<ApiResponseDto<DispatchAssignmentResponseDto>> ResolveAssignmentAsync(
        Guid id,
        ResolveDispatchDto dto,
        Guid rescuerUserId,
        CancellationToken cancellationToken = default)
    {
        var incidentId = await _assignmentRepository.GetIncidentIdAsync(id, cancellationToken);
        if (incidentId == null)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult("Không tìm thấy thông tin phân công.");
        }

        // BR-14: khóa sự cố TRƯỚC khi đọc để các tổ cùng sự cố nghiệm thu tuần tự —
        // đếm đúng số xe còn lại, đóng sự cố đúng một lần, bấm nghiệm thu hai lần thì lần sau thấy Completed
        await using var lockScope = await _incidentRepository.BeginLockedTransactionAsync(incidentId.Value, cancellationToken);

        var assignment = await _assignmentRepository.GetByIdWithDetailsAsync(id);
        if (assignment == null)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult("Không tìm thấy thông tin phân công.");
        }

        // Đại diện tổ đã nghiệm thu → thành viên bấm sau nhận thông báo rõ ràng thay vì lỗi trạng thái
        if (assignment.Status == DispatchAssignmentStatus.Completed && assignment.CompletionReport != null)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.ConflictResult("Nhiệm vụ này đã được một thành viên trong tổ nghiệm thu. Không cần nộp lại.");
        }

        if (!assignment.IsActive)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult(LocalizationHelper.Localize("Nhiệm vụ đã ở trạng thái '{0}', không thể nghiệm thu.", assignment.Status));
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

        // Tổ nhận lệnh (lưu trên lượt lúc phát lệnh; một tổ có thể nhiều xe)
        var crewTeam = CrewTeamOf(assignment, unit) is Guid teamId ? await _teamRepository.GetByIdAsync(teamId, cancellationToken) : null;

        if (isCommandCenter && CommandCenterScopeError(rescuer!, incident, unit) is { } resolveScopeError)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.ForbiddenResult(resolveScopeError);
        }

        if (!isCommandCenter)
        {
            if (unit == null || rescuer?.StationId != unit.StationId)
            {
                return ApiResponseDto<DispatchAssignmentResponseDto>.ForbiddenResult("Bạn chỉ được nghiệm thu nhiệm vụ của xe thuộc trạm mình.");
            }

            // Xe đã có tổ phụ trách → chỉ thành viên tổ đó; xe chưa gán tổ → nhân viên trạm
            if (crewTeam != null && crewTeam.Members.All(m => m.StaffId != rescuerUserId))
            {
                return ApiResponseDto<DispatchAssignmentResponseDto>.ForbiddenResult(
                    LocalizationHelper.Localize("Chỉ thành viên tổ \"{0}\" phụ trách xe {1} được nghiệm thu nhiệm vụ này.", crewTeam.Name, unit.PlateNumber));
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

        // Chữ ký đại diện tổ (bắt buộc): chỉ nhận ảnh do hệ thống upload, không nhận dataURL / link ngoài
        var signatureUrl = dto.SignatureUrl?.Trim();
        if (string.IsNullOrEmpty(signatureUrl))
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult(SignatureRequiredMessage);
        }
        if (!MediaUrlPolicy.IsTrusted(signatureUrl, _configuration))
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult("Ảnh chữ ký không hợp lệ. Vui lòng tải chữ ký lên qua hệ thống trước khi nộp báo cáo.");
        }

        // 1. Các lượt được hoàn tất: mọi xe của tổ đang làm sự cố này (kể cả xe chưa tới), hoặc riêng xe này nếu chưa gán tổ
        var siblingAssignments = await _assignmentRepository.GetByIncidentIdAsync(incident.Id);
        var toComplete = crewTeam == null
            ? new List<DispatchAssignment> { assignment }
            : siblingAssignments.Where(a => a.IsActive && CrewTeamOf(a, a.RescueUnit) == crewTeam.Id).ToList();
        if (!toComplete.Contains(assignment))
        {
            toComplete.Insert(0, assignment);
        }

        // FR-RS-03 / BR-25 / BR-26: một báo cáo (ảnh, chữ ký đại diện, bàn giao) dùng chung cho các xe của tổ
        var report = new CompletionReport
        {
            IncidentId = incident.Id,
            StationTeamId = crewTeam?.Id,
            StationTeam = crewTeam,
            SubmittedByUserId = rescuerUserId,
            ActionsSummary = dto.ResolutionSummary.Trim(),
            VictimsAssistedCount = dto.VictimsAssistedCount ?? 0,
            EquipmentUsed = dto.EquipmentUsed?.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).ToList() ?? new List<string>(),
            HandoverEntity = dto.HandoverEntity?.Trim(),
            HandoverContactName = dto.HandoverContactName?.Trim(),
            HandoverContactPhone = dto.HandoverContactPhone?.Trim(),
            HandoverNotes = dto.HandoverNotes?.Trim(),
            SignatureUrl = signatureUrl,
            SignerName = rescuer?.FullName,
            SignedAt = now,
            SubmittedAt = now
        };
        foreach (var url in evidenceUrls)
        {
            report.MediaItems.Add(new CompletionReportMedia { MediaUrl = url, MediaType = MediaType.Photo });
        }
        await _assignmentRepository.AddCompletionReportAsync(report);

        var dispatchSettings = await _settingsService.GetDispatchSettingsAsync(cancellationToken);
        var completedPlates = new List<string>();
        var changedUnits = new List<RescueUnit>();
        foreach (var item in toComplete)
        {
            var oldStatus = item.Status;
            item.Status = DispatchAssignmentStatus.Completed;
            item.CompletedAt = now;
            item.UpdatedAt = now;
            item.CompletionReportId = report.Id;
            item.CompletionReport = report;

            await _assignmentRepository.AddStatusHistoryAsync(new AssignmentStatusHistory
            {
                AssignmentId = item.Id,
                OldStatus = oldStatus,
                NewStatus = DispatchAssignmentStatus.Completed,
                ChangedByUserId = rescuerUserId,
                Notes = crewTeam == null ? "Nộp báo cáo nghiệm thu" : $"Tổ {crewTeam.Name} nộp báo cáo nghiệm thu (đại diện: {rescuer?.FullName})",
                ChangedAt = now
            });
            await _assignmentRepository.UpdateAsync(item);

            // 2. BR-27: xe hồi sức (CoolDown) trước khi quay lại nhận lệnh mới
            var itemUnit = item.Id == assignment.Id ? unit : item.RescueUnit ?? await _rescueUnitRepository.GetByIdAsync(item.RescueUnitId);
            if (itemUnit != null)
            {
                itemUnit.Status = RescueUnitStatus.CoolDown;
                itemUnit.CoolDownUntil = now.AddMinutes(dispatchSettings.RecoveryMinutes);
                itemUnit.UpdatedAt = now;
                await SaveUnitAsync(itemUnit, changedUnits, cancellationToken);
                completedPlates.Add(itemUnit.PlateNumber);
            }
        }

        // 3. BR-14: sự cố chỉ đóng khi mọi xe còn hiệu lực đều đã nghiệm thu (tổ nào cũng phải tự nộp)
        var completedIds = toComplete.Select(a => a.Id).ToHashSet();
        var pendingPlates = siblingAssignments
            .Where(a => !completedIds.Contains(a.Id) && a.IsActive)
            .Select(a => a.RescueUnit?.PlateNumber ?? a.RescueUnitId.ToString())
            .ToList();
        var remainingActive = pendingPlates.Count;
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

        var who = crewTeam == null
            ? $"Xe {string.Join(", ", completedPlates)}"
            : $"Tổ {crewTeam.Name} (xe {string.Join(", ", completedPlates)})";
        await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
        {
            IncidentId = incident.Id,
            ActorId = rescuerUserId,
            ActorRole = actorRole,
            Action = incidentClosed ? AuditAction.Completed : AuditAction.StatusChanged,
            Description = incidentClosed
                ? $"{who} nghiệm thu hiện trường và hoàn tất sự cố"
                : $"{who} nghiệm thu xong, chờ các xe còn lại nghiệm thu: {string.Join(", ", pendingPlates)}",
            NewStateJson = JsonSerializer.Serialize(new
            {
                completionReportId = report.Id,
                stationTeamId = crewTeam?.Id,
                completedAssignmentIds = completedIds,
                incidentClosed,
                remainingActiveAssignments = remainingActive,
                resolutionTimeMs = incident.ResolutionTimeMs,
                closedBy = incidentClosed ? rescuerUserId : (Guid?)null,
                evidencePhotosCount = evidenceUrls.Count,
                victimsAssisted = report.VictimsAssistedCount
            })
        }, cancellationToken);

        await _incidentRepository.SaveChangesAsync(cancellationToken);
        await lockScope.CommitAsync(cancellationToken);
        await PublishUnitsAsync(changedUnits, cancellationToken);
        if (!incidentClosed)
        {
            await _fieldRequestService.CancelOpenAsync(incident.Id, toComplete.Select(a => a.Id).ToList(), "Tự hủy: tổ đã nghiệm thu.", cancellationToken);
        }

        _logger.LogInformation(
            "Nghiệm thu {Count} xe ({Plates}) của sự cố {IncidentId}. Sự cố {State} (T_Response={Ms}ms)",
            toComplete.Count, string.Join(", ", completedPlates), incident.Id, incidentClosed ? "đã đóng" : $"còn {remainingActive} xe", incident.ResolutionTimeMs);

        // Bắn SignalR cho từng xe được hoàn tất
        DispatchAssignmentResponseDto? assignmentDto = null;
        var crewIds = await CrewIdsAsync(unit, cancellationToken, CrewTeamOf(assignment, unit));
        foreach (var item in toComplete)
        {
            var itemDto = MapToAssignmentDto(item, incident, item.Id == assignment.Id ? unit : item.RescueUnit);
            await _realtimeNotifier.AssignmentStatusChangedAsync(itemDto, crewIds, cancellationToken);
            if (item.Id == assignment.Id)
            {
                assignmentDto = itemDto;
            }
        }
        await _realtimeNotifier.IncidentUpdatedAsync(MapIncidentToSlimDto(incident), cancellationToken);

        // Đại diện nghiệm thu = cả tổ hoàn thành: báo các thành viên còn lại để app đóng nhiệm vụ (kể cả khi đang offline)
        var otherCrew = crewTeam?.Members.Select(m => m.StaffId).Where(staffId => staffId != rescuerUserId).Distinct().ToList();
        if (crewTeam != null && otherCrew is { Count: > 0 })
        {
            await _notificationService.NotifyUsersAsync(otherCrew, new NotificationRequest
            {
                Type = NotificationType.StatusUpdate,
                Title = "TỔ ĐÃ NGHIỆM THU NHIỆM VỤ",
                Message = new LocalizedText("{0} đã chụp ảnh và ký xác nhận thay tổ {1}, hoàn tất nhiệm vụ tại {2} (xe {3}).",
                    rescuer?.FullName ?? "Thành viên tổ", crewTeam.Name, incident.LocationAddress, string.Join(", ", completedPlates)),
                IncidentId = incident.Id
            }, cancellationToken);
        }

        if (incidentClosed)
        {
            await AfterIncidentClosedAsync(incident, rescuerUserId, cancellationToken);
        }

        return ApiResponseDto<DispatchAssignmentResponseDto>.SuccessResult(
            assignmentDto!,
            incidentClosed
                ? "Nghiệm thu hiện trường thành công. Hồ sơ sự cố đã được đóng và ghi nhận KPI phản hồi."
                : LocalizationHelper.Localize("Nghiệm thu thành công cho xe {0}. Sự cố vẫn mở, chờ tổ của các xe còn lại chụp ảnh và ký nghiệm thu: {1}.",
                    string.Join(", ", completedPlates), string.Join(", ", pendingPlates)));
    }

    public async Task<ApiResponseDto<CitizenTrackingResponseDto>> GetCitizenTrackingAsync(
        Guid incidentId,
        CancellationToken cancellationToken = default)
        => await GetCitizenTrackingAsync(incidentId, false, cancellationToken);

    public async Task<ApiResponseDto<CitizenTrackingResponseDto>> GetCitizenTrackingAsync(
        Guid incidentId,
        bool onlyAcceptedUnits,
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
            IncidentCode = incident.Code,
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
        var active = assignments.Where(a => a.IsActive).ToList();
        // Người dân chỉ thấy xe khi tổ đã nhận lệnh: lượt còn Dispatched có thể bị tổ từ chối và đổi xe khác
        var shown = onlyAcceptedUnits
            ? active.Where(a => a.Status != DispatchAssignmentStatus.Dispatched).ToList()
            : active;
        if (active.Count == 0)
        {
            shown = assignments.Where(a => a.Status == DispatchAssignmentStatus.Completed).ToList();
        }

        if (shown.Count == 0 && active.Count > 0)
        {
            return ApiResponseDto<CitizenTrackingResponseDto>.SuccessResult(result,
                "Đã giao sự cố cho đội cứu hộ, đang chờ đội xác nhận nhận lệnh.");
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
            LocalizationHelper.Localize("Lấy thông tin lộ trình di chuyển của {0} xe cứu hộ thành công.", result.Units.Count));
    }

    /// <summary>
    /// Lưu xe và phát ReceiveRescueUnitStatus để live map / danh sách xe cập nhật ngay
    /// </summary>
    /// <summary>
    /// Quy tắc loại xe của loại sự cố (bảng IncidentTypeUnitRules); chưa có danh mục / chưa biết loại → danh sách rỗng
    /// </summary>
    public async Task<IReadOnlyList<IncidentTypeUnitRule>> GetUnitRulesAsync(Incident incident, CancellationToken cancellationToken)
    {
        var code = DispatchScoring.IncidentTypeCodeFor(incident);
        if (_typeCatalog == null || code == null) return Array.Empty<IncidentTypeUnitRule>();
        var rules = await _typeCatalog.GetUnitRulesAsync(code, cancellationToken) ?? (IReadOnlyList<IncidentTypeUnitRule>)Array.Empty<IncidentTypeUnitRule>();
        // Loại chi tiết thuộc nhóm Khác chưa có kế hoạch riêng → dùng kế hoạch của loại "Khác" chung (nếu Admin đã cấu hình)
        const string genericOther = nameof(IncidentEventType.Other);
        if (rules.Count == 0 && code != genericOther && incident.EventType is IncidentEventType.Other or IncidentEventType.None)
        {
            rules = await _typeCatalog.GetUnitRulesAsync(genericOther, cancellationToken) ?? rules;
        }
        return rules;
    }

    private async Task<IReadOnlySet<string>?> MatchingUnitTypesAsync(Incident incident, CancellationToken cancellationToken)
        => DispatchScoring.MatchingUnitTypes(
            await GetUnitRulesAsync(incident, cancellationToken),
            incident.EventType != IncidentEventType.None
                ? incident.EventType
                : incident.AiClassification?.EventType ?? IncidentEventType.None,
            incident.AiClassification?.HazardTags ?? new List<string>(),
            $"{incident.Title} {incident.Description}",
            incident.Severity);

    /// <summary>
    /// Đội nhận lệnh của xe (CrewScope): tổ phụ trách xe, hoặc nhân viên cứu hộ của trạm nếu xe chưa gán tổ
    /// </summary>
    private async Task<IReadOnlyCollection<Guid>> CrewIdsAsync(RescueUnit? unit, CancellationToken cancellationToken, Guid? crewTeamId = null)
    {
        if (unit == null) return Array.Empty<Guid>();
        return await _notificationService.GetCrewUserIdsAsync(unit.StationId, crewTeamId ?? unit.StationTeamId, cancellationToken)
               ?? (IReadOnlyCollection<Guid>)Array.Empty<Guid>();
    }

    /// <summary>
    /// Tổ chịu trách nhiệm một lượt: tổ lưu trên lượt lúc phát lệnh (không đổi theo việc xe bị chuyển tổ sau đó);
    /// lượt cũ chưa lưu tổ → tổ hiện tại của xe
    /// </summary>
    private static Guid? CrewTeamOf(DispatchAssignment assignment, RescueUnit? unit)
        => assignment.StationTeamId ?? unit?.StationTeamId;

    /// <summary>
    /// Nhân viên cứu hộ chỉ thao tác nhiệm vụ của tổ mình (lượt không có tổ: nhân viên cùng trạm với xe)
    /// </summary>
    private async Task<bool> IsCrewOfAsync(User? staff, DispatchAssignment assignment, RescueUnit? unit, CancellationToken cancellationToken)
    {
        if (staff == null || unit == null || !staff.StationId.HasValue || staff.StationId != unit.StationId) return false;
        if (CrewTeamOf(assignment, unit) is not Guid crewTeamId) return true;
        return await _userRepository.GetTeamIdAsync(staff.Id, cancellationToken) == crewTeamId;
    }

    /// <summary>
    /// Operator chỉ thao tác lượt của sự cố trạm mình (hoặc chưa gán trạm) hoặc của xe trạm mình đi hỗ trợ; Admin mọi lượt
    /// </summary>
    private static string? CommandCenterScopeError(User actor, Incident? incident, RescueUnit? unit)
        => OperatorScope.CheckAssignment(actor.Role, actor.StationId, incident?.ResponsibleStationId, unit?.StationId);

    private static string CrewForbiddenMessage(RescueUnit? unit)
        => unit?.StationTeamId != null
            ? LocalizationHelper.Localize("Nhiệm vụ của xe {0} do tổ khác phụ trách.", unit.PlateNumber)
            : "Bạn chỉ được thao tác nhiệm vụ của xe thuộc trạm mình.";

    /// <summary>
    /// Lưu trạng thái xe: chỉ ghi Status / CoolDownUntil / UpdatedAt, không ghi đè vị trí GPS mới hơn.
    /// Ghi trong transaction đang mở của request (cùng lượt điều phối); realtime phát sau khi commit qua PublishUnitsAsync
    /// </summary>
    private async Task SaveUnitAsync(RescueUnit unit, List<RescueUnit> publishAfterCommit, CancellationToken cancellationToken)
    {
        await _rescueUnitRepository.SaveStatusAsync(unit, cancellationToken);
        publishAfterCommit.Add(unit);
    }

    private async Task PublishUnitsAsync(IEnumerable<RescueUnit> units, CancellationToken cancellationToken)
    {
        foreach (var unit in units.DistinctBy(u => u.Id))
        {
            await PublishUnitStatusAsync(unit, null, cancellationToken);
        }
    }

    private Task PublishUnitStatusAsync(RescueUnit unit, RescueUnitStatus? previous, CancellationToken cancellationToken)
        => _realtimeNotifier.RescueUnitStatusChangedAsync(new EmergencyDispatch.Application.DTOs.Incident.RescueUnitStatusEventDto
        {
            UnitType = unit.UnitType,
            Latitude = unit.CurrentLat,
            Longitude = unit.CurrentLng,
            RescueUnitId = unit.Id,
            PlateNumber = unit.PlateNumber,
            StationId = unit.StationId,
            Status = unit.Status,
            PreviousStatus = previous,
            CoolDownUntil = unit.CoolDownUntil,
            ChangedAt = DateTime.UtcNow
        }, cancellationToken);

    #region Private Helper Methods

    private enum TeamProblem { None, NoTeam, NoStaff, Busy }

    /// <summary>
    /// Tình trạng các tổ khi điều phối: số nhân viên đang hoạt động và tổ đang bận
    /// (có lượt điều phối đang chạy, hoặc xe của tổ đang đi / ở hiện trường / quay về trạm)
    /// </summary>
    private sealed class TeamPoolSnapshot
    {
        public required IReadOnlyDictionary<Guid, TeamDispatchInfo> Teams { get; init; }
        public required IReadOnlySet<Guid> BusyTeams { get; init; }

        public TeamProblem Problem(RescueUnit unit)
        {
            if (unit.StationTeamId is not Guid teamId) return TeamProblem.NoTeam;
            if (!Teams.TryGetValue(teamId, out var info) || info.ActiveStaffCount == 0) return TeamProblem.NoStaff;
            return BusyTeams.Contains(teamId) ? TeamProblem.Busy : TeamProblem.None;
        }

        public string NameOf(Guid? teamId)
            => teamId is Guid id && Teams.TryGetValue(id, out var info) ? info.Name : teamId?.ToString() ?? "-";
    }

    private async Task<TeamPoolSnapshot> LoadTeamPoolAsync(
        IReadOnlyList<RescueUnit> allUnits, IReadOnlyList<DispatchAssignment> activeAssignments, CancellationToken cancellationToken)
    {
        var teamOfUnit = allUnits.ToDictionary(u => u.Id, u => u.StationTeamId);
        var busy = activeAssignments
            .Select(a => a.StationTeamId ?? (teamOfUnit.TryGetValue(a.RescueUnitId, out var t) ? t : null))
            .OfType<Guid>()
            .ToHashSet();
        busy.UnionWith(allUnits
            .Where(u => u.Status is RescueUnitStatus.Dispatched or RescueUnitStatus.OnScene or RescueUnitStatus.Returning)
            .Select(u => u.StationTeamId)
            .OfType<Guid>());

        var teamIds = allUnits.Select(u => u.StationTeamId).OfType<Guid>().Distinct().ToList();
        var infos = await _teamRepository.GetDispatchInfoAsync(teamIds, cancellationToken) ?? Array.Empty<TeamDispatchInfo>();
        return new TeamPoolSnapshot
        {
            Teams = infos.GroupBy(i => i.TeamId).ToDictionary(g => g.Key, g => g.First()),
            BusyTeams = busy
        };
    }

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

    internal static DispatchAssignmentResponseDto MapToAssignmentDto(
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
            IncidentCode = incident?.Code ?? string.Empty,
            ResponsibleStationId = incident?.ResponsibleStationId,
            IncidentTitle = incident?.Title ?? string.Empty,
            IncidentAddress = incident?.LocationAddress ?? string.Empty,
            IncidentLat = incident?.Latitude ?? 0,
            IncidentLng = incident?.Longitude ?? 0,
            IncidentSeverity = incident?.Severity ?? SeverityLevel.Unclassified,
            RescueUnitId = assignment.RescueUnitId,
            RescueUnit = unitDto,
            StationTeamId = assignment.StationTeamId ?? unit?.StationTeamId,
            StationTeamName = assignment.StationTeam?.Name,
            StationTeamDeleted = assignment.StationTeam?.IsDeleted == true,
            RescueUnitDeleted = unit?.IsDeleted == true,
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
            CompletionReport = assignment.CompletionReport is not { IsDeleted: false } ? null : new CompletionReportDto
            {
                Id = assignment.CompletionReport.Id,
                StationTeamId = assignment.CompletionReport.StationTeamId,
                StationTeamName = assignment.CompletionReport.StationTeam?.Name,
                StationTeamDeleted = assignment.CompletionReport.StationTeam?.IsDeleted == true,
                ActionsSummary = assignment.CompletionReport.ActionsSummary,
                VictimsAssistedCount = assignment.CompletionReport.VictimsAssistedCount,
                EquipmentUsed = assignment.CompletionReport.EquipmentUsed,
                EvidenceMediaUrls = assignment.CompletionReport.MediaItems.Select(m => m.MediaUrl).ToList(),
                HandoverEntity = assignment.CompletionReport.HandoverEntity,
                HandoverContactName = assignment.CompletionReport.HandoverContactName,
                HandoverContactPhone = assignment.CompletionReport.HandoverContactPhone,
                HandoverNotes = assignment.CompletionReport.HandoverNotes,
                SignatureUrl = assignment.CompletionReport.SignatureUrl,
                SignerName = assignment.CompletionReport.SignerName,
                SignedAt = assignment.CompletionReport.SignedAt,
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
            Code = incident.Code,
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
