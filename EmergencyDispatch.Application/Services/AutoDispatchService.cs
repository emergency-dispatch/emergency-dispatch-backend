using System.Text.Json;
using EmergencyDispatch.Application.DTOs.Dispatch;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.DTOs.Notification;
using EmergencyDispatch.Application.DTOs.Settings;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Application.Services;

public enum AutoDispatchOutcome
{
    Disabled,
    NotEligible,
    Dispatched,
    SkippedDuplicate,
    SkippedNoUnits,
    Failed
}

public interface IAutoDispatchService
{
    /// <summary>
    /// Gọi ngay sau khi AI phân tích xong một sự cố. Đủ điều kiện → hệ thống tự xác minh và điều xe;
    /// nghi trùng / hết xe → đánh dấu Skipped và báo Operator; không đủ điều kiện → không làm gì (Operator xử lý như thường)
    /// </summary>
    Task<AutoDispatchOutcome> TryAutoDispatchAsync(Guid incidentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// AI tự động điều phối (thay BR-01 cho ca đủ điều kiện): TriageVL đủ tin cậy (xác suất token ≥ ngưỡng) với sự cố
/// cháy nổ / tai nạn Level 1–3 → hệ thống xác minh thay Operator, chọn xe theo quy tắc loại xe (IncidentTypeUnitRules)
/// và giới hạn BR-14, rồi phát lệnh. Chống trùng: khóa khu vực + kiểm tra sự cố đã điều xe gần đó (không xét loại).
/// </summary>
public class AutoDispatchService : IAutoDispatchService
{
    public const string DispatchedNote = "AI tự điều phối: độ tin cậy đạt ngưỡng, hệ thống đã xác minh và điều xe.";
    public const string SuspectedDuplicateNote = "AI không tự điều phối: nghi trùng với sự cố đã được điều xe gần đó. Cần Điều phối viên kiểm tra.";
    public const string NoUnitsNote = "AI không tự điều phối: không còn xe phù hợp trong bán kính tìm kiếm. Cần Điều phối viên xử lý.";
    public const string AssignFailedNote = "AI không tự điều phối: phát lệnh thất bại (xe vừa bị điều đi nơi khác). Cần Điều phối viên xử lý.";

    private readonly IIncidentRepository _incidentRepository;
    private readonly IDispatchService _dispatchService;
    private readonly ISystemSettingsService _settingsService;
    private readonly INotificationService _notificationService;
    private readonly IUserRepository _userRepository;
    private readonly IRealtimeNotifier _realtimeNotifier;
    private readonly ILogger<AutoDispatchService> _logger;

    public AutoDispatchService(
        IIncidentRepository incidentRepository,
        IDispatchService dispatchService,
        ISystemSettingsService settingsService,
        INotificationService notificationService,
        IUserRepository userRepository,
        IRealtimeNotifier realtimeNotifier,
        ILogger<AutoDispatchService> logger)
    {
        _incidentRepository = incidentRepository;
        _dispatchService = dispatchService;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _userRepository = userRepository;
        _realtimeNotifier = realtimeNotifier;
        _logger = logger;
    }

    /// <summary>
    /// Điều kiện AI (hàm thuần): null = đủ điều kiện; ngược lại là lý do không xét tự điều phối
    /// </summary>
    public static string? IneligibleReason(Incident incident, DispatchSettingsDto settings, DateTime utcNow)
    {
        if (incident.Status != IncidentStatus.AiProcessed || incident.VerifiedAt != null) return "not-awaiting-verification";
        if (incident.DuplicateOfIncidentId != null) return "linked-report";
        if (incident.IsOutOfServiceArea) return "out-of-service-area";
        if (incident.Severity is not (SeverityLevel.Level1 or SeverityLevel.Level2 or SeverityLevel.Level3)) return "unclassified";
        if (incident.EventType is not (IncidentEventType.FireExplosion or IncidentEventType.TrafficAccident)) return "type-not-ai-recognizable";
        if (incident.ReportedEventType == IncidentEventType.Other) return "reporter-chose-other";
        if (incident.LockedByOperatorId.HasValue && incident.LockExpiresAt > utcNow) return "operator-handling";

        var ai = incident.AiClassification;
        if (ai is not { IsSuccess: true }) return "ai-failed";
        if (ai.ConfidenceSource?.StartsWith("logprob", StringComparison.Ordinal) != true) return "confidence-not-measured";
        if (ai.EventType != incident.EventType) return "ai-type-mismatch";
        if (!(ai.ConfidenceScore >= settings.AutoDispatchMinConfidence)) return "below-threshold";
        return null;
    }

    public async Task<AutoDispatchOutcome> TryAutoDispatchAsync(Guid incidentId, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetDispatchSettingsAsync(cancellationToken);
        if (!settings.AutoDispatchEnabled)
        {
            return AutoDispatchOutcome.Disabled;
        }

        var incident = await _incidentRepository.GetIncidentWithDetailsAsync(incidentId, cancellationToken);
        if (incident == null || IneligibleReason(incident, settings, DateTime.UtcNow) is { } early)
        {
            _logger.LogDebug("Auto-dispatch bỏ qua sự cố {IncidentId}: {Reason}", incidentId, incident == null ? "not-found" : IneligibleReason(incident, settings, DateTime.UtcNow));
            return AutoDispatchOutcome.NotEligible;
        }

        // Khóa khu vực: hai sự cố gần nhau không được tự điều phối song song (bước chống trùng phải thấy nhau)
        var areaLock = await _incidentRepository.BeginAreaLockAsync(incident.Latitude, incident.Longitude,
            settings.AutoDispatchDuplicateRadiusMeters, cancellationToken);
        var committed = false;
        try
        {
            await _incidentRepository.ReloadAsync(incident, cancellationToken);
            var now = DateTime.UtcNow;
            if (IneligibleReason(incident, settings, now) != null)
            {
                return AutoDispatchOutcome.NotEligible;
            }

            var duplicate = await FindDispatchedNeighbourAsync(incident, settings, now, cancellationToken);
            if (duplicate is { } dup)
            {
                await MarkSkippedAsync(incident, SuspectedDuplicateNote, dup.Incident.Id,
                    new { reason = "SuspectedDuplicate", duplicateOf = dup.Incident.Id, distanceMeters = Math.Round(dup.DistanceMeters, 1) }, cancellationToken);
                if (areaLock != null) await areaLock.CommitAsync(cancellationToken);
                committed = true;
                await AnnounceAsync(incident, cancellationToken);
                return AutoDispatchOutcome.SkippedDuplicate;
            }

            var unitIds = await PickUnitsAsync(incident, cancellationToken);
            if (unitIds.Count == 0)
            {
                await MarkSkippedAsync(incident, NoUnitsNote, null, new { reason = "NoUnits" }, cancellationToken);
                if (areaLock != null) await areaLock.CommitAsync(cancellationToken);
                committed = true;
                await AnnounceAsync(incident, cancellationToken);
                return AutoDispatchOutcome.SkippedNoUnits;
            }

            // Hệ thống xác minh thay Operator (lưu cùng lệnh điều phối trong một lần SaveChanges)
            var ai = incident.AiClassification!;
            var oldStatus = incident.Status;
            incident.Status = IncidentStatus.Verified;
            incident.VerifiedAt = now;
            incident.VerifiedByUserId = null;
            incident.IncidentTypeCode = DispatchScoring.IncidentTypeCodeFor(incident);
            incident.ResponseTimeMs = (long)(now - incident.CreatedAt).TotalMilliseconds;
            incident.AutoDispatchState = AutoDispatchState.Dispatched;
            incident.AutoDispatchNote = DispatchedNote;
            incident.SuspectedDuplicateOfIncidentId = null;
            incident.UpdatedAt = now;

            await _incidentRepository.AddStatusHistoryAsync(new IncidentStatusHistory
            {
                IncidentId = incident.Id,
                OldStatus = oldStatus,
                NewStatus = IncidentStatus.Verified,
                ChangedByRole = DispatchService.AutoDispatchActorRole,
                Notes = $"AI tự xác minh: {incident.EventType} {incident.Severity}, độ tin cậy {ai.ConfidenceScore:P1} (ngưỡng {settings.AutoDispatchMinConfidence:P0}).",
                ChangedAt = now
            }, cancellationToken);
            await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
            {
                IncidentId = incident.Id,
                ActorRole = DispatchService.AutoDispatchActorRole,
                Action = AuditAction.AutoDispatched,
                Description = $"AI tự điều phối {unitIds.Count} xe (độ tin cậy {ai.ConfidenceScore:P1}, ngưỡng {settings.AutoDispatchMinConfidence:P0})",
                NewStateJson = JsonSerializer.Serialize(new
                {
                    confidence = ai.ConfidenceScore,
                    confidenceSource = ai.ConfidenceSource,
                    threshold = settings.AutoDispatchMinConfidence,
                    eventType = incident.EventType.ToString(),
                    incidentTypeCode = incident.IncidentTypeCode,
                    severity = incident.Severity.ToString(),
                    model = ai.ModelName,
                    unitIds
                }),
                Timestamp = now
            }, cancellationToken);

            var result = await _dispatchService.AutoAssignAsync(new AssignDispatchDto
            {
                IncidentId = incident.Id,
                RescueUnitIds = unitIds,
                Algorithm = DispatchAlgorithm.NearestAvailable,
                Notes = "AI tự điều phối"
            }, cancellationToken);

            if (!result.Success)
            {
                // Phát lệnh lỗi: hoàn tác xác minh (rollback transaction + bỏ thay đổi đang theo dõi), trả về Operator
                _logger.LogWarning("Auto-dispatch sự cố {IncidentId} không phát lệnh được: {Message}", incident.Id, result.Message);
                if (areaLock != null) await areaLock.DisposeAsync();
                areaLock = null;
                _incidentRepository.ClearChangeTracker();
                var fresh = await _incidentRepository.GetIncidentWithDetailsAsync(incident.Id, cancellationToken);
                if (fresh != null && fresh.Status == IncidentStatus.AiProcessed)
                {
                    await MarkSkippedAsync(fresh, AssignFailedNote, null, new { reason = "AssignFailed", message = result.Message }, cancellationToken);
                    await AnnounceAsync(fresh, cancellationToken);
                }
                committed = true;
                return AutoDispatchOutcome.Failed;
            }

            if (areaLock != null) await areaLock.CommitAsync(cancellationToken);
            committed = true;
            _logger.LogInformation("AI tự điều phối sự cố {IncidentId}: {Count} xe, độ tin cậy {Confidence:P1}",
                incident.Id, unitIds.Count, ai.ConfidenceScore);
            await AnnounceAsync(incident, cancellationToken, string.Join(", ", result.Data?.Select(d => d.RescueUnit?.PlateNumber) ?? Array.Empty<string?>()));
            return AutoDispatchOutcome.Dispatched;
        }
        finally
        {
            if (areaLock != null)
            {
                if (!committed) _logger.LogDebug("Auto-dispatch sự cố {IncidentId}: rollback khóa khu vực.", incidentId);
                await areaLock.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Sự cố khác đã được điều xe (hoặc AI vừa tự điều) trong bán kính và khung thời gian, không xét loại
    /// </summary>
    private async Task<(Incident Incident, double DistanceMeters)?> FindDispatchedNeighbourAsync(
        Incident incident, DispatchSettingsDto settings, DateTime now, CancellationToken cancellationToken)
    {
        var radius = settings.AutoDispatchDuplicateRadiusMeters;
        var box = IncidentLinking.BoundingBox(incident.Latitude, incident.Longitude, radius);
        var since = now.AddMinutes(-settings.AutoDispatchDuplicateWindowMinutes);
        var candidates = await _incidentRepository.FindLinkCandidatesAsync(box.MinLat, box.MaxLat, box.MinLng, box.MaxLng, since, cancellationToken)
                         ?? (IReadOnlyList<Incident>)Array.Empty<Incident>();

        return candidates
            .Where(c => c.Id != incident.Id && c.Status != IncidentStatus.Linked)
            .Where(c => c.Status is IncidentStatus.Dispatched or IncidentStatus.InProgress
                        || c.AutoDispatchState == AutoDispatchState.Dispatched
                        || c.DispatchAssignments.Any(a => a.IsActive))
            .Select(c => (Incident: c, DistanceMeters: IncidentLinking.DistanceMeters(incident.Latitude, incident.Longitude, c.Latitude, c.Longitude)))
            .Where(x => x.DistanceMeters <= radius)
            .OrderBy(x => x.DistanceMeters)
            .Select(x => ((Incident, double)?)(x.Incident, x.DistanceMeters))
            .FirstOrDefault();
    }

    /// <summary>
    /// Chọn xe theo quy tắc loại xe (ưu tiên trước), xe gần nhất trước. BR-14: Level 1–2 một xe; Level 3 tối đa 5 xe theo số lượng quy tắc
    /// </summary>
    private async Task<List<Guid>> PickUnitsAsync(Incident incident, CancellationToken cancellationToken)
    {
        var isLevel3 = incident.Severity == SeverityLevel.Level3;
        var maxUnits = isLevel3 ? 5 : 1;
        var rules = DispatchScoring.ApplicableRules(await _dispatchService.GetUnitRulesAsync(incident, cancellationToken), incident.Severity);
        var chosen = new List<Guid>();

        // Chưa có quy tắc → để động cơ gợi ý tự lọc loại xe (luật dự phòng), lấy xe gần nhất
        var plan = rules.Count > 0
            ? rules.Select(r => (Code: (string?)r.UnitTypeCode, Need: isLevel3 ? r.Quantity : 1)).ToList()
            : new List<(string? Code, int Need)> { (null, 1) };

        foreach (var (code, need) in plan)
        {
            if (chosen.Count >= maxUnits) break;
            var recommendations = await _dispatchService.GetRecommendationsAsync(incident.Id, DispatchAlgorithm.NearestAvailable, code, null, cancellationToken);
            var picked = (recommendations.Data ?? new List<RecommendedUnitResponseDto>())
                .Select(r => r.RescueUnitId)
                .Where(id => !chosen.Contains(id))
                .Take(Math.Min(need, maxUnits - chosen.Count))
                .ToList();
            chosen.AddRange(picked);
            // Level 1–2: một xe của quy tắc ưu tiên cao nhất còn xe là đủ
            if (!isLevel3 && chosen.Count > 0) break;
        }

        return chosen;
    }

    private async Task MarkSkippedAsync(Incident incident, string note, Guid? duplicateOf, object details, CancellationToken cancellationToken)
    {
        incident.AutoDispatchState = AutoDispatchState.Skipped;
        incident.AutoDispatchNote = note;
        incident.SuspectedDuplicateOfIncidentId = duplicateOf;
        incident.UpdatedAt = DateTime.UtcNow;
        await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
        {
            IncidentId = incident.Id,
            ActorRole = DispatchService.AutoDispatchActorRole,
            Action = AuditAction.AutoDispatchSkipped,
            Description = note,
            NewStateJson = JsonSerializer.Serialize(new
            {
                details,
                confidence = incident.AiClassification?.ConfidenceScore,
                confidenceSource = incident.AiClassification?.ConfidenceSource
            }),
            Timestamp = DateTime.UtcNow
        }, cancellationToken);
        await _incidentRepository.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("AI không tự điều phối sự cố {IncidentId}: {Note}", incident.Id, note);
    }

    /// <summary>
    /// Báo trung tâm chỉ huy (Operator trạm phụ trách + Admin): realtime ReceiveAutoDispatched + thông báo
    /// </summary>
    private async Task AnnounceAsync(Incident incident, CancellationToken cancellationToken, string? plates = null)
    {
        var dispatched = incident.AutoDispatchState == AutoDispatchState.Dispatched;
        await _realtimeNotifier.AutoDispatchResultAsync(new AutoDispatchEventDto
        {
            IncidentId = incident.Id,
            ResponsibleStationId = incident.ResponsibleStationId,
            State = incident.AutoDispatchState,
            Note = incident.AutoDispatchNote,
            Confidence = incident.AiClassification?.ConfidenceScore,
            Plates = plates,
            SuspectedDuplicateOfIncidentId = incident.SuspectedDuplicateOfIncidentId,
            At = DateTime.UtcNow
        }, cancellationToken);
        await _realtimeNotifier.IncidentUpdatedAsync(IncidentService.MapToResponseDto(incident), cancellationToken);

        var recipients = (await _userRepository.GetActiveByRolesAsync(new[] { UserRole.Operator, UserRole.Admin }) ?? new List<User>())
            .Where(u => u.Role == UserRole.Admin || incident.ResponsibleStationId == null || u.StationId == incident.ResponsibleStationId)
            .Select(u => u.Id)
            .ToList();
        if (recipients.Count == 0) return;

        await _notificationService.NotifyUsersAsync(recipients, new NotificationRequest
        {
            Type = NotificationType.StatusUpdate,
            Title = dispatched ? "AI ĐÃ TỰ ĐIỀU PHỐI" : "AI CHUYỂN SỰ CỐ CHO ĐIỀU PHỐI VIÊN",
            Message = dispatched
                ? $"{incident.Title} — {incident.LocationAddress}. Xe: {plates}. Kiểm tra và hủy / đổi xe nếu cần."
                : $"{incident.Title} — {incident.LocationAddress}. {incident.AutoDispatchNote}",
            IncidentId = incident.Id,
            Data = new { autoDispatchState = incident.AutoDispatchState.ToString(), suspectedDuplicateOf = incident.SuspectedDuplicateOfIncidentId },
            HighPriority = true
        }, cancellationToken);
    }
}
