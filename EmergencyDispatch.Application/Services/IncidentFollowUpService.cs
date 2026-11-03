using System.Text.Json;
using EmergencyDispatch.Application.DTOs.Ai;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.DTOs.Notification;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Localization;
using EmergencyDispatch.Application.Validators;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Application.Services;

/// <summary>
/// Đổi mức độ sau xác minh (B), gom / tách báo cáo trùng (C), người báo bổ sung thông tin (D). Mọi thao tác ghi IncidentAuditTrail.
/// </summary>
public class IncidentFollowUpService : IIncidentFollowUpService
{
    public const int MinReasonLength = 5;
    public const int MaxSupplementsPerIncident = 20;
    public const int MaxSupplementMedia = 5;
    private const string SystemRole = "System";
    private const string GuestRole = "Guest";

    private readonly IIncidentRepository _incidentRepository;
    private readonly IUserRepository _userRepository;
    private readonly IIncidentSupplementRepository _supplementRepository;
    private readonly IRealtimeNotifier _realtimeNotifier;
    private readonly INotificationService _notificationService;
    private readonly ICurrentUserContext _currentUser;
    private readonly ISystemSettingsService _settingsService;
    private readonly IAiClassificationService _aiService;
    private readonly IAiAnalysisQueue _aiQueue;
    private readonly IGuestCancellationTokenService _guestTokens;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IncidentFollowUpService> _logger;

    public IncidentFollowUpService(
        IIncidentRepository incidentRepository,
        IUserRepository userRepository,
        IIncidentSupplementRepository supplementRepository,
        IRealtimeNotifier realtimeNotifier,
        INotificationService notificationService,
        ICurrentUserContext currentUser,
        ISystemSettingsService settingsService,
        IAiClassificationService aiService,
        IAiAnalysisQueue aiQueue,
        IGuestCancellationTokenService guestTokens,
        IConfiguration configuration,
        ILogger<IncidentFollowUpService> logger)
    {
        _incidentRepository = incidentRepository;
        _userRepository = userRepository;
        _supplementRepository = supplementRepository;
        _realtimeNotifier = realtimeNotifier;
        _notificationService = notificationService;
        _currentUser = currentUser;
        _settingsService = settingsService;
        _aiService = aiService;
        _aiQueue = aiQueue;
        _guestTokens = guestTokens;
        _configuration = configuration;
        _logger = logger;
    }

    // =====================================================================================
    // B. Đổi mức độ sau khi xác minh
    // =====================================================================================
    public async Task<ApiResponseDto<IncidentResponseDto>> ChangeSeverityAsync(Guid incidentId, ChangeSeverityDto dto, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var reason = dto.Reason?.Trim() ?? string.Empty;
        if (reason.Length < MinReasonLength)
        {
            return ApiResponseDto<IncidentResponseDto>.FailureResult(
                LocalizationHelper.Localize("Vui lòng nhập lý do thay đổi mức độ (tối thiểu {0} ký tự).", MinReasonLength));
        }

        if (dto.NewSeverity is not (SeverityLevel.Level1 or SeverityLevel.Level2 or SeverityLevel.Level3))
        {
            return ApiResponseDto<IncidentResponseDto>.FailureResult("Mức độ mới phải từ Level 1 đến Level 3.");
        }

        if (dto.NewEventType is { } t && t is not (IncidentEventType.FireExplosion or IncidentEventType.TrafficAccident))
        {
            return ApiResponseDto<IncidentResponseDto>.FailureResult("Loại sự cố chỉ nhận FireExplosion (cháy nổ) hoặc TrafficAccident (tai nạn).");
        }

        var incident = await _incidentRepository.GetIncidentWithDetailsAsync(incidentId, cancellationToken);
        if (incident == null)
        {
            return ApiResponseDto<IncidentResponseDto>.NotFoundResult("Không tìm thấy sự cố.");
        }

        var scopeError = _currentUser.CheckIncident(incident.ResponsibleStationId);
        if (scopeError != null)
        {
            return ApiResponseDto<IncidentResponseDto>.ForbiddenResult(scopeError);
        }

        if (incident.VerifiedAt == null
            || incident.Status is not (IncidentStatus.Verified or IncidentStatus.Dispatched or IncidentStatus.InProgress or IncidentStatus.Escalated))
        {
            return ApiResponseDto<IncidentResponseDto>.FailureResult(
                LocalizationHelper.Localize("Chỉ đổi mức độ được cho sự cố đã xác minh và đang xử lý (trạng thái hiện tại: {0}). Sự cố chưa xác minh hãy dùng chức năng xác minh.", incident.Status));
        }

        if (incident.IsLockedByOther(actorUserId, DateTime.UtcNow))
        {
            return ApiResponseDto<IncidentResponseDto>.ConflictResult(
                LocalizationHelper.Localize("Sự cố đang được xử lý bởi {0}. Vui lòng chờ khóa hết hạn.",
                    incident.LockedByOperator?.FullName ?? LocalizationHelper.Localize("điều phối viên khác")));
        }

        var newType = dto.NewEventType ?? incident.EventType;
        if (dto.NewSeverity == incident.Severity && newType == incident.EventType)
        {
            return ApiResponseDto<IncidentResponseDto>.FailureResult("Mức độ và loại sự cố không thay đổi.");
        }

        var oldSeverity = incident.Severity;
        var oldType = incident.EventType;
        incident.Severity = dto.NewSeverity;
        incident.EventType = newType;
        incident.UpdatedAt = DateTime.UtcNow;

        await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
        {
            IncidentId = incident.Id,
            ActorId = actorUserId,
            ActorRole = _currentUser.Role?.ToString() ?? UserRole.Operator.ToString(),
            Action = AuditAction.SeverityChanged,
            Description = $"Đổi mức độ sau xác minh: {oldSeverity} → {dto.NewSeverity}" +
                          (oldType != newType ? $", loại {oldType} → {newType}" : string.Empty) + $". Lý do: {reason}",
            OldStateJson = JsonSerializer.Serialize(new { severity = oldSeverity.ToString(), eventType = oldType.ToString() }),
            NewStateJson = JsonSerializer.Serialize(new { severity = dto.NewSeverity.ToString(), eventType = newType.ToString(), reason }),
            IpAddress = _currentUser.IpAddress
        }, cancellationToken);
        await _incidentRepository.SaveChangesAsync(cancellationToken);

        var response = IncidentService.MapToResponseDto(incident);
        await _realtimeNotifier.IncidentUpdatedAsync(response, cancellationToken);

        var raised = dto.NewSeverity > oldSeverity;
        foreach (var stationId in ActiveUnitStations(incident))
        {
            await _notificationService.NotifyStationStaffAsync(stationId, new NotificationRequest
            {
                Type = NotificationType.StatusUpdate,
                Title = raised ? $"NÂNG MỨC ĐỘ SỰ CỐ → {dto.NewSeverity}" : $"Điều chỉnh mức độ sự cố → {dto.NewSeverity}",
                Message = $"{incident.Title}: {oldSeverity} → {dto.NewSeverity}. Lý do: {reason}",
                IncidentId = incident.Id,
                HighPriority = raised
            }, cancellationToken);
        }

        var activeUnits = incident.DispatchAssignments.Count(a => a.IsActive);
        var message = dto.NewSeverity != SeverityLevel.Level3 && activeUnits > 1
            ? LocalizationHelper.Localize("Đã đổi mức độ sự cố. Lưu ý: đang có {0} xe xử lý, nhiều hơn số xe chuẩn của mức này; có thể rút bớt nếu không cần.", activeUnits)
            : "Đã đổi mức độ sự cố.";
        return ApiResponseDto<IncidentResponseDto>.SuccessResult(response, message);
    }

    // =====================================================================================
    // C. Gom / tách báo cáo trùng
    // =====================================================================================
    public async Task<(Incident Parent, double DistanceMeters)?> FindLinkParentAsync(Incident newReport, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetDuplicateDetectionAsync(cancellationToken);
        if (!settings.Enabled)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var windowStart = now.AddMinutes(-settings.WindowMinutes);
        var box = IncidentLinking.BoundingBox(newReport.Latitude, newReport.Longitude, settings.RadiusMeters);
        var candidates = await _incidentRepository.FindLinkCandidatesAsync(box.MinLat, box.MaxLat, box.MinLng, box.MaxLng, windowStart, cancellationToken);

        return IncidentLinking.PickParent(candidates.Where(c => c.Id != newReport.Id), newReport.Latitude, newReport.Longitude,
            IncidentLinking.KnownType(newReport), settings.RadiusMeters, windowStart);
    }

    public async Task<LinkedToIncidentDto> OnReportLinkedAsync(Incident report, Incident parent, double distanceMeters, CancellationToken cancellationToken = default)
    {
        await _incidentRepository.AdjustLinkedReportCountAsync(parent.Id, +1, report.CreatedAt, cancellationToken);
        await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
        {
            IncidentId = parent.Id,
            ActorRole = SystemRole,
            Action = AuditAction.ReportLinked,
            Description = $"Gom báo cáo #{ShortId(report.Id)} (cách {distanceMeters:F0} m) vào sự cố này",
            NewStateJson = JsonSerializer.Serialize(new { linkedReportId = report.Id, distanceMeters = Math.Round(distanceMeters) })
        }, cancellationToken);
        await _incidentRepository.SaveChangesAsync(cancellationToken);

        var refreshed = await _incidentRepository.GetIncidentWithDetailsAsync(parent.Id, cancellationToken) ?? parent;
        await _realtimeNotifier.IncidentUpdatedAsync(IncidentService.MapToResponseDto(refreshed), cancellationToken);
        _logger.LogInformation("Gom báo cáo {ReportId} vào sự cố {ParentId} ({Distance:F0} m).", report.Id, parent.Id, distanceMeters);

        return new LinkedToIncidentDto
        {
            IncidentId = refreshed.Id,
            Title = refreshed.Title,
            LocationAddress = refreshed.LocationAddress,
            EventType = refreshed.EventType,
            Status = refreshed.Status,
            DistanceMeters = Math.Round(distanceMeters),
            AssignedUnitPlates = refreshed.DispatchAssignments.Where(a => a.IsActive)
                .Select(a => a.RescueUnit?.PlateNumber).Where(p => p != null).Select(p => p!).Distinct().ToList()
        };
    }

    public async Task HandleQuickCheckAsync(AiAnalysisJob job, CancellationToken cancellationToken = default)
    {
        var detected = await _aiService.QuickCheckEventTypeAsync(job.MediaUrl, cancellationToken);
        var report = await _incidentRepository.GetIncidentWithDetailsAsync(job.IncidentId, cancellationToken);
        if (report == null || report.Status != IncidentStatus.Linked || report.DuplicateOfIncidentId == null)
        {
            return;
        }

        report.QuickCheckEventType = detected;
        var parent = await _incidentRepository.GetIncidentWithDetailsAsync(report.DuplicateOfIncidentId.Value, cancellationToken);
        var parentType = parent != null ? IncidentLinking.KnownType(parent) : null;

        if (detected != null && !IncidentLinking.Compatible(parentType, detected))
        {
            await UnlinkCoreAsync(report, null, SystemRole,
                $"Ảnh nhận ra {detected}, khác loại sự cố gốc ({parentType})", cancellationToken);
            return;
        }

        await _incidentRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task<ApiResponseDto<List<LinkedReportDto>>> GetLinkedReportsAsync(Guid parentIncidentId, CancellationToken cancellationToken = default)
    {
        var parent = await _incidentRepository.GetIncidentWithDetailsAsync(parentIncidentId, cancellationToken);
        if (parent == null)
        {
            return ApiResponseDto<List<LinkedReportDto>>.NotFoundResult("Không tìm thấy sự cố.");
        }

        var scopeError = _currentUser.CheckIncident(parent.ResponsibleStationId);
        if (scopeError != null)
        {
            return ApiResponseDto<List<LinkedReportDto>>.ForbiddenResult(scopeError);
        }

        var settings = await _settingsService.GetDuplicateDetectionAsync(cancellationToken);
        var parentType = IncidentLinking.KnownType(parent);
        var reports = await _incidentRepository.GetLinkedReportsAsync(parentIncidentId, cancellationToken);
        var list = reports.Select(r => new LinkedReportDto
        {
            IncidentId = r.Id,
            Status = r.Status,
            ReportedAt = r.CreatedAt,
            LinkedAt = r.LinkedAt,
            DistanceMeters = r.LinkDistanceMeters,
            Description = r.Description,
            LocationAddress = r.LocationAddress,
            Latitude = r.Latitude,
            Longitude = r.Longitude,
            ReporterName = r.ReporterName,
            MaskedReporterPhone = PhoneMasking.Mask(r.ReporterPhone),
            ReportedEventType = r.ReportedEventType,
            QuickCheckEventType = r.QuickCheckEventType,
            IsOutlier = IncidentLinking.IsOutlier(r, parentType, settings.RadiusMeters),
            MediaUrls = r.MediaItems.Select(m => m.MediaUrl).ToList()
        }).ToList();

        return ApiResponseDto<List<LinkedReportDto>>.SuccessResult(list);
    }

    public async Task<ApiResponseDto<IncidentResponseDto>> UnlinkAsync(Guid reportId, UnlinkIncidentDto dto, Guid? actorUserId, CancellationToken cancellationToken = default)
    {
        var report = await _incidentRepository.GetIncidentWithDetailsAsync(reportId, cancellationToken);
        if (report == null)
        {
            return ApiResponseDto<IncidentResponseDto>.NotFoundResult("Không tìm thấy sự cố.");
        }

        if (report.Status != IncidentStatus.Linked || report.DuplicateOfIncidentId == null)
        {
            return ApiResponseDto<IncidentResponseDto>.FailureResult("Báo cáo này không nằm trong nhóm gom nào.");
        }

        var (role, error) = await ResolveReporterOrCommandCenterAsync(report, actorUserId, dto.GuestCancellationToken);
        if (error != null)
        {
            return error;
        }

        var reason = string.IsNullOrWhiteSpace(dto.Reason)
            ? (role is nameof(UserRole.Operator) or nameof(UserRole.Admin) ? "Điều phối viên tách báo cáo" : "Người báo xác nhận đây là sự cố khác")
            : dto.Reason.Trim();
        await UnlinkCoreAsync(report, actorUserId, role, reason, cancellationToken);
        return ApiResponseDto<IncidentResponseDto>.SuccessResult(IncidentService.MapToResponseDto(report, revealPhone: role is nameof(UserRole.Citizen) or GuestRole),
            "Đã tách báo cáo thành sự cố riêng. Điều phối viên sẽ xử lý ngay.");
    }

    public async Task<ApiResponseDto<IncidentResponseDto>> ConfirmDuplicateAsync(Guid reportId, string? note, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var report = await _incidentRepository.GetIncidentWithDetailsAsync(reportId, cancellationToken);
        if (report == null)
        {
            return ApiResponseDto<IncidentResponseDto>.NotFoundResult("Không tìm thấy sự cố.");
        }

        if (report.Status != IncidentStatus.Linked || report.DuplicateOfIncidentId == null)
        {
            return ApiResponseDto<IncidentResponseDto>.FailureResult("Báo cáo này không nằm trong nhóm gom nào.");
        }

        var scopeError = _currentUser.CheckIncident(report.ResponsibleStationId);
        if (scopeError != null)
        {
            return ApiResponseDto<IncidentResponseDto>.ForbiddenResult(scopeError);
        }

        var parentId = report.DuplicateOfIncidentId.Value;
        var text = string.IsNullOrWhiteSpace(note) ? $"Trùng với sự cố #{ShortId(parentId)}" : note.Trim();
        await CloseAsDuplicateAsync(report, actorUserId, _currentUser.Role?.ToString() ?? nameof(UserRole.Operator), text, AuditAction.DuplicateConfirmed, cancellationToken);
        await _incidentRepository.AdjustLinkedReportCountAsync(parentId, -1, null, cancellationToken);
        await PublishParentAsync(parentId, cancellationToken);

        return ApiResponseDto<IncidentResponseDto>.SuccessResult(IncidentService.MapToResponseDto(report), "Đã xác nhận báo cáo trùng.");
    }

    public async Task<int> RestoreLinkedReportsAsync(Guid parentIncidentId, string reason, CancellationToken cancellationToken = default)
    {
        var linked = (await _incidentRepository.GetLinkedReportsAsync(parentIncidentId, cancellationToken))
            .Where(r => r.Status == IncidentStatus.Linked).Select(r => r.Id).ToList();
        foreach (var id in linked)
        {
            var report = await _incidentRepository.GetIncidentWithDetailsAsync(id, cancellationToken);
            if (report is { Status: IncidentStatus.Linked })
            {
                await UnlinkCoreAsync(report, null, SystemRole, reason, cancellationToken, AuditAction.LinkedReportsRestored);
            }
        }

        if (linked.Count > 0)
        {
            await _incidentRepository.AdjustLinkedReportCountAsync(parentIncidentId, -linked.Count, null, cancellationToken);
        }

        return linked.Count;
    }

    public async Task<int> CloseLinkedReportsAsync(Guid parentIncidentId, Guid? actorUserId, CancellationToken cancellationToken = default)
    {
        var linked = (await _incidentRepository.GetLinkedReportsAsync(parentIncidentId, cancellationToken))
            .Where(r => r.Status == IncidentStatus.Linked).Select(r => r.Id).ToList();
        foreach (var id in linked)
        {
            var report = await _incidentRepository.GetIncidentWithDetailsAsync(id, cancellationToken);
            if (report is { Status: IncidentStatus.Linked })
            {
                await CloseAsDuplicateAsync(report, actorUserId, SystemRole,
                    $"Đã xử lý cùng sự cố #{ShortId(parentIncidentId)}", AuditAction.DuplicateConfirmed, cancellationToken);
            }
        }

        if (linked.Count > 0)
        {
            await _incidentRepository.AdjustLinkedReportCountAsync(parentIncidentId, -linked.Count, null, cancellationToken);
        }

        return linked.Count;
    }

    // =====================================================================================
    // D. Người báo bổ sung thông tin
    // =====================================================================================
    public async Task<ApiResponseDto<IncidentSupplementDto>> AddSupplementAsync(Guid incidentId, AddSupplementDto dto, Guid? actorUserId, CancellationToken cancellationToken = default)
    {
        var note = dto.Note?.Trim() ?? string.Empty;
        var media = (dto.MediaUrls ?? new List<string>()).Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).ToList();
        if (note.Length == 0 && media.Count == 0)
        {
            return ApiResponseDto<IncidentSupplementDto>.FailureResult("Vui lòng nhập nội dung hoặc đính kèm ảnh bổ sung.");
        }

        if (note.Length > 1000)
        {
            return ApiResponseDto<IncidentSupplementDto>.FailureResult("Nội dung bổ sung không được vượt quá 1000 ký tự.");
        }

        if (media.Count > MaxSupplementMedia || media.Any(u => !MediaUrlPolicy.IsTrusted(u, _configuration)))
        {
            return ApiResponseDto<IncidentSupplementDto>.FailureResult(
                LocalizationHelper.Localize("Tối đa {0} ảnh / video, và phải được tải lên qua hệ thống (POST /api/media/upload).", MaxSupplementMedia));
        }

        var incident = await _incidentRepository.GetIncidentWithDetailsAsync(incidentId, cancellationToken);
        if (incident == null)
        {
            return ApiResponseDto<IncidentSupplementDto>.NotFoundResult("Không tìm thấy sự cố.");
        }

        var (role, error) = await ResolveReporterAsync(incident, actorUserId, dto.GuestCancellationToken);
        if (error != null)
        {
            return error;
        }

        if (incident.Status is IncidentStatus.Completed or IncidentStatus.Cancelled)
        {
            return ApiResponseDto<IncidentSupplementDto>.FailureResult("Sự cố đã kết thúc, không thể bổ sung thông tin.");
        }

        if ((await _supplementRepository.GetByIncidentIdsAsync(new[] { incident.Id }, cancellationToken)).Count >= MaxSupplementsPerIncident)
        {
            return ApiResponseDto<IncidentSupplementDto>.TooManyRequestsResult(
                LocalizationHelper.Localize("Mỗi sự cố chỉ được bổ sung tối đa {0} lần. Nếu khẩn cấp, vui lòng gọi trực tiếp 114 / 115.", MaxSupplementsPerIncident));
        }

        // Báo cáo đang được gom → thông tin chuyển tới sự cố gốc (nơi đội đang xử lý)
        var target = incident;
        if (incident.Status == IncidentStatus.Linked && incident.DuplicateOfIncidentId.HasValue)
        {
            target = await _incidentRepository.GetIncidentWithDetailsAsync(incident.DuplicateOfIncidentId.Value, cancellationToken) ?? incident;
        }

        var supplement = new IncidentSupplement
        {
            IncidentId = incident.Id,
            ReporterUserId = actorUserId,
            Note = note,
            MediaUrls = media
        };
        await _supplementRepository.AddAsync(supplement);

        var description = $"Người báo bổ sung thông tin" + (target.Id != incident.Id ? $" (từ báo cáo gom #{ShortId(incident.Id)})" : string.Empty)
                          + (note.Length > 0 ? $": {note}" : string.Empty) + (media.Count > 0 ? $" (+{media.Count} ảnh / video)" : string.Empty);
        await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
        {
            IncidentId = target.Id,
            ActorId = actorUserId,
            ActorRole = role,
            Action = AuditAction.SupplementAdded,
            Description = description.Length > 1000 ? description[..1000] : description,
            NewStateJson = JsonSerializer.Serialize(new { supplementId = supplement.Id, sourceIncidentId = incident.Id, mediaCount = media.Count }),
            IpAddress = _currentUser.IpAddress
        }, cancellationToken);
        await _incidentRepository.SaveChangesAsync(cancellationToken);

        var dtoOut = MapSupplement(supplement, target.Id, incident.ReporterName);
        var stations = ActiveUnitStations(target);
        await _realtimeNotifier.IncidentSupplementAddedAsync(dtoOut, target.ResponsibleStationId, stations, cancellationToken);
        foreach (var stationId in stations)
        {
            await _notificationService.NotifyStationStaffAsync(stationId, new NotificationRequest
            {
                Type = NotificationType.StatusUpdate,
                Title = "Người báo bổ sung thông tin hiện trường",
                Message = $"{target.Title}: {(note.Length > 0 ? note : "ảnh / video mới")}",
                IncidentId = target.Id,
                HighPriority = true
            }, cancellationToken);
        }

        return ApiResponseDto<IncidentSupplementDto>.SuccessResult(dtoOut, "Đã gửi thông tin bổ sung tới trung tâm điều phối.");
    }

    public async Task<ApiResponseDto<List<IncidentSupplementDto>>> GetSupplementsAsync(Guid incidentId, Guid? actorUserId, CancellationToken cancellationToken = default)
    {
        var incident = await _incidentRepository.GetIncidentWithDetailsAsync(incidentId, cancellationToken);
        if (incident == null)
        {
            return ApiResponseDto<List<IncidentSupplementDto>>.NotFoundResult("Không tìm thấy sự cố.");
        }

        var viewer = actorUserId.HasValue ? await _userRepository.GetByIdAsync(actorUserId.Value) : null;
        if (viewer == null || !IncidentService.CanAccess(incident, viewer))
        {
            return ApiResponseDto<List<IncidentSupplementDto>>.ForbiddenResult("Bạn không có quyền xem sự cố này.");
        }

        var linked = await _incidentRepository.GetLinkedReportsAsync(incidentId, cancellationToken);
        var names = linked.ToDictionary(r => r.Id, r => r.ReporterName);
        names[incident.Id] = incident.ReporterName;
        var items = await _supplementRepository.GetByIncidentIdsAsync(names.Keys.ToList(), cancellationToken);
        return ApiResponseDto<List<IncidentSupplementDto>>.SuccessResult(
            items.Select(s => MapSupplement(s, incidentId, names.GetValueOrDefault(s.IncidentId))).ToList());
    }

    // =====================================================================================
    // Nội bộ
    // =====================================================================================
    private async Task UnlinkCoreAsync(Incident report, Guid? actorUserId, string actorRole, string reason, CancellationToken cancellationToken,
        AuditAction action = AuditAction.ReportUnlinked)
    {
        var parentId = report.DuplicateOfIncidentId!.Value;
        var photo = report.MediaItems.FirstOrDefault(m => m.MediaType == MediaType.Photo)?.MediaUrl;
        var now = DateTime.UtcNow;

        // Giữ CreatedAt gốc → quay lại hàng đợi đúng vị trí theo thời điểm báo
        report.Status = photo != null ? IncidentStatus.AiProcessing : IncidentStatus.Pending;
        report.DuplicateOfIncidentId = null;
        report.LinkedAt = null;
        report.UpdatedAt = now;

        await _incidentRepository.AddStatusHistoryAsync(new IncidentStatusHistory
        {
            IncidentId = report.Id,
            OldStatus = IncidentStatus.Linked,
            NewStatus = report.Status,
            ChangedByUserId = actorUserId,
            ChangedByRole = actorRole,
            Notes = reason,
            ChangedAt = now
        }, cancellationToken);
        await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
        {
            IncidentId = report.Id,
            ActorId = actorUserId,
            ActorRole = actorRole,
            Action = action,
            Description = $"Tách khỏi sự cố #{ShortId(parentId)}: {reason}",
            OldStateJson = JsonSerializer.Serialize(new { status = IncidentStatus.Linked.ToString(), parentIncidentId = parentId }),
            NewStateJson = JsonSerializer.Serialize(new { status = report.Status.ToString() }),
            IpAddress = _currentUser.IpAddress
        }, cancellationToken);
        await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
        {
            IncidentId = parentId,
            ActorId = actorUserId,
            ActorRole = actorRole,
            Action = action,
            Description = $"Báo cáo #{ShortId(report.Id)} được tách thành sự cố riêng: {reason}",
            NewStateJson = JsonSerializer.Serialize(new { unlinkedReportId = report.Id })
        }, cancellationToken);
        await _incidentRepository.SaveChangesAsync(cancellationToken);
        if (action != AuditAction.LinkedReportsRestored)
        {
            await _incidentRepository.AdjustLinkedReportCountAsync(parentId, -1, null, cancellationToken);
        }

        if (photo != null)
        {
            await _aiQueue.EnqueueAsync(new AiAnalysisJob(report.Id, photo, report.Description, now), cancellationToken);
        }

        await _realtimeNotifier.IncidentCreatedAsync(IncidentService.MapToResponseDto(report), cancellationToken);
        if (action != AuditAction.LinkedReportsRestored)
        {
            await PublishParentAsync(parentId, cancellationToken);
        }

        _logger.LogInformation("Tách báo cáo {ReportId} khỏi sự cố {ParentId} ({Role}): {Reason}", report.Id, parentId, actorRole, reason);
    }

    private async Task CloseAsDuplicateAsync(Incident report, Guid? actorUserId, string actorRole, string note, AuditAction action, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        report.Status = IncidentStatus.Cancelled;
        report.CancellationReason = CancellationReason.Duplicate;
        report.CancelledAt = now;
        report.CancelledByUserId = actorUserId;
        report.CancellationNote = note.Length > 500 ? note[..500] : note;
        report.UpdatedAt = now;

        await _incidentRepository.AddStatusHistoryAsync(new IncidentStatusHistory
        {
            IncidentId = report.Id,
            OldStatus = IncidentStatus.Linked,
            NewStatus = IncidentStatus.Cancelled,
            ChangedByUserId = actorUserId,
            ChangedByRole = actorRole,
            Notes = note,
            ChangedAt = now
        }, cancellationToken);
        await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
        {
            IncidentId = report.Id,
            ActorId = actorUserId,
            ActorRole = actorRole,
            Action = action,
            Description = note,
            OldStateJson = JsonSerializer.Serialize(new { status = IncidentStatus.Linked.ToString() }),
            NewStateJson = JsonSerializer.Serialize(new { status = IncidentStatus.Cancelled.ToString(), cancellationReason = CancellationReason.Duplicate.ToString(), parentIncidentId = report.DuplicateOfIncidentId }),
            IpAddress = _currentUser.IpAddress
        }, cancellationToken);
        await _incidentRepository.SaveChangesAsync(cancellationToken);
        await _realtimeNotifier.IncidentUpdatedAsync(IncidentService.MapToResponseDto(report), cancellationToken);
    }

    private async Task PublishParentAsync(Guid parentId, CancellationToken cancellationToken)
    {
        var parent = await _incidentRepository.GetIncidentWithDetailsAsync(parentId, cancellationToken);
        if (parent != null)
        {
            await _incidentRepository.ReloadAsync(parent, cancellationToken);
            await _realtimeNotifier.IncidentUpdatedAsync(IncidentService.MapToResponseDto(parent), cancellationToken);
        }
    }

    /// <summary>
    /// Người báo (tài khoản hoặc khách có mã) hoặc trung tâm điều phối trạm phụ trách
    /// </summary>
    private async Task<(string Role, ApiResponseDto<IncidentResponseDto>? Error)> ResolveReporterOrCommandCenterAsync(Incident incident, Guid? actorUserId, string? guestToken)
    {
        if (_currentUser.Role is UserRole.Operator or UserRole.Admin)
        {
            var scopeError = _currentUser.CheckIncident(incident.ResponsibleStationId);
            return scopeError != null
                ? (string.Empty, ApiResponseDto<IncidentResponseDto>.ForbiddenResult(scopeError))
                : (_currentUser.Role.ToString()!, null);
        }

        var (role, error) = await ResolveReporterAsync(incident, actorUserId, guestToken);
        return error != null ? (role, ApiResponseDto<IncidentResponseDto>.ForbiddenResult(error.Message)) : (role, null);
    }

    private async Task<(string Role, ApiResponseDto<IncidentSupplementDto>? Error)> ResolveReporterAsync(Incident incident, Guid? actorUserId, string? guestToken)
    {
        if (actorUserId.HasValue)
        {
            var user = await _userRepository.GetByIdAsync(actorUserId.Value);
            if (user != null && incident.ReportedByUserId == user.Id)
            {
                return (user.Role.ToString(), null);
            }
        }
        else if (incident.ReportedByUserId == null && _guestTokens.Validate(incident.Id, guestToken))
        {
            return (GuestRole, null);
        }

        return (string.Empty, ApiResponseDto<IncidentSupplementDto>.ForbiddenResult("Chỉ người báo sự cố này mới được thực hiện thao tác."));
    }

    private static IReadOnlyCollection<Guid> ActiveUnitStations(Incident incident)
        => incident.DispatchAssignments.Where(a => a.IsActive && a.RescueUnit != null)
            .Select(a => a.RescueUnit!.StationId).Distinct().ToList();

    private static IncidentSupplementDto MapSupplement(IncidentSupplement s, Guid targetIncidentId, string? reporterName) => new()
    {
        Id = s.Id,
        IncidentId = s.IncidentId,
        TargetIncidentId = targetIncidentId,
        Note = s.Note,
        MediaUrls = s.MediaUrls,
        ReporterName = reporterName,
        CreatedAt = s.CreatedAt
    };

    private static string ShortId(Guid id) => id.ToString("N")[..8].ToUpperInvariant();
}
