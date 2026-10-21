using System.Globalization;
using System.Text.Json;
using EmergencyDispatch.Application.Localization;
using EmergencyDispatch.Application.DTOs.Ai;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.DTOs.Notification;
using EmergencyDispatch.Application.DTOs.Security;
using EmergencyDispatch.Application.DTOs.User;
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
        IGuestCancellationTokenService guestTokens)
    {
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
        string? smsDispatchLog = null;

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

                // Tự động kích hoạt thông báo gửi SMS tới người thân ICE nếu bật tính năng
                if (reportingUser.AutoSendSmsOnSos && reportingUser.IceContacts.Any())
                {
                    var primaryIce = reportingUser.IceContacts.FirstOrDefault(c => c.IsPrimary) 
                                     ?? reportingUser.IceContacts.First();
                    var template = string.IsNullOrWhiteSpace(reportingUser.SmsTemplate)
                        ? LocalizationHelper.Localize("CẤP CỨU: Tôi vừa bấm SOS tại tọa độ {lat},{lng}. Cần hỗ trợ khẩn cấp!")
                        : reportingUser.SmsTemplate;

                    var smsContent = template
                        .Replace("{GPS_COORDS}", FormattableString.Invariant($"{dto.Latitude:F4},{dto.Longitude:F4}"))
                        .Replace("{lat}", dto.Latitude.ToString("F4", CultureInfo.InvariantCulture))
                        .Replace("{lng}", dto.Longitude.ToString("F4", CultureInfo.InvariantCulture));

                    smsDispatchLog = LocalizationHelper.Localize("[SMS SOS PREPARED - NOT SENT]: Đã chuẩn bị nội dung cấp cứu cho người thân [{0} ({1}) - SĐT: {2}]: \"{3}\"",
                        primaryIce.Name, primaryIce.Relationship, primaryIce.PhoneNumber, smsContent);
                    _logger.LogInformation("{Log}", smsDispatchLog);
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

        var incident = new Incident
        {
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
        await _realtimeNotifier.IncidentCreatedAsync(MapToResponseDto(incident, null, smsDispatchLog), cancellationToken);
        var responseDto = MapToResponseDto(incident, null, smsDispatchLog, revealPhone: true);

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

        // Chỉ cập nhật trạng thái/mức độ nếu Operator chưa can thiệp (chưa xác minh / chưa hủy)
        var appliedToIncident = incident.Status == IncidentStatus.AiProcessing;
        if (appliedToIncident)
        {
            var oldStatus = incident.Status;
            incident.Severity = aiResult.SeverityLevel;
            incident.Status = IncidentStatus.AiProcessed;
            incident.UpdatedAt = DateTime.UtcNow;

            await _incidentRepository.AddStatusHistoryAsync(new IncidentStatusHistory
            {
                IncidentId = incident.Id,
                OldStatus = oldStatus,
                NewStatus = incident.Status,
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

        // Người báo luôn thấy số của chính mình; người khác thấy số đã che (BR-32)
        var isReporter = viewerUserId.HasValue && incident.ReportedByUserId == viewerUserId;
        return ApiResponseDto<IncidentResponseDto>.SuccessResult(MapToResponseDto(incident, revealPhone: isReporter));
    }

    public async Task<ApiResponseDto<PaginatedResultDto<IncidentResponseDto>>> GetIncidentsAsync(
        IncidentFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _incidentRepository.GetFilteredIncidentsAsync(
            filter.Status,
            filter.Severity,
            filter.SearchTerm,
            filter.FromDate,
            filter.ToDate,
            filter.PageIndex,
            filter.PageSize,
            cancellationToken);

        var dtoList = items.Select(i => MapToResponseDto(i)).ToList();
        var paginated = new PaginatedResultDto<IncidentResponseDto>(dtoList, totalCount, filter.PageIndex, filter.PageSize);

        return ApiResponseDto<PaginatedResultDto<IncidentResponseDto>>.SuccessResult(paginated);
    }

    public async Task<ApiResponseDto<List<IncidentResponseDto>>> GetQueueAsync(CancellationToken cancellationToken = default)
    {
        var queueItems = await _incidentRepository.GetPendingQueueAsync(cancellationToken);
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

        // BR-03 (MSG25): ghi đè mức độ AI đề xuất bắt buộc kèm lý do để lưu vết bất biến
        var isOverride = aiSeverity.HasValue && aiSeverity.Value != dto.ConfirmedSeverity;
        if (isOverride && (dto.OperatorNotes?.Trim().Length ?? 0) < MinOverrideReasonLength)
        {
            return ApiResponseDto<IncidentResponseDto>.FailureResult(
                $"Bạn đang thay đổi mức độ AI đề xuất ({aiSeverity} → {dto.ConfirmedSeverity}). Vui lòng nhập lý do ghi đè (tối thiểu {MinOverrideReasonLength} ký tự) vào operatorNotes.");
        }

        // Cập nhật thông tin xác minh bởi Operator (Human-in-the-loop)
        incident.Severity = dto.ConfirmedSeverity;
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

        var severityOverridden = isOverride;

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
                ? $"Operator ghi đè mức độ AI {aiSeverity} → {dto.ConfirmedSeverity}"
                : "Operator xác minh sự cố",
            OldStateJson = JsonSerializer.Serialize(new { status = oldStatus.ToString(), aiSeverity = aiSeverity?.ToString() }),
            NewStateJson = JsonSerializer.Serialize(new
            {
                status = IncidentStatus.Verified.ToString(),
                severity = dto.ConfirmedSeverity.ToString(),
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
        string? guestCancellationToken = null)
    {
        var incident = await _incidentRepository.GetIncidentWithDetailsAsync(id, cancellationToken);
        if (incident == null)
        {
            return ApiResponseDto<IncidentResponseDto>.FailureResult("Không tìm thấy sự cố cần hủy.");
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

        var actorDescription = $"{cancellingUser.Role} {cancellingUser.FullName} ({cancellingUser.Id})";

        // Thực tế: Nếu xe đã xuất phát (Dispatched / EnRoute / OnScene), hủy sự cố sẽ tự động THU HỒI XE về trạng thái Sẵn sàng (Available)
        var recalledUnitsCount = 0;
        if (incident.DispatchAssignments != null && incident.DispatchAssignments.Any())
        {
            foreach (var assignment in incident.DispatchAssignments)
            {
                if (assignment.Status != DispatchAssignmentStatus.Completed &&
                    assignment.Status != DispatchAssignmentStatus.Cancelled)
                {
                    assignment.Status = DispatchAssignmentStatus.Cancelled;
                    assignment.Notes = LocalizationHelper.Localize("[THU HỒI XE]: Sự cố bị hủy bởi {0}. Lý do: {1}", actorDescription, reason);
                    assignment.UpdatedAt = DateTime.UtcNow;

                    if (assignment.RescueUnit != null)
                    {
                        assignment.RescueUnit.Status = RescueUnitStatus.Available;
                        assignment.RescueUnit.UpdatedAt = DateTime.UtcNow;
                        recalledUnitsCount++;
                    }
                }
            }
        }

        incident.Status = IncidentStatus.Cancelled;
        if (cancelledByUserId.HasValue)
        {
            incident.VerifiedByUserId = cancelledByUserId;
            incident.VerifiedAt = DateTime.UtcNow;
        }

        var recallNote = recalledUnitsCount > 0 ? " " + LocalizationHelper.Localize("(Đã thu hồi {0} xe đang xuất phát)", recalledUnitsCount) : string.Empty;
        incident.OperatorNotes = LocalizationHelper.Localize("[ĐÃ HỦY bởi {0}{1}]: {2}", actorDescription, recallNote, reason).Trim();
        incident.UpdatedAt = DateTime.UtcNow;

        await _incidentRepository.UpdateAsync(incident);

        _logger.LogInformation("Sự cố {IncidentId} đã bị hủy bởi {Actor}. Thu hồi {Count} xe. Lý do: {Reason}",
            id, actorDescription, recalledUnitsCount, reason);

        var successMessage = recalledUnitsCount > 0
            ? LocalizationHelper.Localize("Đã hủy sự cố thành công và tự động phát lệnh thu hồi {0} xe cứu hộ quay về trạm.", recalledUnitsCount)
            : "Đã hủy sự cố thành công.";

        var responseDto = MapToResponseDto(incident);
        await _realtimeNotifier.IncidentUpdatedAsync(responseDto, cancellationToken);

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

        if (incident.Status is IncidentStatus.Completed or IncidentStatus.Cancelled)
        {
            return ApiResponseDto<IncidentLockDto>.FailureResult($"Sự cố đã ở trạng thái '{incident.Status}', không cần khóa.");
        }

        var now = DateTime.UtcNow;
        var expiresAt = now.Add(SoftLockDuration);
        var acquired = await _incidentRepository.TryAcquireLockAsync(id, operatorId, expiresAt, now, cancellationToken);
        if (!acquired)
        {
            return ApiResponseDto<IncidentLockDto>.ConflictResult(
                $"Sự cố đang được xử lý bởi {incident.LockedByOperator?.FullName ?? "điều phối viên khác"}.");
        }

        var operatorUser = await _userRepository.GetByIdAsync(operatorId);
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
        if (user.Role is UserRole.Operator or UserRole.Admin) return true;

        var incident = await _incidentRepository.GetIncidentWithDetailsAsync(id, cancellationToken);
        if (incident == null) return false;

        return user.Role switch
        {
            UserRole.Citizen => incident.ReportedByUserId == user.Id,
            UserRole.RescueStaff => user.StationId.HasValue
                                    && incident.DispatchAssignments.Any(a => a.RescueUnit?.StationId == user.StationId),
            _ => false
        };
    }

    /// <summary>
    /// BR-31 / BR-32: Operator, Admin và đội cứu hộ đang làm nhiệm vụ của sự cố được xem số đầy đủ (có ghi vết)
    /// </summary>
    private Task<bool> CanSeeFullPhoneAsync(Incident incident, User actor)
    {
        var allowed = actor.Role switch
        {
            UserRole.Operator or UserRole.Admin => true,
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
        string? emergencySmsLog = null,
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

        // Map hồ sơ y tế chuyên sâu & danh bạ ICE nếu sự cố gắn với tài khoản người dùng
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
                IceContacts = incident.ReportedByUser.IceContacts?.Select(c => new IceContactDto
                {
                    Id = c.Id,
                    UserId = c.UserId,
                    Name = c.Name,
                    PhoneNumber = Phone(c.PhoneNumber) ?? string.Empty,
                    Relationship = c.Relationship,
                    IsPrimary = c.IsPrimary,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt
                }).ToList() ?? new()
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
            MediaItems = incident.MediaItems.Select(m => new IncidentMediaDto
            {
                Id = m.Id,
                MediaUrl = m.MediaUrl,
                MediaType = m.MediaType,
                FileSizeBytes = m.FileSizeBytes,
                MimeType = m.MimeType
            }).ToList(),
            AiClassification = aiDto,
            ReporterMedicalProfile = medicalProfile,
            EmergencySmsDispatchLog = emergencySmsLog
        };
    }
}
