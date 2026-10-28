using System.Text.Json;
using EmergencyDispatch.Application.Localization;
using EmergencyDispatch.Application.DTOs.Ai;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.DTOs.Notification;
using EmergencyDispatch.Application.DTOs.Security;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Application.Services;

public class IncidentService : IIncidentService
{
    private const string AiWorkerRole = "AI_Worker";
    private const string GuestRole = "Guest";
    private const int MinOverrideReasonLength = 5;
    private static readonly TimeSpan SoftLockDuration = TimeSpan.FromMinutes(2);

    private readonly IIncidentRepository _incidentRepository;
    private readonly IUserRepository _userRepository;
    private readonly IBlockedPhoneRepository _blockedPhoneRepository;
    private readonly IAiClassificationService _aiService;
    private readonly IAiAnalysisQueue _aiQueue;
    private readonly IRealtimeNotifier _realtimeNotifier;
    private readonly INotificationService _notificationService;
    private readonly IValidator<CreateIncidentDto> _validator;
    private readonly ILogger<IncidentService> _logger;
    private readonly IGuestCancellationTokenService _guestTokens;
    private readonly IStationRepository _stationRepository;
    private readonly ISystemSettingsService _settingsService;
    private readonly ICurrentUserContext _currentUser;

    // Đội cứu hộ vẫn xem được sự cố trong 30 phút sau khi hoàn tất để nộp báo cáo nghiệm thu
    private static readonly TimeSpan StaffAccessAfterCompletion = TimeSpan.FromMinutes(30);

    public IncidentService(
        IIncidentRepository incidentRepository,
        IUserRepository userRepository,
        IBlockedPhoneRepository blockedPhoneRepository,
        IAiClassificationService aiService,
        IAiAnalysisQueue aiQueue,
        IRealtimeNotifier realtimeNotifier,
        INotificationService notificationService,
        IValidator<CreateIncidentDto> validator,
        ILogger<IncidentService> logger,
        IGuestCancellationTokenService guestTokens,
        IStationRepository stationRepository,
        ISystemSettingsService settingsService,
        ICurrentUserContext currentUser)
    {
        _stationRepository = stationRepository;
        _settingsService = settingsService;
        _currentUser = currentUser;
        _incidentRepository = incidentRepository;
        _userRepository = userRepository;
        _blockedPhoneRepository = blockedPhoneRepository;
        _aiService = aiService;
        _aiQueue = aiQueue;
        _realtimeNotifier = realtimeNotifier;
        _notificationService = notificationService;
        _validator = validator;
        _logger = logger;
        _guestTokens = guestTokens;
    }

    public async Task<ApiResponseDto<IncidentResponseDto>> CreateIncidentAsync(
        CreateIncidentDto dto,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Validation Logic
        var validationResult = await _validator.ValidateAsync(dto, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors.Select(e => e.ErrorMessage).ToList();
            return ApiResponseDto<IncidentResponseDto>.FailureResult(errors, "Dữ liệu báo cáo sự cố không hợp lệ.");
        }

        // 2. Khởi tạo thực thể Incident
        var title = !string.IsNullOrWhiteSpace(dto.Title)
            ? dto.Title.Trim()
            : $"Sự cố tại {dto.LocationAddress}";

        User? reportingUser = null;

        if (userId.HasValue)
        {
            reportingUser = await _userRepository.GetByIdWithDetailsAsync(userId.Value);
            if (reportingUser != null)
            {
                if (string.IsNullOrWhiteSpace(dto.ReporterName))
                {
                    dto.ReporterName = reportingUser.FullName;
                }
                if (string.IsNullOrWhiteSpace(dto.ReporterPhone))
                {
                    dto.ReporterPhone = reportingUser.PhoneNumber;
                }
            }
        }

        // FR-OP-03 / BR-05: chặn SOS từ số điện thoại đang trong danh sách đen
        var normalizedPhone = PhoneMasking.Normalize(dto.ReporterPhone);
        var now = DateTime.UtcNow;
        if (normalizedPhone != null)
        {
            var activeBlock = await _blockedPhoneRepository.GetActiveBlockAsync(normalizedPhone, now, cancellationToken);
            if (activeBlock != null)
            {
                return ApiResponseDto<IncidentResponseDto>.ForbiddenResult(
                    "Số điện thoại này đang bị tạm khóa chức năng gửi SOS do báo tin không đúng sự thật. Nếu khẩn cấp, vui lòng gọi trực tiếp 114 / 115.");
            }
        }

        // BR-CIT-01: mỗi người dùng / số điện thoại tối đa 1 sự cố đang hoạt động trong 3 phút
        if (await _incidentRepository.HasRecentActiveIncidentAsync(userId, normalizedPhone, now.AddMinutes(-3), cancellationToken))
        {
            return ApiResponseDto<IncidentResponseDto>.TooManyRequestsResult(
                "Bạn vừa gửi một yêu cầu cứu hộ đang được xử lý. Vui lòng chờ 3 phút hoặc gọi trực tiếp 114 / 115 nếu cần hỗ trợ thêm.");
        }

        // Trạm phụ trách theo vị trí (mỗi Operator xử lý sự cố của trạm mình) + kiểm tra khu vực phục vụ
        var responsibleStation = StationAssignment.PickResponsibleStation(
            await _stationRepository.GetAllAsync(s => s.IsActive), dto.Latitude, dto.Longitude);
        var serviceArea = await _settingsService.GetServiceAreaAsync(cancellationToken);
        var outsideKm = StationAssignment.DistanceOutsideArea(serviceArea, dto.Latitude, dto.Longitude);

        var incident = new Incident
        {
            ResponsibleStationId = responsibleStation?.Id,
            ResponsibleStation = responsibleStation,
            IsOutOfServiceArea = outsideKm > 0,
            DistanceOutsideAreaKm = outsideKm > 0 ? outsideKm : null,
            Title = title,
            Description = dto.Description?.Trim() ?? string.Empty,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            LocationAddress = dto.LocationAddress.Trim(),
            ReporterName = dto.ReporterName?.Trim(),
            ReporterPhone = normalizedPhone ?? dto.ReporterPhone?.Trim(),
            ReportedByUserId = userId,
            ReportedByUser = reportingUser,
            Status = IncidentStatus.Pending,
            Severity = SeverityLevel.Unclassified,
            CreatedAt = DateTime.UtcNow
        };

        // 3. Thêm các tệp tin hình ảnh/video đính kèm
        if (dto.MediaUrls != null && dto.MediaUrls.Any())
        {
            foreach (var url in dto.MediaUrls)
            {
                if (string.IsNullOrWhiteSpace(url)) continue;

                var isVideo = url.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                              url.EndsWith(".mov", StringComparison.OrdinalIgnoreCase);

                incident.MediaItems.Add(new IncidentMedia
                {
                    MediaUrl = url.Trim(),
                    MediaType = isVideo ? MediaType.Video : MediaType.Photo,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        // 4. Xác định ảnh hiện trường cho AI (VLM chỉ nhận ảnh, ưu tiên Photo)
        var mediaToAnalyze = incident.MediaItems.FirstOrDefault(m => m.MediaType == MediaType.Photo)?.MediaUrl;
        var willAnalyze = !string.IsNullOrWhiteSpace(mediaToAnalyze);

        incident.Status = willAnalyze ? IncidentStatus.AiProcessing : IncidentStatus.Pending;
        incident.Severity = SeverityLevel.Unclassified;

        var actorRole = reportingUser?.Role.ToString() ?? GuestRole;

        incident.StatusHistories.Add(new IncidentStatusHistory
        {
            IncidentId = incident.Id,
            OldStatus = IncidentStatus.Pending,
            NewStatus = incident.Status,
            ChangedByUserId = userId,
            ChangedByRole = actorRole,
            Notes = willAnalyze ? "Tiếp nhận SOS, chuyển AI phân tích nền." : "Tiếp nhận SOS không kèm ảnh, chờ Operator đánh giá thủ công.",
            ChangedAt = incident.CreatedAt
        });

        incident.AuditTrails.Add(new IncidentAuditTrail
        {
            IncidentId = incident.Id,
            ActorId = userId,
            ActorRole = actorRole,
            Action = AuditAction.Created,
            Description = "Tiếp nhận báo cáo SOS",
            NewStateJson = JsonSerializer.Serialize(new
            {
                status = incident.Status.ToString(),
                mediaCount = incident.MediaItems.Count,
                queuedForAi = willAnalyze
            }),
            Timestamp = incident.CreatedAt
        });

        if (incident.IsOutOfServiceArea)
        {
            incident.AuditTrails.Add(new IncidentAuditTrail
            {
                IncidentId = incident.Id,
                ActorRole = "System",
                Action = AuditAction.OutOfServiceArea,
                Description = $"Vị trí báo cáo nằm ngoài khu vực phục vụ \"{serviceArea.Name}\" {outsideKm:F1} km. Vẫn tiếp nhận, giao trạm gần nhất {responsibleStation?.Name ?? "(chưa có trạm)"}.",
                Timestamp = incident.CreatedAt
            });
        }

        // 5. Lưu vào cơ sở dữ liệu (không chờ AI → phản hồi nhanh cho người báo)
        await _incidentRepository.AddAsync(incident);

        // 6. Đẩy công việc AI vào hàng đợi nền (System.Threading.Channels)
        if (willAnalyze)
        {
            await _aiQueue.EnqueueAsync(
                new AiAnalysisJob(incident.Id, mediaToAnalyze!, dto.Description, DateTime.UtcNow),
                cancellationToken);

            _logger.LogInformation("Sự cố {IncidentId} đã vào hàng đợi phân tích AI.", incident.Id);
        }
        else
        {
            _logger.LogInformation("Sự cố {IncidentId} không có ảnh hiện trường. Giữ mức Unclassified (Level 0).", incident.Id);
        }

        // 7. Map kết quả trả về + đẩy realtime tới Operator
        // Operator nhận bản đã che số (BR-32); người báo nhận bản đầy đủ của chính mình
        await _realtimeNotifier.IncidentCreatedAsync(MapToResponseDto(incident), cancellationToken);

        if (incident.IsOutOfServiceArea)
        {
            await _realtimeNotifier.OutOfAreaAlertAsync(new OutOfAreaAlertDto
            {
                IncidentId = incident.Id,
                IncidentTitle = incident.Title,
                LocationAddress = incident.LocationAddress,
                Latitude = incident.Latitude,
                Longitude = incident.Longitude,
                DistanceOutsideAreaKm = outsideKm,
                ServiceAreaName = serviceArea.Name,
                ResponsibleStationId = responsibleStation?.Id,
                ResponsibleStationName = responsibleStation?.Name,
                CreatedAt = incident.CreatedAt
            }, cancellationToken);

            await NotifyCommandCenterAsync(responsibleStation?.Id, new NotificationRequest
            {
                Type = NotificationType.System,
                Title = "SOS NGOÀI KHU VỰC PHỤC VỤ",
                Message = $"{incident.Title} — {incident.LocationAddress} nằm ngoài \"{serviceArea.Name}\" {outsideKm:F1} km. Cần xác minh và phối hợp đơn vị ngoài khu vực nếu cần.",
                IncidentId = incident.Id,
                HighPriority = true
            }, cancellationToken);
        }
        var responseDto = MapToResponseDto(incident, revealPhone: true);

        // Return the secret only to the creator, after publishing the realtime event.
        if (!userId.HasValue) responseDto.GuestCancellationToken = _guestTokens.Create(incident.Id);
        return ApiResponseDto<IncidentResponseDto>.SuccessResult(
            responseDto,
            willAnalyze
                ? "Đã tiếp nhận SOS. AI đang phân tích mức độ nguy hiểm, kết quả sẽ được cập nhật realtime."
                : "Đã tiếp nhận SOS. Điều phối viên sẽ đánh giá sự cố ngay.");
    }

    public async Task ProcessAiAnalysisAsync(AiAnalysisJob job, CancellationToken cancellationToken = default)
    {
        var incident = await _incidentRepository.GetIncidentWithDetailsAsync(job.IncidentId, cancellationToken);
        if (incident == null)
        {
            _logger.LogWarning("AI Worker: không tìm thấy sự cố {IncidentId}, bỏ qua.", job.IncidentId);
            return;
        }

        if (incident.AiClassification != null)
        {
            _logger.LogInformation("AI Worker: sự cố {IncidentId} đã có kết quả AI, bỏ qua.", job.IncidentId);
            return;
        }

        var startedAt = DateTime.UtcNow;
        var queueWaitMs = (long)(startedAt - job.EnqueuedAtUtc).TotalMilliseconds;

        // Gọi AI (đã có Fallback nội bộ → không ném exception khi AI lỗi)
        var aiResult = await _aiService.AnalyzeAsync(job.MediaUrl, job.AdditionalContext, cancellationToken);

        var aiEntity = new AiClassification
        {
            IncidentId = incident.Id,
            SeverityScore = aiResult.SeverityLevel,
            EventType = aiResult.EventType,
            IsTrafficJam = aiResult.IsTrafficJam,
            HazardTags = aiResult.HazardTags,
            Summary = aiResult.Summary,
            ConfidenceScore = aiResult.ConfidenceScore,
            ModelName = aiResult.ModelName,
            RawResponse = aiResult.RawResponse,
            IsSuccess = aiResult.IsSuccess,
            ErrorMessage = aiResult.ErrorMessage,
            ProcessingDurationMs = aiResult.ProcessingDurationMs,
            CreatedAt = DateTime.UtcNow
        };
        await _incidentRepository.AddAiClassificationAsync(aiEntity, cancellationToken);

        // Chỉ cập nhật trạng thái/mức độ nếu Operator chưa can thiệp (chưa xác minh / chưa hủy).
        // Kiểm tra trên DB chứ không trên bản đọc lúc đầu: AI chạy 17–180s, trong lúc đó Operator có thể đã xác minh.
        var appliedToIncident = await _incidentRepository.TryApplyAiResultAsync(
            incident.Id, aiResult.SeverityLevel, aiResult.EventType, DateTime.UtcNow, cancellationToken);
        await _incidentRepository.ReloadAsync(incident, cancellationToken);
        if (appliedToIncident)
        {
            await _incidentRepository.AddStatusHistoryAsync(new IncidentStatusHistory
            {
                IncidentId = incident.Id,
                OldStatus = IncidentStatus.AiProcessing,
                NewStatus = IncidentStatus.AiProcessed,
                ChangedByRole = AiWorkerRole,
                Notes = aiResult.IsSuccess
                    ? $"AI đánh giá mức độ {aiResult.SeverityLevel}."
                    : "AI lỗi → Fallback Unclassified, cần Operator đánh giá thủ công."
            }, cancellationToken);
        }

        // Telemetry RQ3: T_AI (end-to-end từ lúc tạo sự cố) + độ trễ hàng đợi + độ trễ mô hình
        var tAiTotalMs = (long)(DateTime.UtcNow - incident.CreatedAt).TotalMilliseconds;
        await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
        {
            IncidentId = incident.Id,
            ActorRole = AiWorkerRole,
            Action = AuditAction.AiAnalyzed,
            Description = appliedToIncident
                ? "AI phân tích hoàn tất"
                : $"AI phân tích hoàn tất nhưng sự cố đã ở trạng thái {incident.Status}, chỉ lưu kết quả tham khảo",
            NewStateJson = JsonSerializer.Serialize(new
            {
                severity = aiResult.SeverityLevel.ToString(),
                isSuccess = aiResult.IsSuccess,
                model = aiResult.ModelName,
                modelLatencyMs = aiResult.ProcessingDurationMs,
                queueWaitMs,
                tAiTotalMs
            })
        }, cancellationToken);

        await _incidentRepository.SaveChangesAsync(cancellationToken);
        incident.AiClassification ??= aiEntity;

        _logger.LogInformation(
            "AI Worker: sự cố {IncidentId} → {Severity} (success={IsSuccess}, model={ModelMs}ms, queue={QueueMs}ms, T_AI={TotalMs}ms)",
            incident.Id, aiResult.SeverityLevel, aiResult.IsSuccess, aiResult.ProcessingDurationMs, queueWaitMs, tAiTotalMs);

        await _realtimeNotifier.IncidentUpdatedAsync(MapToResponseDto(incident, aiResult), cancellationToken);
    }

    public async Task<ApiResponseDto<IncidentResponseDto>> GetIncidentByIdAsync(
        Guid id,
        Guid? viewerUserId = null,
        CancellationToken cancellationToken = default)
    {
        var incident = await _incidentRepository.GetIncidentWithDetailsAsync(id, cancellationToken);
        if (incident == null)
        {
            return ApiResponseDto<IncidentResponseDto>.NotFoundResult("Không tìm thấy sự cố với mã yêu cầu.");
        }

        if (viewerUserId.HasValue)
        {
            var viewer = await _userRepository.GetByIdAsync(viewerUserId.Value);
            if (viewer == null || !CanAccess(incident, viewer))
            {
                return ApiResponseDto<IncidentResponseDto>.ForbiddenResult("Bạn không có quyền xem sự cố này.");
            }
        }

        // Người báo luôn thấy số của chính mình; người khác thấy số đã che (BR-32)
        var isReporter = viewerUserId.HasValue && incident.ReportedByUserId == viewerUserId;
        return ApiResponseDto<IncidentResponseDto>.SuccessResult(MapToResponseDto(incident, revealPhone: isReporter));
    }

    public async Task<ApiResponseDto<PaginatedResultDto<IncidentResponseDto>>> GetIncidentsAsync(
        IncidentFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<IncidentStatus>? statuses;
        try
        {
            statuses = filter.ParseStatuses();
        }
        catch (ArgumentException ex)
        {
            return ApiResponseDto<PaginatedResultDto<IncidentResponseDto>>.FailureResult(ex.Message);
        }

        var (items, totalCount) = await _incidentRepository.GetFilteredIncidentsAsync(
            filter.Status,
            filter.Severity,
            filter.SearchTerm,
            filter.FromDate,
            filter.ToDate,
            filter.PageIndex,
            filter.PageSize,
            cancellationToken,
            _currentUser.FilterStationId(),
            statuses);

        var dtoList = items.Select(i => MapToResponseDto(i)).ToList();
        var paginated = new PaginatedResultDto<IncidentResponseDto>(dtoList, totalCount, filter.PageIndex, filter.PageSize);

        return ApiResponseDto<PaginatedResultDto<IncidentResponseDto>>.SuccessResult(paginated);
    }

    public async Task<ApiResponseDto<List<IncidentResponseDto>>> GetQueueAsync(CancellationToken cancellationToken = default)
    {
        var queueItems = await _incidentRepository.GetPendingQueueAsync(cancellationToken, _currentUser.FilterStationId());
        var dtos = queueItems.Select(i => MapToResponseDto(i)).ToList();
        return ApiResponseDto<List<IncidentResponseDto>>.SuccessResult(dtos, "Lấy danh sách hàng đợi sự cố thành công.");
    }

    public async Task<ApiResponseDto<IncidentResponseDto>> VerifyIncidentAsync(
        Guid id,
        VerifyIncidentDto dto,
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        var incident = await _incidentRepository.GetIncidentWithDetailsAsync(id, cancellationToken);
        if (incident == null)
        {
            return ApiResponseDto<IncidentResponseDto>.FailureResult("Không tìm thấy sự cố cần xác minh.");
        }

        var scopeError = _currentUser.CheckIncident(incident.ResponsibleStationId);
        if (scopeError != null)
        {
            return ApiResponseDto<IncidentResponseDto>.ForbiddenResult(scopeError);
        }

        // Cho phép Operator xác minh ngay cả khi AI chưa xong (Human-in-the-loop luôn được ưu tiên),
        // kể cả sự cố đã bị leo thang khi chưa ai duyệt
        var awaitingVerification = incident.Status is IncidentStatus.Pending or IncidentStatus.AiProcessing or IncidentStatus.AiProcessed
            || (incident.Status == IncidentStatus.Escalated && incident.VerifiedAt == null);
        if (!awaitingVerification)
        {
            return ApiResponseDto<IncidentResponseDto>.FailureResult(LocalizationHelper.Localize("Sự cố đang ở trạng thái '{0}', không thể xác minh lại.", incident.Status));
        }

        // BR-15: không được thao tác khi Operator khác đang giữ khóa
        if (incident.IsLockedByOther(operatorId, DateTime.UtcNow))
        {
            return ApiResponseDto<IncidentResponseDto>.ConflictResult(
                LocalizationHelper.Localize("Sự cố đang được xử lý bởi {0}. Vui lòng chờ khóa hết hạn.",
                    incident.LockedByOperator?.FullName ?? LocalizationHelper.Localize("điều phối viên khác")));
        }

        var oldStatus = incident.Status;
        var aiSeverity = incident.AiClassification?.SeverityScore;

        // BR-03 (MSG25): ghi đè mức độ AI đề xuất, hoặc tự đánh giá ca chưa phân loại (Level 0 / không có AI), bắt buộc kèm lý do
        var isOverride = aiSeverity.HasValue && aiSeverity.Value != dto.ConfirmedSeverity;
        var manualAssessment = incident.Severity == SeverityLevel.Unclassified;
        if ((isOverride || manualAssessment) && (dto.OperatorNotes?.Trim().Length ?? 0) < MinOverrideReasonLength)
        {
            return ApiResponseDto<IncidentResponseDto>.FailureResult(isOverride
                ? LocalizationHelper.Localize("Bạn đang thay đổi mức độ AI đề xuất ({0} → {1}). Vui lòng nhập lý do ghi đè (tối thiểu {2} ký tự) vào operatorNotes.", aiSeverity.GetValueOrDefault(), dto.ConfirmedSeverity, MinOverrideReasonLength)
                : LocalizationHelper.Localize("Sự cố chưa được AI phân loại. Vui lòng nhập căn cứ đánh giá mức độ (tối thiểu {0} ký tự) vào operatorNotes.", MinOverrideReasonLength));
        }

        // BR-13: loại sự cố quyết định chủng loại xe; AI chưa xác định thì Operator phải chọn
        var aiEventType = incident.AiClassification?.EventType ?? IncidentEventType.None;
        var currentEventType = incident.EventType != IncidentEventType.None ? incident.EventType : aiEventType;
        var confirmedEventType = dto.ConfirmedEventType is { } chosen && chosen != IncidentEventType.None ? chosen : currentEventType;
        if (confirmedEventType == IncidentEventType.None)
        {
            return ApiResponseDto<IncidentResponseDto>.FailureResult(
                "AI chưa xác định được loại sự cố. Vui lòng chọn confirmedEventType: FireExplosion (cháy nổ) hoặc TrafficAccident (tai nạn).");
        }
        var eventTypeOverridden = aiEventType != IncidentEventType.None && aiEventType != confirmedEventType;

        // Cập nhật thông tin xác minh bởi Operator (Human-in-the-loop)
        incident.Severity = dto.ConfirmedSeverity;
        incident.EventType = confirmedEventType;
        incident.Status = IncidentStatus.Verified;
        incident.VerifiedByUserId = operatorId;
        incident.VerifiedAt = DateTime.UtcNow;
        incident.OperatorNotes = dto.OperatorNotes?.Trim();
        incident.UpdatedAt = DateTime.UtcNow;

        // Telemetry RQ3: T_Operator = thời gian từ lúc tạo sự cố đến khi Operator xác minh
        incident.ResponseTimeMs = (long)(incident.VerifiedAt.Value - incident.CreatedAt).TotalMilliseconds;

        if (!string.IsNullOrWhiteSpace(dto.AdjustedTitle))
        {
            incident.Title = dto.AdjustedTitle.Trim();
        }

        var severityOverridden = isOverride || eventTypeOverridden;

        await _incidentRepository.AddStatusHistoryAsync(new IncidentStatusHistory
        {
            IncidentId = incident.Id,
            OldStatus = oldStatus,
            NewStatus = IncidentStatus.Verified,
            ChangedByUserId = operatorId,
            ChangedByRole = UserRole.Operator.ToString(),
            Notes = incident.OperatorNotes
        }, cancellationToken);

        await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
        {
            IncidentId = incident.Id,
            ActorId = operatorId,
            ActorRole = UserRole.Operator.ToString(),
            Action = severityOverridden ? AuditAction.SeverityOverridden : AuditAction.Verified,
            Description = severityOverridden
                ? $"Operator ghi đè đánh giá AI: mức độ {aiSeverity?.ToString() ?? "—"} → {dto.ConfirmedSeverity}, loại {aiEventType} → {confirmedEventType}"
                : manualAssessment ? $"Operator tự đánh giá sự cố chưa phân loại: {dto.ConfirmedSeverity}, {confirmedEventType}" : "Operator xác minh sự cố",
            OldStateJson = JsonSerializer.Serialize(new { status = oldStatus.ToString(), aiSeverity = aiSeverity?.ToString(), aiEventType = aiEventType.ToString() }),
            NewStateJson = JsonSerializer.Serialize(new
            {
                status = IncidentStatus.Verified.ToString(),
                severity = dto.ConfirmedSeverity.ToString(),
                eventType = confirmedEventType.ToString(),
                tOperatorMs = incident.ResponseTimeMs
            })
        }, cancellationToken);

        // Sự cố đang được theo dõi bởi DbContext → chỉ cần SaveChanges
        await _incidentRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Operator {OperatorId} đã xác minh sự cố {IncidentId} với mức độ {Severity} (T_Operator={Ms}ms)",
            operatorId, id, dto.ConfirmedSeverity, incident.ResponseTimeMs);

        var responseDto = MapToResponseDto(incident);
        await _realtimeNotifier.IncidentUpdatedAsync(responseDto, cancellationToken);

        // FR-CIT-04: "Emergency report verified. Units are being mobilized."
        if (incident.ReportedByUserId.HasValue)
        {
            await _notificationService.NotifyUsersAsync(new[] { incident.ReportedByUserId.Value }, new NotificationRequest
            {
                Type = NotificationType.StatusUpdate,
                Title = "Sự cố đã được xác minh",
                Message = "Trung tâm đã xác minh báo cáo của bạn và đang điều động lực lượng cứu hộ.",
                IncidentId = incident.Id
            }, cancellationToken);
        }

        return ApiResponseDto<IncidentResponseDto>.SuccessResult(
            responseDto,
            "Xác minh sự cố thành công. Sự cố sẵn sàng để điều phối đội cứu hộ.");
    }

    public async Task<ApiResponseDto<IncidentResponseDto>> CancelIncidentAsync(
        Guid id,
        string reason,
        Guid? cancelledByUserId = null,
        CancellationToken cancellationToken = default,
        string? guestCancellationToken = null,
        CancellationReason? reasonCode = null)
    {
        var incident = await _incidentRepository.GetIncidentWithDetailsAsync(id, cancellationToken);
        if (incident == null)
        {
            return ApiResponseDto<IncidentResponseDto>.FailureResult("Không tìm thấy sự cố cần hủy.");
        }

        var cancelScopeError = _currentUser.CheckIncident(incident.ResponsibleStationId);
        if (cancelScopeError != null)
        {
            return ApiResponseDto<IncidentResponseDto>.ForbiddenResult(cancelScopeError);
        }

        // Kiểm tra trạng thái: Không được hủy sự cố đã hoàn tất hoặc đã bị hủy trước đó
        if (incident.Status == IncidentStatus.Completed)
        {
            return ApiResponseDto<IncidentResponseDto>.FailureResult("Không thể hủy sự cố đã hoàn tất xử lý hiện trường.");
        }
        if (incident.Status == IncidentStatus.Cancelled)
        {
            return ApiResponseDto<IncidentResponseDto>.FailureResult("Sự cố này đã được hủy trước đó.");
        }

        var cancellingUser = cancelledByUserId.HasValue
            ? await _userRepository.GetByIdAsync(cancelledByUserId.Value)
            : null;
        if (cancellingUser == null)
        {
            return ApiResponseDto<IncidentResponseDto>.FailureResult("Bạn cần đăng nhập để hủy sự cố.");
        }

        var isCommandCenter = cancellingUser.Role is UserRole.Operator or UserRole.Admin;
        if (isCommandCenter && incident.IsLockedByOther(cancellingUser.Id, DateTime.UtcNow))
        {
            return ApiResponseDto<IncidentResponseDto>.ConflictResult(
                LocalizationHelper.Localize("Sự cố đang được xử lý bởi {0}. Vui lòng chờ khóa hết hạn.",
                    incident.LockedByOperator?.FullName ?? LocalizationHelper.Localize("điều phối viên khác")));
        }

        if (!isCommandCenter)
        {
            // Người dân chỉ được hủy sự cố do chính mình báo cáo
            if (incident.ReportedByUserId != cancellingUser.Id)
            {
                return ApiResponseDto<IncidentResponseDto>.FailureResult("Bạn không có quyền hủy sự cố được báo cáo bởi người khác.");
            }

            // BR-08 / BR-CIT-03 (MSG20): khóa tự hủy khi đội cứu hộ đã được điều động
            var unitsMobilized = incident.Status is IncidentStatus.Dispatched or IncidentStatus.InProgress
                || (incident.DispatchAssignments != null && incident.DispatchAssignments.Any(a => a.IsActive));
            if (unitsMobilized)
            {
                return ApiResponseDto<IncidentResponseDto>.FailureResult(
                    "Đội cứu hộ đang trên đường tới vị trí của bạn. Bạn không thể tự hủy sự cố, vui lòng liên hệ Trung tâm điều phối qua số 115.");
            }
        }

        var code = isCommandCenter
            ? reasonCode is { } rc && rc is not (CancellationReason.CitizenCancelled or CancellationReason.AutoExpired) ? rc : CancellationReason.Other
            : CancellationReason.CitizenCancelled;
        return await CancelCoreAsync(incident, reason, code, cancellingUser, cancellationToken);
    }

    public async Task<int> AutoExpireStaleIncidentsAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        var ids = await _incidentRepository.GetStaleUnverifiedIdsAsync(DateTime.UtcNow - maxAge, cancellationToken);
        var count = 0;
        foreach (var id in ids)
        {
            var incident = await _incidentRepository.GetIncidentWithDetailsAsync(id, cancellationToken);
            if (incident == null || incident.Status is IncidentStatus.Completed or IncidentStatus.Cancelled) continue;

            var result = await CancelCoreAsync(incident,
                $"Tự động hủy: không được xác minh sau {maxAge.TotalHours:0.#} giờ.", CancellationReason.AutoExpired, null, cancellationToken);
            if (result.Success) count++;
        }
        return count;
    }

    /// <summary>
    /// Hủy sự cố: thu hồi xe đang làm nhiệm vụ, ghi lịch sử + audit bất biến, phát realtime.
    /// Không đụng VerifiedAt / VerifiedByUserId / OperatorNotes để giữ đúng số liệu RQ3.
    /// </summary>
    private async Task<ApiResponseDto<IncidentResponseDto>> CancelCoreAsync(
        Incident incident,
        string reason,
        CancellationReason code,
        User? actor,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var actorRole = actor?.Role.ToString() ?? "System";
        var actorName = actor != null ? $"{actor.Role} {actor.FullName}" : "Hệ thống";
        var note = reason.Trim();
        if (note.Length > 500) note = note[..500];

        // Chỉ thu hồi các lượt đang hoạt động (lượt đã từ chối / hoàn tất giữ nguyên, không đụng tới xe của chúng)
        var recalledUnits = new List<RescueUnit>();
        foreach (var assignment in incident.DispatchAssignments.Where(a => a.IsActive))
        {
            assignment.Status = DispatchAssignmentStatus.Cancelled;
            assignment.Notes = LocalizationHelper.Localize("[THU HỒI XE]: Sự cố bị hủy bởi {0}. Lý do: {1}", actorName, note);
            assignment.UpdatedAt = now;

            if (assignment.RescueUnit is { } unit
                && unit.Status is RescueUnitStatus.Dispatched or RescueUnitStatus.OnScene or RescueUnitStatus.Returning)
            {
                unit.Status = RescueUnitStatus.Available;
                unit.UpdatedAt = now;
                recalledUnits.Add(unit);
            }
        }

        var oldStatus = incident.Status;
        incident.Status = IncidentStatus.Cancelled;
        incident.CancellationReason = code;
        incident.CancelledByUserId = actor?.Id;
        incident.CancelledAt = now;
        incident.CancellationNote = note;
        incident.LockedByOperatorId = null;
        incident.LockExpiresAt = null;
        incident.UpdatedAt = now;

        await _incidentRepository.AddStatusHistoryAsync(new IncidentStatusHistory
        {
            IncidentId = incident.Id,
            OldStatus = oldStatus,
            NewStatus = IncidentStatus.Cancelled,
            ChangedByUserId = actor?.Id,
            ChangedByRole = actorRole,
            Notes = $"[{code}] {note}",
            ChangedAt = now
        }, cancellationToken);

        await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
        {
            IncidentId = incident.Id,
            ActorId = actor?.Id,
            ActorRole = actorRole,
            Action = AuditAction.Cancelled,
            Description = $"Hủy sự cố ({code}) bởi {actorName}. Lý do: {note}" +
                          (recalledUnits.Count > 0 ? $". Thu hồi {recalledUnits.Count} xe: {string.Join(", ", recalledUnits.Select(u => u.PlateNumber))}" : string.Empty),
            OldStateJson = JsonSerializer.Serialize(new { status = oldStatus.ToString() }),
            NewStateJson = JsonSerializer.Serialize(new
            {
                status = IncidentStatus.Cancelled.ToString(),
                cancellationReason = code.ToString(),
                recalledUnits = recalledUnits.Select(u => u.PlateNumber)
            }),
            Timestamp = now
        }, cancellationToken);

        await _incidentRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Sự cố {IncidentId} đã bị hủy ({Code}) bởi {Actor}. Thu hồi {Count} xe. Lý do: {Reason}",
            incident.Id, code, actorName, recalledUnits.Count, note);

        var responseDto = MapToResponseDto(incident);
        await _realtimeNotifier.IncidentUpdatedAsync(responseDto, cancellationToken);
        await _realtimeNotifier.IncidentLockChangedAsync(new IncidentLockEventDto { IncidentId = incident.Id, ChangedAt = now },
            incident.ResponsibleStationId, cancellationToken);
        foreach (var unit in recalledUnits)
        {
            await _realtimeNotifier.RescueUnitStatusChangedAsync(new RescueUnitStatusEventDto
            {
                UnitType = unit.UnitType,
                Latitude = unit.CurrentLat,
                Longitude = unit.CurrentLng,
                RescueUnitId = unit.Id,
                PlateNumber = unit.PlateNumber,
                StationId = unit.StationId,
                Status = unit.Status,
                Reason = "Thu hồi do sự cố bị hủy",
                ChangedAt = now
            }, cancellationToken);
        }

        var successMessage = recalledUnits.Count > 0
            ? LocalizationHelper.Localize("Đã hủy sự cố thành công và tự động phát lệnh thu hồi {0} xe cứu hộ quay về trạm.", recalledUnits.Count)
            : "Đã hủy sự cố thành công.";
        return ApiResponseDto<IncidentResponseDto>.SuccessResult(responseDto, successMessage);
    }

    public async Task<ApiResponseDto<IncidentLockDto>> LockIncidentAsync(
        Guid id,
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        var incident = await _incidentRepository.GetIncidentWithDetailsAsync(id, cancellationToken);
        if (incident == null)
        {
            return ApiResponseDto<IncidentLockDto>.NotFoundResult("Không tìm thấy sự cố.");
        }

        var lockScopeError = _currentUser.CheckIncident(incident.ResponsibleStationId);
        if (lockScopeError != null)
        {
            return ApiResponseDto<IncidentLockDto>.ForbiddenResult(lockScopeError);
        }

        if (incident.Status is IncidentStatus.Completed or IncidentStatus.Cancelled)
        {
            return ApiResponseDto<IncidentLockDto>.FailureResult(LocalizationHelper.Localize("Sự cố đã ở trạng thái '{0}', không cần khóa.", incident.Status));
        }

        var now = DateTime.UtcNow;
        var expiresAt = now.Add(SoftLockDuration);
        var acquired = await _incidentRepository.TryAcquireLockAsync(id, operatorId, expiresAt, now, cancellationToken);
        if (!acquired)
        {
            return ApiResponseDto<IncidentLockDto>.ConflictResult(
                LocalizationHelper.Localize("Sự cố đang được xử lý bởi {0}.", incident.LockedByOperator?.FullName ?? LocalizationHelper.Localize("điều phối viên khác")));
        }

        var operatorUser = await _userRepository.GetByIdAsync(operatorId);
        await _realtimeNotifier.IncidentLockChangedAsync(new IncidentLockEventDto
        {
            IncidentId = id,
            LockedByOperatorId = operatorId,
            LockedByOperatorName = operatorUser?.FullName,
            LockExpiresAt = expiresAt,
            ChangedAt = now
        }, incident.ResponsibleStationId, cancellationToken);
        return ApiResponseDto<IncidentLockDto>.SuccessResult(new IncidentLockDto
        {
            IncidentId = id,
            LockedByOperatorId = operatorId,
            LockedByOperatorName = operatorUser?.FullName ?? string.Empty,
            LockExpiresAt = expiresAt
        }, "Đã giữ quyền xử lý sự cố trong 2 phút.");
    }

    public async Task<ApiResponseDto<bool>> UnlockIncidentAsync(
        Guid id,
        Guid operatorId,
        bool force,
        CancellationToken cancellationToken = default)
    {
        var released = await _incidentRepository.ReleaseLockAsync(id, operatorId, force, cancellationToken);
        if (released)
        {
            var incident = await _incidentRepository.GetByIdAsync(id);
            await _realtimeNotifier.IncidentLockChangedAsync(new IncidentLockEventDto { IncidentId = id },
                incident?.ResponsibleStationId, cancellationToken);
        }
        return ApiResponseDto<bool>.SuccessResult(released,
            released ? "Đã nhả quyền xử lý sự cố." : "Sự cố không bị khóa bởi bạn.");
    }

    public async Task<ApiResponseDto<RevealPhoneDto>> RevealReporterPhoneAsync(
        Guid id,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var incident = await _incidentRepository.GetIncidentWithDetailsAsync(id, cancellationToken);
        if (incident == null)
        {
            return ApiResponseDto<RevealPhoneDto>.NotFoundResult("Không tìm thấy sự cố.");
        }

        var revealScopeError = _currentUser.CheckIncident(incident.ResponsibleStationId);
        if (revealScopeError != null)
        {
            return ApiResponseDto<RevealPhoneDto>.ForbiddenResult(revealScopeError);
        }

        var actor = await _userRepository.GetByIdAsync(actorUserId);
        if (actor == null || !await CanSeeFullPhoneAsync(incident, actor))
        {
            return ApiResponseDto<RevealPhoneDto>.ForbiddenResult("Bạn không có quyền xem số điện thoại của người báo sự cố này.");
        }

        var phone = incident.ReporterPhone ?? incident.ReportedByUser?.PhoneNumber;

        // BR-32: ghi vết bất biến mỗi lần xem số đầy đủ
        await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
        {
            IncidentId = incident.Id,
            ActorId = actor.Id,
            ActorRole = actor.Role.ToString(),
            Action = AuditAction.PhoneRevealed,
            Description = $"{actor.FullName} xem số điện thoại đầy đủ của người báo",
            NewStateJson = JsonSerializer.Serialize(new { maskedPhone = PhoneMasking.Mask(phone) })
        }, cancellationToken);
        await _incidentRepository.SaveChangesAsync(cancellationToken);

        return ApiResponseDto<RevealPhoneDto>.SuccessResult(new RevealPhoneDto
        {
            IncidentId = incident.Id,
            PhoneNumber = phone
        }, "Đã ghi nhận lượt xem số điện thoại.");
    }

    public async Task<ApiResponseDto<BlockedNumberDto>> BlockReporterAsync(
        Guid id,
        BlockReporterDto dto,
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        var incident = await _incidentRepository.GetIncidentWithDetailsAsync(id, cancellationToken);
        if (incident == null)
        {
            return ApiResponseDto<BlockedNumberDto>.NotFoundResult("Không tìm thấy sự cố.");
        }

        var blockScopeError = _currentUser.CheckIncident(incident.ResponsibleStationId);
        if (blockScopeError != null)
        {
            return ApiResponseDto<BlockedNumberDto>.ForbiddenResult(blockScopeError);
        }

        var phone = PhoneMasking.Normalize(incident.ReporterPhone ?? incident.ReportedByUser?.PhoneNumber);
        if (phone == null)
        {
            return ApiResponseDto<BlockedNumberDto>.FailureResult("Sự cố không có số điện thoại người báo để chặn.");
        }

        var now = DateTime.UtcNow;
        var existing = await _blockedPhoneRepository.GetActiveBlockAsync(phone, now, cancellationToken);
        if (existing != null)
        {
            return ApiResponseDto<BlockedNumberDto>.ConflictResult("Số điện thoại này đang trong danh sách chặn. Admin có thể gia hạn tại trang quản lý số bị chặn.");
        }

        var block = new BlockedPhoneNumber
        {
            PhoneNumber = phone,
            Duration = dto.Duration,
            BlockedAt = now,
            ExpiresAt = BlockedPhoneNumber.CalculateExpiry(dto.Duration, now),
            Reason = dto.Reason.Trim(),
            IncidentId = incident.Id,
            BlockedByUserId = operatorId
        };
        await _blockedPhoneRepository.AddAsync(block);

        var actor = await _userRepository.GetByIdAsync(operatorId);
        await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
        {
            IncidentId = incident.Id,
            ActorId = operatorId,
            ActorRole = actor?.Role.ToString() ?? UserRole.Operator.ToString(),
            Action = AuditAction.ReporterBlocked,
            Description = $"Chặn số điện thoại người báo ({dto.Duration}): {dto.Reason.Trim()}",
            NewStateJson = JsonSerializer.Serialize(new { blockId = block.Id, maskedPhone = PhoneMasking.Mask(phone), duration = dto.Duration.ToString(), block.ExpiresAt })
        }, cancellationToken);
        await _incidentRepository.SaveChangesAsync(cancellationToken);

        return ApiResponseDto<BlockedNumberDto>.SuccessResult(new BlockedNumberDto
        {
            Id = block.Id,
            MaskedPhone = PhoneMasking.Mask(phone) ?? string.Empty,
            Duration = block.Duration,
            Status = block.GetStatus(now),
            BlockedAt = block.BlockedAt,
            ExpiresAt = block.ExpiresAt,
            Reason = block.Reason,
            IncidentId = block.IncidentId,
            BlockedByName = actor?.FullName
        }, "Đã chặn số điện thoại báo khống.");
    }

    public async Task<bool> CanAccessIncidentAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null) return false;
        if (user.Role == UserRole.Admin) return true;

        var incident = await _incidentRepository.GetIncidentWithDetailsAsync(id, cancellationToken);
        return incident != null && CanAccess(incident, user);
    }

    /// <summary>
    /// Admin: mọi sự cố. Operator: sự cố trạm mình phụ trách (hoặc chưa xác định trạm), hoặc có xe trạm mình đang hỗ trợ.
    /// Citizen: sự cố mình báo. RescueStaff: chỉ khi xe trạm mình đang làm nhiệm vụ (hoặc vừa hoàn tất ≤ 30 phút).
    /// </summary>
    private static bool CanAccess(Incident incident, User user)
    {
        var now = DateTime.UtcNow;
        return user.Role switch
        {
            UserRole.Admin => true,
            UserRole.Operator => incident.ResponsibleStationId == null
                                 || incident.ResponsibleStationId == user.StationId
                                 || incident.DispatchAssignments.Any(a => a.IsActive && a.RescueUnit?.StationId == user.StationId),
            UserRole.Citizen => incident.ReportedByUserId == user.Id,
            UserRole.RescueStaff => user.StationId.HasValue
                                    && incident.DispatchAssignments.Any(a => a.RescueUnit?.StationId == user.StationId
                                        && (a.IsActive || (a.Status == DispatchAssignmentStatus.Completed
                                                           && a.CompletedAt >= now - StaffAccessAfterCompletion))),
            _ => false
        };
    }

    /// <summary>
    /// Gửi thông báo khẩn tới trung tâm chỉ huy của sự cố: Operator trạm phụ trách + Admin
    /// (trạm chưa có Operator → mọi Operator)
    /// </summary>
    private async Task NotifyCommandCenterAsync(Guid? stationId, NotificationRequest request, CancellationToken cancellationToken)
    {
        var staffRoles = await _userRepository.GetActiveByRolesAsync(new[] { UserRole.Operator, UserRole.Admin });
        var stationOperators = staffRoles.Where(u => u.Role == UserRole.Operator && stationId.HasValue && u.StationId == stationId).ToList();
        var recipients = stationOperators.Count > 0
            ? stationOperators.Concat(staffRoles.Where(u => u.Role == UserRole.Admin))
            : staffRoles;

        await _notificationService.NotifyUsersAsync(recipients.Select(u => u.Id).Distinct().ToList(), request, cancellationToken);
    }

    public async Task<ApiResponseDto<IncidentResponseDto>> TransferIncidentAsync(
        Guid id,
        TransferIncidentDto dto,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var incident = await _incidentRepository.GetIncidentWithDetailsAsync(id, cancellationToken);
        if (incident == null)
        {
            return ApiResponseDto<IncidentResponseDto>.NotFoundResult("Không tìm thấy sự cố.");
        }

        var scopeError = _currentUser.CheckIncident(incident.ResponsibleStationId);
        if (scopeError != null)
        {
            return ApiResponseDto<IncidentResponseDto>.ForbiddenResult(scopeError);
        }
        if (incident.Status is IncidentStatus.Completed or IncidentStatus.Cancelled)
        {
            return ApiResponseDto<IncidentResponseDto>.FailureResult("Không thể chuyển trạm cho sự cố đã đóng.");
        }
        var reason = dto.Reason?.Trim() ?? string.Empty;
        if (reason.Length < 5)
        {
            return ApiResponseDto<IncidentResponseDto>.FailureResult("Lý do chuyển trạm tối thiểu 5 ký tự.");
        }
        if (dto.StationId == incident.ResponsibleStationId)
        {
            return ApiResponseDto<IncidentResponseDto>.FailureResult("Sự cố đã thuộc trạm này.");
        }

        var target = await _stationRepository.GetByIdAsync(dto.StationId);
        if (target == null || target.IsDeleted)
        {
            return ApiResponseDto<IncidentResponseDto>.NotFoundResult("Không tìm thấy trạm nhận.");
        }
        if (!target.IsActive)
        {
            return ApiResponseDto<IncidentResponseDto>.FailureResult("Trạm nhận đang tạm ngưng hoạt động.");
        }

        var actor = await _userRepository.GetByIdAsync(actorUserId);
        var from = incident.ResponsibleStation;
        var now = DateTime.UtcNow;

        incident.ResponsibleStationId = target.Id;
        incident.ResponsibleStation = target;
        // Nhả khóa mềm để Operator trạm nhận xử lý ngay
        incident.LockedByOperatorId = null;
        incident.LockExpiresAt = null;
        incident.UpdatedAt = now;

        await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
        {
            IncidentId = incident.Id,
            ActorId = actorUserId,
            ActorRole = actor?.Role.ToString(),
            Action = AuditAction.TransferredStation,
            Description = $"Chuyển trạm phụ trách: {from?.Name ?? "(chưa có)"} → {target.Name}. Lý do: {reason}",
            OldStateJson = JsonSerializer.Serialize(new { responsibleStationId = from?.Id }),
            NewStateJson = JsonSerializer.Serialize(new { responsibleStationId = target.Id }),
            Timestamp = now
        }, cancellationToken);
        await _incidentRepository.SaveChangesAsync(cancellationToken);

        var incidentDto = MapToResponseDto(incident);
        if (from != null)
        {
            await _realtimeNotifier.IncidentLockChangedAsync(new IncidentLockEventDto { IncidentId = incident.Id, ChangedAt = now },
                from.Id, cancellationToken);
        }
        await _realtimeNotifier.IncidentTransferredAsync(new IncidentTransferredDto
        {
            IncidentId = incident.Id,
            IncidentTitle = incident.Title,
            FromStationId = from?.Id,
            FromStationName = from?.Name,
            ToStationId = target.Id,
            ToStationName = target.Name,
            Reason = reason,
            TransferredByName = actor?.FullName,
            TransferredAt = now
        }, incidentDto, cancellationToken);

        await NotifyCommandCenterAsync(target.Id, new NotificationRequest
        {
            Type = NotificationType.NewIncident,
            Title = "SỰ CỐ ĐƯỢC CHUYỂN TỚI TRẠM BẠN",
            Message = $"{incident.Title} — {incident.LocationAddress} (từ {from?.Name ?? "trạm khác"}). Lý do: {reason}",
            IncidentId = incident.Id,
            HighPriority = incident.Severity is SeverityLevel.Level3 or SeverityLevel.Unclassified
        }, cancellationToken);

        return ApiResponseDto<IncidentResponseDto>.SuccessResult(incidentDto, LocalizationHelper.Localize("Đã chuyển sự cố sang {0}.", target.Name));
    }

    /// <summary>
    /// BR-31 / BR-32: Operator, Admin và đội cứu hộ đang làm nhiệm vụ của sự cố được xem số đầy đủ (có ghi vết)
    /// </summary>
    private Task<bool> CanSeeFullPhoneAsync(Incident incident, User actor)
    {
        var allowed = actor.Role switch
        {
            UserRole.Admin => true,
            UserRole.Operator => CanAccess(incident, actor),
            UserRole.RescueStaff => actor.StationId.HasValue
                                    && incident.DispatchAssignments.Any(a => a.IsActive && a.RescueUnit?.StationId == actor.StationId),
            UserRole.Citizen => incident.ReportedByUserId == actor.Id,
            _ => false
        };
        return Task.FromResult(allowed);
    }

    public async Task<ApiResponseDto<PaginatedResultDto<MyReportDto>>> GetMyReportsAsync(
        Guid userId,
        MyReportFilter filter,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 50);
        IncidentStatus? status = filter switch
        {
            MyReportFilter.Completed => IncidentStatus.Completed,
            MyReportFilter.Cancelled => IncidentStatus.Cancelled,
            _ => null
        };

        var (items, total) = await _incidentRepository.GetByReporterAsync(userId, status, pageIndex, pageSize, cancellationToken);
        var dtos = items.Select(i =>
        {
            var assignments = i.DispatchAssignments.Where(a => a.Status != DispatchAssignmentStatus.Cancelled && a.Status != DispatchAssignmentStatus.Rejected).ToList();
            return new MyReportDto
            {
                Id = i.Id,
                Title = i.Title,
                Description = i.Description,
                LocationAddress = i.LocationAddress,
                Status = i.Status,
                Severity = i.Severity,
                ResolutionSummary = i.ResolutionSummary,
                CancellationReason = i.CancellationReason,
                MediaUrls = i.MediaItems.Select(m => m.MediaUrl).ToList(),
                ReportedAt = i.CreatedAt,
                VerifiedAt = i.VerifiedAt,
                DispatchedAt = assignments.Select(a => (DateTime?)a.DispatchedAt).Min(),
                OnSceneAt = assignments.Select(a => a.ArrivedAt).Where(t => t.HasValue).Min(),
                CompletedAt = i.ClosedAt,
                DispatchedUnits = assignments.Select(a => new MyReportUnitDto
                {
                    PlateNumber = a.RescueUnit?.PlateNumber ?? string.Empty,
                    UnitType = a.RescueUnit?.UnitType ?? RescueUnitType.Ambulance,
                    StationName = a.RescueUnit?.Station?.Name,
                    Status = a.Status
                }).ToList()
            };
        }).ToList();

        return ApiResponseDto<PaginatedResultDto<MyReportDto>>.SuccessResult(
            new PaginatedResultDto<MyReportDto>(dtos, total, pageIndex, pageSize));
    }

    public async Task<ApiResponseDto<PaginatedResultDto<AuditTrailEntryDto>>> GetAuditTrailAsync(
        AuditTrailFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        if (filter.From.HasValue && filter.To.HasValue && filter.From > filter.To)
        {
            return ApiResponseDto<PaginatedResultDto<AuditTrailEntryDto>>.FailureResult("Ngày bắt đầu phải trước hoặc bằng ngày kết thúc.");
        }

        if (_currentUser.IsStationScoped())
        {
            var scoped = filter.IncidentId.HasValue ? await _incidentRepository.GetByIdAsync(filter.IncidentId.Value) : null;
            var auditScopeError = scoped == null
                ? OperatorScope.OtherStationMessage
                : _currentUser.CheckIncident(scoped.ResponsibleStationId);
            if (auditScopeError != null)
            {
                return ApiResponseDto<PaginatedResultDto<AuditTrailEntryDto>>.ForbiddenResult(auditScopeError);
            }
        }

        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var (items, total) = await _incidentRepository.GetAuditTrailsAsync(
            filter.IncidentId, filter.ActorId, filter.Action, filter.From, filter.To,
            filter.PageIndex, pageSize, cancellationToken);

        var dtos = items.Select(a => new AuditTrailEntryDto
        {
            Id = a.Id,
            IncidentId = a.IncidentId,
            IncidentTitle = a.Incident?.Title,
            ActorId = a.ActorId,
            ActorName = a.Actor?.FullName ?? (a.ActorRole == "AI_Worker" ? "AI Worker" : a.ActorRole),
            ActorRole = a.ActorRole,
            Action = a.Action,
            Description = a.Description,
            OldStateJson = a.OldStateJson,
            NewStateJson = a.NewStateJson,
            Timestamp = a.Timestamp
        }).ToList();

        return ApiResponseDto<PaginatedResultDto<AuditTrailEntryDto>>.SuccessResult(
            new PaginatedResultDto<AuditTrailEntryDto>(dtos, total, filter.PageIndex, pageSize));
    }

    private static IncidentResponseDto MapToResponseDto(
        Incident incident,
        AiClassificationResultDto? directAiResult = null,
        bool revealPhone = false)
    {
        string? Phone(string? raw) => revealPhone ? raw : PhoneMasking.Mask(raw);

        AiClassificationResultDto? aiDto = directAiResult;

        if (aiDto == null && incident.AiClassification != null)
        {
            aiDto = new AiClassificationResultDto
            {
                SeverityScore = (int)incident.AiClassification.SeverityScore,
                IncidentType = AiClassificationResultDto.ToIncidentTypeLabel(incident.AiClassification.EventType),
                IsTrafficJam = incident.AiClassification.IsTrafficJam,
                HazardTags = incident.AiClassification.HazardTags,
                Summary = incident.AiClassification.Summary,
                ConfidenceScore = incident.AiClassification.ConfidenceScore,
                ModelName = incident.AiClassification.ModelName,
                RawResponse = incident.AiClassification.RawResponse,
                IsSuccess = incident.AiClassification.IsSuccess,
                ErrorMessage = incident.AiClassification.ErrorMessage,
                ProcessingDurationMs = incident.AiClassification.ProcessingDurationMs ?? 0
            };
        }

        // Map hồ sơ y tế chuyên sâu & người liên hệ khẩn cấp nếu sự cố gắn với tài khoản người dùng
        IncidentReporterMedicalDto? medicalProfile = null;
        if (incident.ReportedByUser != null)
        {
            medicalProfile = new IncidentReporterMedicalDto
            {
                UserId = incident.ReportedByUser.Id,
                FullName = incident.ReportedByUser.FullName,
                PhoneNumber = Phone(incident.ReportedByUser.PhoneNumber) ?? string.Empty,
                BloodType = incident.ReportedByUser.BloodType?.ToString(),
                MedicalNotes = incident.ReportedByUser.MedicalNotes,
                ChronicConditions = incident.ReportedByUser.ChronicConditions ?? new(),
                Allergies = incident.ReportedByUser.Allergies ?? new(),
                CurrentMedications = incident.ReportedByUser.CurrentMedications ?? new(),
                MobilityLimitations = incident.ReportedByUser.MobilityLimitations ?? new(),
                PreferredLanguage = incident.ReportedByUser.PreferredLanguage,
                DependentsNote = incident.ReportedByUser.DependentsNote,
                HouseholdMembersCount = incident.ReportedByUser.HouseholdMembersCount ?? 1,
                EmergencyContactName = incident.ReportedByUser.EmergencyContactName,
                EmergencyContactPhone = Phone(incident.ReportedByUser.EmergencyContactPhone),
                EmergencyContactRelationship = incident.ReportedByUser.EmergencyContactRelationship
            };
        }

        var hasActiveLock = incident.LockedByOperatorId.HasValue
                            && incident.LockExpiresAt.HasValue
                            && incident.LockExpiresAt.Value > DateTime.UtcNow;

        return new IncidentResponseDto
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
            ReporterName = incident.ReporterName ?? incident.ReportedByUser?.FullName,
            ReporterPhone = Phone(incident.ReporterPhone ?? incident.ReportedByUser?.PhoneNumber),
            VerifiedByUserId = incident.VerifiedByUserId,
            VerifiedByUserName = incident.VerifiedByUser?.FullName,
            VerifiedAt = incident.VerifiedAt,
            OperatorNotes = incident.OperatorNotes,
            CreatedAt = incident.CreatedAt,
            UpdatedAt = incident.UpdatedAt,
            LockedByOperatorId = hasActiveLock ? incident.LockedByOperatorId : null,
            LockedByOperatorName = hasActiveLock ? incident.LockedByOperator?.FullName : null,
            LockExpiresAt = hasActiveLock ? incident.LockExpiresAt : null,
            ResponsibleStationId = incident.ResponsibleStationId,
            ResponsibleStationName = incident.ResponsibleStation?.Name,
            IsOutOfServiceArea = incident.IsOutOfServiceArea,
            DistanceOutsideAreaKm = incident.DistanceOutsideAreaKm,
            EventType = incident.EventType != IncidentEventType.None
                ? incident.EventType
                : incident.AiClassification?.EventType ?? IncidentEventType.None,
            CancellationReason = incident.CancellationReason,
            CancelledAt = incident.CancelledAt,
            CancelledByUserId = incident.CancelledByUserId,
            CancelledByName = incident.CancelledByUser?.FullName,
            CancellationNote = incident.CancellationNote,
            MediaItems = incident.MediaItems.Select(m => new IncidentMediaDto
            {
                Id = m.Id,
                MediaUrl = m.MediaUrl,
                MediaType = m.MediaType,
                FileSizeBytes = m.FileSizeBytes,
                MimeType = m.MimeType
            }).ToList(),
            AiClassification = aiDto,
            ReporterMedicalProfile = medicalProfile
        };
    }
}
