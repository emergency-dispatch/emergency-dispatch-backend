using EmergencyDispatch.Application.Localization;
using System.Text.Json;
using EmergencyDispatch.Application.DTOs.Ai;
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
    /// <summary>
    /// Ghi chú cho từng mã không đủ điều kiện (lưu vào sự cố, hiển thị song ngữ). Mã không có ở đây thì không lưu
    /// (not-awaiting-verification: Operator đã xác minh, không ghi đè)
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> NotEligibleNotes = new Dictionary<string, string>
    {
        ["linked-report"] = "AI không tự điều phối: báo cáo đã được gom vào sự cố khác.",
        ["out-of-service-area"] = "AI không tự điều phối: sự cố ngoài khu vực phục vụ.",
        ["unclassified"] = "AI không tự điều phối: chưa xác định được mức độ Level 1–3.",
        ["type-not-ai-recognizable"] = "AI không tự điều phối: loại sự cố không thuộc nhóm AI nhận diện (cháy nổ / tai nạn).",
        ["reporter-chose-other"] = "AI không tự điều phối: người báo chọn loại \"Khác\", cần Điều phối viên thẩm định.",
        ["operator-handling"] = "AI không tự điều phối: Điều phối viên đang giữ ca xử lý sự cố tại thời điểm AI phân tích.",
        ["ai-failed"] = "AI không tự điều phối: phân tích AI thất bại.",
        ["audio-description-unavailable"] = AudioEvidence.MissingDescriptionReview,
        ["confidence-not-measured"] = "AI không tự điều phối: độ tin cậy chỉ là ước lượng, không đo từ mô hình.",
        ["ai-type-mismatch"] = "AI không tự điều phối: loại sự cố AI nhận diện khác loại của sự cố.",
        ["below-threshold"] = "AI không tự điều phối: độ tin cậy thấp hơn ngưỡng cấu hình.",
        ["ensemble-no-photo"] = "AI không tự điều phối: sự cố không có ảnh để các model CNN bỏ phiếu (chế độ 3 AI).",
        ["ensemble-unavailable"] = "AI không tự điều phối: không gọi được ResNet-50 / YOLO11n-cls để bỏ phiếu (chế độ 3 AI).",
        ["ensemble-no-majority"] = "AI không tự điều phối: không model CNN nào cùng loại sự cố với TriageVL ở độ tin cậy đạt ngưỡng (chế độ 3 AI)."
    };

    private static readonly JsonSerializerOptions VoteJsonOptions = new(JsonSerializerDefaults.Web);

    public const string PartialNote = "AI tự điều phối một phần: chưa đủ số xe theo cấu hình của mức độ. Điều phối viên cần bổ sung xe.";

    private readonly IIncidentRepository _incidentRepository;
    private readonly IDispatchService _dispatchService;
    private readonly ISystemSettingsService _settingsService;
    private readonly INotificationService _notificationService;
    private readonly IUserRepository _userRepository;
    private readonly IRealtimeNotifier _realtimeNotifier;
    private readonly ICnnTriageService _cnnTriage;
    private readonly ILogger<AutoDispatchService> _logger;

    public AutoDispatchService(
        IIncidentRepository incidentRepository,
        IDispatchService dispatchService,
        ISystemSettingsService settingsService,
        INotificationService notificationService,
        IUserRepository userRepository,
        IRealtimeNotifier realtimeNotifier,
        ICnnTriageService cnnTriage,
        ILogger<AutoDispatchService> logger)
    {
        _incidentRepository = incidentRepository;
        _dispatchService = dispatchService;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _userRepository = userRepository;
        _realtimeNotifier = realtimeNotifier;
        _cnnTriage = cnnTriage;
        _logger = logger;
    }

    /// <summary>
    /// Luật "3 AI bỏ phiếu" (hàm thuần). Gọi sau khi TriageVL đã đạt điều kiện của IneligibleReason (đo bằng xác suất token,
    /// ≥ ngưỡng, cháy nổ / tai nạn Level 1–3). Đủ điều kiện khi ít nhất một trong ResNet-50 / YOLO11n-cls cùng loại sự cố
    /// với TriageVL và có độ tin cậy ≥ ngưỡng (TriageVL bắt buộc nằm trong nhóm đa số). Mức điều phối = mức cao nhất
    /// trong nhóm đa số (tránh hạ mức). Reason null = đủ điều kiện
    /// </summary>
    public static (string? Reason, SeverityLevel Severity, List<AiModelVoteDto> Votes) EnsembleVerdict(
        Incident incident, IReadOnlyList<AiModelVoteDto> cnnVotes, double threshold)
    {
        var ai = incident.AiClassification!;
        var type = AiClassificationResultDto.ToIncidentTypeLabel(ai.EventType);
        var votes = new List<AiModelVoteDto>
        {
            new()
            {
                Model = "TriageVL-3B",
                IncidentType = type,
                Severity = (int)incident.Severity,
                Confidence = ai.ConfidenceScore,
                ConfidenceSource = ai.ConfidenceSource,
                IsAvailable = true,
                InMajority = true
            }
        };
        foreach (var v in cnnVotes)
        {
            v.InMajority = v.IsAvailable && v.IncidentType == type && v.Confidence >= threshold;
            votes.Add(v);
        }

        if (!cnnVotes.Any(v => v.IsAvailable)) return ("ensemble-unavailable", incident.Severity, votes);
        if (!cnnVotes.Any(v => v.InMajority)) return ("ensemble-no-majority", incident.Severity, votes);
        var level = Math.Clamp(votes.Where(v => v.InMajority).Max(v => v.Severity), 1, 3);
        return (null, (SeverityLevel)level, votes);
    }

    public static List<AiModelVoteDto>? ParseVotes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<AiModelVoteDto>>(json, VoteJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Gọi 2 model CNN (YoloService) cho ảnh của sự cố rồi áp luật bỏ phiếu. Lỗi gọi service → coi như không có phiếu CNN
    /// </summary>
    private async Task<(string? Reason, SeverityLevel Severity, List<AiModelVoteDto> Votes)> RunEnsembleAsync(
        Incident incident, DispatchSettingsDto settings, CancellationToken cancellationToken)
    {
        var photo = incident.MediaItems.FirstOrDefault(m => m.MediaType == MediaType.Photo)?.MediaUrl;
        if (string.IsNullOrWhiteSpace(photo))
        {
            var (_, sev, onlyTriage) = EnsembleVerdict(incident, Array.Empty<AiModelVoteDto>(), settings.AutoDispatchMinConfidence);
            return ("ensemble-no-photo", sev, onlyTriage);
        }

        IReadOnlyList<AiModelVoteDto> cnnVotes;
        try
        {
            cnnVotes = await _cnnTriage.ClassifyAsync(photo, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Không gọi được 2 model CNN cho sự cố {IncidentId}", incident.Id);
            cnnVotes = Array.Empty<AiModelVoteDto>();
        }
        return EnsembleVerdict(incident, cnnVotes, settings.AutoDispatchMinConfidence);
    }

    /// <summary>
    /// Điều kiện AI (hàm thuần): null = đủ điều kiện; ngược lại là lý do không xét tự điều phối
    /// </summary>
    public static string? IneligibleReason(Incident incident, DispatchSettingsDto settings, DateTime utcNow)
    {
        if (incident.Status != IncidentStatus.AiProcessed || incident.VerifiedAt != null) return "not-awaiting-verification";
        if (string.IsNullOrWhiteSpace(incident.Description) && incident.MediaItems.Any(m => !m.IsDeleted && m.MediaType == MediaType.Audio))
            return "audio-description-unavailable";
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
        if (incident == null)
        {
            return AutoDispatchOutcome.NotEligible;
        }
        if (IneligibleReason(incident, settings, DateTime.UtcNow) is { } early)
        {
            _logger.LogDebug("Auto-dispatch bỏ qua sự cố {IncidentId}: {Reason}", incidentId, early);
            await RecordNotEligibleAsync(incident, early, cancellationToken);
            return AutoDispatchOutcome.NotEligible;
        }

        // Chế độ 3 AI bỏ phiếu: gọi 2 model CNN trước khi khóa khu vực (gọi mạng không giữ khóa)
        string? votesJson = null;
        SeverityLevel? ensembleSeverity = null;
        if (settings.AutoDispatchMode == AutoDispatchModes.Ensemble)
        {
            var (reason, severity, votes) = await RunEnsembleAsync(incident, settings, cancellationToken);
            votesJson = JsonSerializer.Serialize(votes, VoteJsonOptions);
            incident.AiClassification!.EnsembleVotesJson = votesJson;
            if (reason != null)
            {
                _logger.LogDebug("Auto-dispatch bỏ qua sự cố {IncidentId}: {Reason}", incidentId, reason);
                if (!await RecordNotEligibleAsync(incident, reason, cancellationToken))
                {
                    await _incidentRepository.SaveChangesAsync(cancellationToken);
                }
                return AutoDispatchOutcome.NotEligible;
            }
            ensembleSeverity = severity;
        }

        // Khóa khu vực: hai sự cố gần nhau không được tự điều phối song song (bước chống trùng phải thấy nhau)
        var areaLock = await _incidentRepository.BeginAreaLockAsync(incident.Latitude, incident.Longitude,
            settings.AutoDispatchDuplicateRadiusMeters, cancellationToken);
        var committed = false;
        try
        {
            await _incidentRepository.ReloadAsync(incident, cancellationToken);
            if (votesJson != null) incident.AiClassification!.EnsembleVotesJson = votesJson;
            var now = DateTime.UtcNow;
            if (IneligibleReason(incident, settings, now) is { } late)
            {
                if (await RecordNotEligibleAsync(incident, late, cancellationToken) && areaLock != null)
                {
                    await areaLock.CommitAsync(cancellationToken);
                    committed = true;
                }
                return AutoDispatchOutcome.NotEligible;
            }

            // Mức cao nhất trong nhóm đa số cao hơn mức của TriageVL → nâng mức sự cố trước khi chọn xe theo kế hoạch
            var aiSeverity = incident.Severity;
            if (ensembleSeverity is { } upgraded && upgraded > incident.Severity)
            {
                incident.Severity = upgraded;
            }

            var duplicate = await FindDispatchedNeighbourAsync(incident, settings, now, cancellationToken);
            if (duplicate is { } dup)
            {
                incident.Severity = aiSeverity; // không điều xe → giữ mức TriageVL cho Operator xét
                await MarkSkippedAsync(incident, SuspectedDuplicateNote, "suspected-duplicate", dup.Incident.Id,
                    new { reason = "SuspectedDuplicate", duplicateOf = dup.Incident.Id, distanceMeters = Math.Round(dup.DistanceMeters, 1) }, cancellationToken);
                if (areaLock != null) await areaLock.CommitAsync(cancellationToken);
                committed = true;
                await AnnounceAsync(incident, cancellationToken);
                return AutoDispatchOutcome.SkippedDuplicate;
            }

            var (unitIds, plannedUnits) = await PickUnitsAsync(incident, cancellationToken);
            if (unitIds.Count == 0)
            {
                incident.Severity = aiSeverity;
                await MarkSkippedAsync(incident, NoUnitsNote, "no-teams", null, new { reason = "NoUnits" }, cancellationToken);
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
            incident.AutoDispatchNote = unitIds.Count < plannedUnits ? PartialNote : DispatchedNote;
            incident.AutoDispatchReasonCode = unitIds.Count < plannedUnits ? "partial" : null;
            incident.SuspectedDuplicateOfIncidentId = null;
            incident.UpdatedAt = now;

            await _incidentRepository.AddStatusHistoryAsync(new IncidentStatusHistory
            {
                IncidentId = incident.Id,
                OldStatus = oldStatus,
                NewStatus = IncidentStatus.Verified,
                ChangedByRole = DispatchService.AutoDispatchActorRole,
                Notes = votesJson == null
                    ? $"AI tự xác minh: {incident.EventType} {incident.Severity}, độ tin cậy {ai.ConfidenceScore:P1} (ngưỡng {settings.AutoDispatchMinConfidence:P0})."
                    : $"AI tự xác minh (3 AI bỏ phiếu): {incident.EventType} {incident.Severity}"
                      + (incident.Severity != aiSeverity ? $" (nâng từ {aiSeverity} theo mức cao nhất trong nhóm đa số)" : "")
                      + $", TriageVL {ai.ConfidenceScore:P1}, ngưỡng {settings.AutoDispatchMinConfidence:P0}.",
                ChangedAt = now
            }, cancellationToken);
            await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
            {
                IncidentId = incident.Id,
                ActorRole = DispatchService.AutoDispatchActorRole,
                Action = AuditAction.AutoDispatched,
                Description = $"AI tự điều phối {unitIds.Count}/{plannedUnits} xe theo cấu hình (độ tin cậy {ai.ConfidenceScore:P1}, ngưỡng {settings.AutoDispatchMinConfidence:P0})",
                NewStateJson = JsonSerializer.Serialize(new
                {
                    confidence = ai.ConfidenceScore,
                    confidenceSource = ai.ConfidenceSource,
                    threshold = settings.AutoDispatchMinConfidence,
                    mode = settings.AutoDispatchMode,
                    votes = ParseVotes(votesJson),
                    aiSeverity = aiSeverity.ToString(),
                    eventType = incident.EventType.ToString(),
                    incidentTypeCode = incident.IncidentTypeCode,
                    severity = incident.Severity.ToString(),
                    model = ai.ModelName,
                    plannedUnits,
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
                    await MarkSkippedAsync(fresh, AssignFailedNote, "assign-failed", null, new { reason = "AssignFailed", message = result.Message }, cancellationToken);
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
    /// Chọn tổ theo kế hoạch điều phối của mức độ (Admin cấu hình: chức năng × số tổ), loại ưu tiên trước, xe gần nhất trước, tối đa 5.
    /// Chưa có kế hoạch → một xe gần nhất theo luật dự phòng. Trả kèm số xe kế hoạch để biết có thiếu xe không
    /// </summary>
    private async Task<(List<Guid> Units, int Planned)> PickUnitsAsync(Incident incident, CancellationToken cancellationToken)
    {
        var rules = DispatchScoring.ApplicableRules(await _dispatchService.GetUnitRulesAsync(incident, cancellationToken), incident.Severity);
        var plan = rules.Count > 0
            ? rules.Select(r => (Code: (string?)r.UnitTypeCode, Need: r.Quantity)).ToList()
            : new List<(string? Code, int Need)> { (null, 1) };
        var planned = Math.Min(plan.Sum(p => p.Need), DispatchScoring.MaxUnitsPerLevel);
        var chosen = new List<Guid>();
        var chosenTeams = new HashSet<Guid>();

        // Giao theo tổ: mỗi chức năng trong kế hoạch là các tổ khác nhau (mỗi tổ mang một xe đúng loại), tổ gần nhất trước
        foreach (var (code, need) in plan)
        {
            if (chosen.Count >= planned) break;
            var remaining = need;
            var recommendations = await _dispatchService.GetRecommendationsAsync(incident.Id, DispatchAlgorithm.NearestAvailable, code, null, cancellationToken);
            foreach (var r in recommendations.Data ?? new List<RecommendedUnitResponseDto>())
            {
                if (chosen.Count >= planned || remaining <= 0) break;
                if (chosen.Contains(r.RescueUnitId) || !chosenTeams.Add(r.StationTeamId)) continue;
                chosen.Add(r.RescueUnitId);
                remaining--;
            }
        }

        return (chosen, planned);
    }

    /// <summary>
    /// Lưu lý do không đủ điều kiện vào sự cố và cập nhật realtime (không gửi thông báo / cảnh báo).
    /// Không ghi đè kết quả đã có (Dispatched / Skipped) và bỏ qua mã không cần lưu. true = đã lưu
    /// </summary>
    private async Task<bool> RecordNotEligibleAsync(Incident incident, string reason, CancellationToken cancellationToken)
    {
        if (!NotEligibleNotes.TryGetValue(reason, out var note)
            || incident.AutoDispatchState is not (AutoDispatchState.None or AutoDispatchState.NotEligible))
        {
            return false;
        }

        incident.AutoDispatchState = AutoDispatchState.NotEligible;
        incident.AutoDispatchReasonCode = reason;
        incident.AutoDispatchNote = note;
        incident.UpdatedAt = DateTime.UtcNow;
        await _incidentRepository.SaveChangesAsync(cancellationToken);

        await _realtimeNotifier.AutoDispatchResultAsync(new AutoDispatchEventDto
        {
            IncidentId = incident.Id,
            ResponsibleStationId = incident.ResponsibleStationId,
            State = AutoDispatchState.NotEligible,
            Note = LocalizationHelper.Localize(note),
            ReasonCode = reason,
            Confidence = incident.AiClassification?.ConfidenceScore,
            Votes = ParseVotes(incident.AiClassification?.EnsembleVotesJson),
            At = DateTime.UtcNow
        }, cancellationToken);
        await _realtimeNotifier.IncidentUpdatedAsync(IncidentService.MapToResponseDto(incident), cancellationToken);
        return true;
    }

    private async Task MarkSkippedAsync(Incident incident, string note, string reasonCode, Guid? duplicateOf, object details, CancellationToken cancellationToken)
    {
        incident.AutoDispatchState = AutoDispatchState.Skipped;
        incident.AutoDispatchReasonCode = reasonCode;
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
            Note = incident.AutoDispatchNote == null ? null : LocalizationHelper.Localize(incident.AutoDispatchNote),
            ReasonCode = incident.AutoDispatchReasonCode,
            Confidence = incident.AiClassification?.ConfidenceScore,
            Plates = plates,
            SuspectedDuplicateOfIncidentId = incident.SuspectedDuplicateOfIncidentId,
            Votes = ParseVotes(incident.AiClassification?.EnsembleVotesJson),
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
                ? new LocalizedText("{0} — {1}. Xe: {2}. Kiểm tra và hủy / đổi xe nếu cần.", incident.Title, incident.LocationAddress, plates)
                : new LocalizedText("{0} — {1}. {2}", incident.Title, incident.LocationAddress, incident.AutoDispatchNote),
            IncidentId = incident.Id,
            Data = new { autoDispatchState = incident.AutoDispatchState.ToString(), suspectedDuplicateOf = incident.SuspectedDuplicateOfIncidentId },
            HighPriority = true
        }, cancellationToken);
    }
}
