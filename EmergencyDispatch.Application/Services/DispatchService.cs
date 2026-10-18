using System.Text.Json;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Dispatch;
using EmergencyDispatch.Application.DTOs.RescueUnit;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Application.Services;

public class DispatchService : IDispatchService
{
    private readonly IDispatchAssignmentRepository _assignmentRepository;
    private readonly IRescueUnitRepository _rescueUnitRepository;
    private readonly IIncidentRepository _incidentRepository;
    private readonly IUserRepository _userRepository;
    private readonly IRealtimeNotifier _realtimeNotifier;
    private readonly ILogger<DispatchService> _logger;

    public DispatchService(
        IDispatchAssignmentRepository assignmentRepository,
        IRescueUnitRepository rescueUnitRepository,
        IIncidentRepository incidentRepository,
        IUserRepository userRepository,
        IRealtimeNotifier realtimeNotifier,
        ILogger<DispatchService> logger)
    {
        _assignmentRepository = assignmentRepository;
        _rescueUnitRepository = rescueUnitRepository;
        _incidentRepository = incidentRepository;
        _userRepository = userRepository;
        _realtimeNotifier = realtimeNotifier;
        _logger = logger;
    }

    public async Task<ApiResponseDto<List<RecommendedUnitResponseDto>>> GetRecommendationsAsync(
        Guid incidentId,
        DispatchAlgorithm algorithm = DispatchAlgorithm.NearestAvailable,
        RescueUnitType? unitType = null,
        double maxDistanceKm = 15.0,
        CancellationToken cancellationToken = default)
    {
        var incident = await _incidentRepository.GetIncidentWithDetailsAsync(incidentId, cancellationToken);
        if (incident == null)
        {
            return ApiResponseDto<List<RecommendedUnitResponseDto>>.FailureResult("Không tìm thấy sự cố cần điều phối.");
        }

        // Tự động suy luận loại phương tiện cần ưu tiên nếu chưa chỉ định rõ
        var targetType = unitType ?? DeduceRecommendedUnitType(incident);

        // Lấy tất cả phương tiện kèm thông tin trạm
        var allUnits = await _rescueUnitRepository.GetAllWithStationAsync();

        var candidates = new List<RecommendedUnitResponseDto>();

        foreach (var unit in allUnits)
        {
            // Bỏ qua xe bảo trì hoặc đã bị xóa
            if (unit.Status == RescueUnitStatus.Maintenance || unit.IsDeleted)
            {
                continue;
            }

            // Nếu chỉ định loại xe cụ thể, lọc đúng loại
            if (targetType.HasValue && unit.UnitType != targetType.Value)
            {
                continue;
            }

            var distKm = CalculateDistanceKm(incident.Latitude, incident.Longitude, unit.CurrentLat, unit.CurrentLng);
            if (distKm > maxDistanceKm)
            {
                continue;
            }

            var activeTasks = await CountActiveTasksForUnitAsync(unit.Id);
            var etaMinutes = EstimateEtaMinutes(distKm);

            double score;
            string reason;

            if (algorithm == DispatchAlgorithm.NearestAvailable)
            {
                // Heuristic 1: Ưu tiên khoảng cách ngắn nhất, chỉ chọn Available hoặc Returning
                var availabilityMultiplier = unit.Status == RescueUnitStatus.Available ? 1.0 : 0.7;
                score = Math.Round((100.0 / (1.0 + distKm)) * availabilityMultiplier, 2);
                reason = $"Khoảng cách: {distKm:F2} km ({etaMinutes:F0} phút). Trạng thái: {unit.Status}.";
            }
            else
            {
                // Heuristic 2 (RQ2: Load-Aware Dispatch): Cân bằng tải kết hợp cự ly
                // Điểm = (Điểm cự ly cơ sở) - (Hệ số phạt tải công việc đang xử lý)
                var distanceScore = 100.0 / (1.0 + distKm);
                var loadPenalty = activeTasks * 18.0;

                // Nếu xe đang rảnh tuyệt đối (Available) cộng điểm thưởng
                var statusBonus = unit.Status == RescueUnitStatus.Available ? 10.0 : 0.0;

                score = Math.Round(Math.Max(0.0, distanceScore - loadPenalty + statusBonus), 2);
                reason = $"Load-Aware: cự ly {distKm:F2} km, {activeTasks} nhiệm vụ hiện hành. Điểm tối ưu: {score}.";
            }

            candidates.Add(new RecommendedUnitResponseDto
            {
                RescueUnitId = unit.Id,
                PlateNumber = unit.PlateNumber,
                UnitType = unit.UnitType,
                Status = unit.Status,
                StationId = unit.StationId,
                StationName = unit.Station?.Name ?? "Trạm chưa xác định",
                CurrentLat = unit.CurrentLat,
                CurrentLng = unit.CurrentLng,
                DistanceKm = distKm,
                EstimatedEtaMinutes = etaMinutes,
                ActiveTasksCount = activeTasks,
                Score = score,
                AlgorithmUsed = algorithm,
                RecommendationReason = reason
            });
        }

        // Sắp xếp: Với NearestAvailable sắp xếp theo Distance tăng dần, LoadAware sắp xếp theo Score giảm dần
        var ranked = algorithm == DispatchAlgorithm.NearestAvailable
            ? candidates.OrderBy(c => c.DistanceKm).ThenBy(c => c.ActiveTasksCount).ToList()
            : candidates.OrderByDescending(c => c.Score).ThenBy(c => c.DistanceKm).ToList();

        return ApiResponseDto<List<RecommendedUnitResponseDto>>.SuccessResult(
            ranked,
            $"Đã tạo danh sách đề xuất thành công theo thuật toán '{algorithm}' (Tìm thấy {ranked.Count} phương tiện).");
    }

    public async Task<ApiResponseDto<List<RecommendedUnitResponseDto>>> GetAvailableUnitsNearbyAsync(
        Guid incidentId,
        double maxDistanceKm = 15.0,
        CancellationToken cancellationToken = default)
    {
        return await GetRecommendationsAsync(
            incidentId,
            DispatchAlgorithm.NearestAvailable,
            null,
            maxDistanceKm,
            cancellationToken);
    }

    public async Task<ApiResponseDto<DispatchAssignmentResponseDto>> AssignUnitAsync(
        AssignDispatchDto dto,
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        var incident = await _incidentRepository.GetIncidentWithDetailsAsync(dto.IncidentId, cancellationToken);
        if (incident == null)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult("Không tìm thấy sự cố cần điều phối.");
        }

        if (incident.Status == IncidentStatus.Completed || incident.Status == IncidentStatus.Cancelled)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult($"Không thể điều phối cho sự cố đã ở trạng thái '{incident.Status}'.");
        }

        var unit = await _rescueUnitRepository.GetByIdWithDetailsAsync(dto.RescueUnitId);
        if (unit == null)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult("Không tìm thấy phương tiện cứu hộ yêu cầu.");
        }

        if (unit.Status == RescueUnitStatus.Maintenance)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult("Phương tiện hiện đang trong lịch bảo trì, không thể xuất xe.");
        }

        // Kiểm tra xem xe đã được gán vào sự cố này chưa (tránh phát lệnh trùng)
        var existing = incident.DispatchAssignments
            .FirstOrDefault(d => d.RescueUnitId == unit.Id &&
                                 d.Status != DispatchAssignmentStatus.Completed &&
                                 d.Status != DispatchAssignmentStatus.Cancelled);
        if (existing != null)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult($"Phương tiện {unit.PlateNumber} đã được phát lệnh cho sự cố này trước đó.");
        }

        var assignment = new DispatchAssignment
        {
            IncidentId = incident.Id,
            RescueUnitId = unit.Id,
            DispatchedAt = DateTime.UtcNow,
            Status = DispatchAssignmentStatus.Dispatched,
            Notes = dto.Notes?.Trim()
        };

        // Cập nhật trạng thái xe và sự cố
        unit.Status = RescueUnitStatus.Dispatched;
        unit.UpdatedAt = DateTime.UtcNow;

        var oldIncidentStatus = incident.Status;
        incident.Status = IncidentStatus.Dispatched;
        incident.UpdatedAt = DateTime.UtcNow;

        // Telemetry RQ3: Thời gian từ khi tạo sự cố đến khi phát lệnh điều phối (Dispatch Time)
        incident.DispatchTimeMs = (long)(DateTime.UtcNow - incident.CreatedAt).TotalMilliseconds;

        // Ghi lịch sử và Audit Trail
        await _incidentRepository.AddStatusHistoryAsync(new IncidentStatusHistory
        {
            IncidentId = incident.Id,
            OldStatus = oldIncidentStatus,
            NewStatus = IncidentStatus.Dispatched,
            ChangedByUserId = operatorId,
            ChangedByRole = UserRole.Operator.ToString(),
            Notes = $"Phát lệnh điều phối phương tiện {unit.PlateNumber} ({unit.UnitType})."
        }, cancellationToken);

        await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
        {
            IncidentId = incident.Id,
            ActorId = operatorId,
            ActorRole = UserRole.Operator.ToString(),
            Action = AuditAction.Dispatched,
            Description = $"Điều phối phương tiện {unit.PlateNumber} (Mã: {unit.Id})",
            NewStateJson = JsonSerializer.Serialize(new
            {
                unitId = unit.Id,
                plateNumber = unit.PlateNumber,
                dispatchTimeMs = incident.DispatchTimeMs
            })
        }, cancellationToken);

        await _assignmentRepository.AddAsync(assignment);
        await _rescueUnitRepository.UpdateAsync(unit);
        await _incidentRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Operator {OpId} đã điều phối xe {Plate} tới sự cố {IncId} (DispatchTime={Ms}ms)",
            operatorId, unit.PlateNumber, incident.Id, incident.DispatchTimeMs);

        var assignmentDto = MapToAssignmentDto(assignment, incident, unit);

        // Phát SignalR thông báo
        await _realtimeNotifier.JobAssignedAsync(assignmentDto, cancellationToken);
        await _realtimeNotifier.IncidentUpdatedAsync(MapIncidentToSlimDto(incident), cancellationToken);

        return ApiResponseDto<DispatchAssignmentResponseDto>.SuccessResult(
            assignmentDto,
            $"Đã phát lệnh điều phối phương tiện {unit.PlateNumber} thành công.");
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

        if (assignment.Status == DispatchAssignmentStatus.Completed || assignment.Status == DispatchAssignmentStatus.Cancelled)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult($"Nhiệm vụ đã ở trạng thái '{assignment.Status}', không thể thay đổi.");
        }

        var oldStatus = assignment.Status;
        assignment.Status = dto.Status;
        assignment.UpdatedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(dto.Notes))
        {
            assignment.Notes = dto.Notes.Trim();
        }

        var rescuer = await _userRepository.GetByIdAsync(rescuerUserId);
        var actorName = rescuer?.FullName ?? $"Rescuer {rescuerUserId}";

        var incident = assignment.Incident ?? await _incidentRepository.GetByIdAsync(assignment.IncidentId);
        var unit = assignment.RescueUnit ?? await _rescueUnitRepository.GetByIdAsync(assignment.RescueUnitId);

        switch (dto.Status)
        {
            case DispatchAssignmentStatus.Accepted:
                assignment.AcceptedAt = DateTime.UtcNow;
                break;

            case DispatchAssignmentStatus.EnRoute:
                if (unit != null)
                {
                    unit.Status = RescueUnitStatus.Dispatched;
                    unit.UpdatedAt = DateTime.UtcNow;
                    await _rescueUnitRepository.UpdateAsync(unit);
                }
                break;

            case DispatchAssignmentStatus.OnScene:
                assignment.ArrivedAt = DateTime.UtcNow;
                if (unit != null)
                {
                    unit.Status = RescueUnitStatus.OnScene;
                    unit.UpdatedAt = DateTime.UtcNow;
                    await _rescueUnitRepository.UpdateAsync(unit);
                }
                if (incident != null && incident.Status != IncidentStatus.InProgress)
                {
                    incident.Status = IncidentStatus.InProgress;
                    incident.UpdatedAt = DateTime.UtcNow;
                    await _incidentRepository.UpdateAsync(incident);
                }
                break;
        }

        await _assignmentRepository.UpdateAsync(assignment);

        if (incident != null)
        {
            await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
            {
                IncidentId = incident.Id,
                ActorId = rescuerUserId,
                ActorRole = UserRole.RescueStaff.ToString(),
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

        if (assignment.Status == DispatchAssignmentStatus.Completed)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult("Nhiệm vụ này đã được hoàn tất trước đó.");
        }

        var incident = assignment.Incident ?? await _incidentRepository.GetByIdAsync(assignment.IncidentId);
        if (incident == null)
        {
            return ApiResponseDto<DispatchAssignmentResponseDto>.FailureResult("Không tìm thấy thông tin sự cố.");
        }

        var unit = assignment.RescueUnit ?? await _rescueUnitRepository.GetByIdAsync(assignment.RescueUnitId);

        // 1. Hoàn tất lượt phân công
        assignment.Status = DispatchAssignmentStatus.Completed;
        assignment.CompletedAt = DateTime.UtcNow;
        assignment.UpdatedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(dto.HandoverNotes))
        {
            assignment.Notes = $"[BÀN GIAO]: {dto.HandoverNotes.Trim()}";
        }
        await _assignmentRepository.UpdateAsync(assignment);

        // 2. Trả phương tiện về trạng thái Sẵn sàng (Available)
        if (unit != null)
        {
            unit.Status = RescueUnitStatus.Available;
            unit.UpdatedAt = DateTime.UtcNow;
            await _rescueUnitRepository.UpdateAsync(unit);
        }

        // 3. Đóng hồ sơ sự cố (Close Incident & Telemetry RQ3: T_Response)
        var oldIncidentStatus = incident.Status;
        incident.Close(rescuerUserId, dto.ResolutionSummary.Trim());
        incident.ResolutionTimeMs = (long)(DateTime.UtcNow - incident.CreatedAt).TotalMilliseconds;

        // Lưu ảnh bằng chứng nghiệm thu hiện trường vào IncidentMedia
        if (dto.EvidenceMediaUrls != null && dto.EvidenceMediaUrls.Any())
        {
            foreach (var url in dto.EvidenceMediaUrls.Where(u => !string.IsNullOrWhiteSpace(u)))
            {
                incident.MediaItems.Add(new IncidentMedia
                {
                    IncidentId = incident.Id,
                    MediaUrl = url.Trim(),
                    MediaType = MediaType.Photo,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        // 4. Ghi lịch sử chuyển trạng thái và Audit Trail
        await _incidentRepository.AddStatusHistoryAsync(new IncidentStatusHistory
        {
            IncidentId = incident.Id,
            OldStatus = oldIncidentStatus,
            NewStatus = IncidentStatus.Completed,
            ChangedByUserId = rescuerUserId,
            ChangedByRole = UserRole.RescueStaff.ToString(),
            Notes = $"Nghiệm thu đóng sự cố: {dto.ResolutionSummary.Trim()}"
        }, cancellationToken);

        await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
        {
            IncidentId = incident.Id,
            ActorId = rescuerUserId,
            ActorRole = UserRole.RescueStaff.ToString(),
            Action = AuditAction.Completed,
            Description = "Nghiệm thu hiện trường và hoàn tất sự cố",
            NewStateJson = JsonSerializer.Serialize(new
            {
                resolutionTimeMs = incident.ResolutionTimeMs,
                closedBy = rescuerUserId,
                evidencePhotosCount = dto.EvidenceMediaUrls?.Count ?? 0
            })
        }, cancellationToken);

        await _incidentRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Sự cố {IncidentId} đã hoàn tất xử lý và đóng hồ sơ (T_Response={Ms}ms)",
            incident.Id, incident.ResolutionTimeMs);

        var assignmentDto = MapToAssignmentDto(assignment, incident, unit);

        // Bắn SignalR
        await _realtimeNotifier.AssignmentStatusChangedAsync(assignmentDto, cancellationToken);
        await _realtimeNotifier.IncidentUpdatedAsync(MapIncidentToSlimDto(incident), cancellationToken);

        return ApiResponseDto<DispatchAssignmentResponseDto>.SuccessResult(
            assignmentDto,
            "Nghiệm thu hiện trường thành công. Hồ sơ sự cố đã được đóng và ghi nhận KPI phản hồi.");
    }

    #region Private Helper Methods

    private async Task<int> CountActiveTasksForUnitAsync(Guid unitId)
    {
        var assignments = await _assignmentRepository.GetByRescueUnitIdAsync(unitId);
        return assignments.Count(a => a.Status == DispatchAssignmentStatus.Dispatched ||
                                      a.Status == DispatchAssignmentStatus.Accepted ||
                                      a.Status == DispatchAssignmentStatus.EnRoute ||
                                      a.Status == DispatchAssignmentStatus.OnScene);
    }

    private static RescueUnitType? DeduceRecommendedUnitType(Incident incident)
    {
        var titleAndDesc = $"{incident.Title} {incident.Description}".ToLower();
        var hazardTags = incident.AiClassification?.HazardTags ?? new List<string>();
        var tagsStr = string.Join(" ", hazardTags).ToLower();

        if (tagsStr.Contains("fire") || tagsStr.Contains("smoke") || titleAndDesc.Contains("cháy") || titleAndDesc.Contains("hỏa hoạn"))
        {
            return RescueUnitType.FireTruck;
        }

        if (tagsStr.Contains("injury") || tagsStr.Contains("casualty") || tagsStr.Contains("medical") ||
            titleAndDesc.Contains("cấp cứu") || titleAndDesc.Contains("bị thương") || titleAndDesc.Contains("ngất xỉu"))
        {
            return RescueUnitType.Ambulance;
        }

        return null;
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
            Status = assignment.Status,
            Notes = assignment.Notes
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
            ReporterPhone = incident.ReporterPhone,
            VerifiedByUserId = incident.VerifiedByUserId,
            VerifiedAt = incident.VerifiedAt,
            OperatorNotes = incident.OperatorNotes,
            CreatedAt = incident.CreatedAt,
            UpdatedAt = incident.UpdatedAt
        };
    }

    #endregion
}
