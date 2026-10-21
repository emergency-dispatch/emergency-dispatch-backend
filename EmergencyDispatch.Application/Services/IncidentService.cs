using System.Text.Json;
using EmergencyDispatch.Application.DTOs.Ai;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Incident;
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

    private readonly IIncidentRepository _incidentRepository;
    private readonly IUserRepository _userRepository;
    private readonly IAiClassificationService _aiService;
    private readonly IAiAnalysisQueue _aiQueue;
    private readonly IRealtimeNotifier _realtimeNotifier;
    private readonly IValidator<CreateIncidentDto> _validator;
    private readonly ILogger<IncidentService> _logger;
    private readonly IGuestCancellationTokenService _guestTokens;

    public IncidentService(
        IIncidentRepository incidentRepository,
        IUserRepository userRepository,
        IAiClassificationService aiService,
        IAiAnalysisQueue aiQueue,
        IRealtimeNotifier realtimeNotifier,
        IValidator<CreateIncidentDto> validator,
        ILogger<IncidentService> logger,
        IGuestCancellationTokenService guestTokens)
    {
        _incidentRepository = incidentRepository;
        _userRepository = userRepository;
        _aiService = aiService;
        _aiQueue = aiQueue;
        _realtimeNotifier = realtimeNotifier;
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
                        ? "CẤP CỨU: Tôi vừa bấm SOS tại tọa độ {lat},{lng}. Cần hỗ trợ khẩn cấp!"
                        : reportingUser.SmsTemplate;

                    var smsContent = template
                        .Replace("{GPS_COORDS}", $"{dto.Latitude:F4},{dto.Longitude:F4}")
                        .Replace("{lat}", dto.Latitude.ToString("F4"))
                        .Replace("{lng}", dto.Longitude.ToString("F4"));

                    smsDispatchLog = $"[SMS SOS PREPARED - NOT SENT]: Đã chuẩn bị nội dung cấp cứu cho người thân [{primaryIce.Name} ({primaryIce.Relationship}) - SĐT: {primaryIce.PhoneNumber}]: \"{smsContent}\"";
                    _logger.LogInformation("{Log}", smsDispatchLog);
                }
            }
        }

        var incident = new Incident
        {
            Title = title,
            Description = dto.Description?.Trim() ?? string.Empty,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            LocationAddress = dto.LocationAddress.Trim(),
            ReporterName = dto.ReporterName?.Trim(),
            ReporterPhone = dto.ReporterPhone?.Trim(),
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
        var responseDto = MapToResponseDto(incident, null, smsDispatchLog);
        await _realtimeNotifier.IncidentCreatedAsync(responseDto, cancellationToken);

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
        CancellationToken cancellationToken = default)
    {
        var incident = await _incidentRepository.GetIncidentWithDetailsAsync(id, cancellationToken);
        if (incident == null)
        {
            return ApiResponseDto<IncidentResponseDto>.FailureResult("Không tìm thấy sự cố với mã yêu cầu.");
        }

        return ApiResponseDto<IncidentResponseDto>.SuccessResult(MapToResponseDto(incident));
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

        // Cho phép Operator xác minh ngay cả khi AI chưa xong (Human-in-the-loop luôn được ưu tiên)
        if (incident.Status != IncidentStatus.Pending &&
            incident.Status != IncidentStatus.AiProcessing &&
            incident.Status != IncidentStatus.AiProcessed)
        {
            return ApiResponseDto<IncidentResponseDto>.FailureResult($"Sự cố đang ở trạng thái '{incident.Status}', không thể xác minh lại.");
        }

        var oldStatus = incident.Status;
        var aiSeverity = incident.AiClassification?.SeverityScore;

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

        var severityOverridden = aiSeverity.HasValue && aiSeverity.Value != dto.ConfirmedSeverity;

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

        var actor = cancelledByUserId.HasValue
            ? await _userRepository.GetByIdAsync(cancelledByUserId.Value) : null;
        var isDispatcher = actor?.Role == UserRole.Admin || actor?.Role == UserRole.Operator;
        var isOwner = actor != null && incident.ReportedByUserId == actor.Id;
        var isGuestOwner = !incident.ReportedByUserId.HasValue && _guestTokens.Validate(id, guestCancellationToken);
        if (!isDispatcher && !isOwner && !isGuestOwner)
        {
            return ApiResponseDto<IncidentResponseDto>.FailureResult("Bạn không có quyền hủy sự cố này hoặc mã hủy đã hết hạn.");
        }

        var actorDescription = cancelledByUserId.HasValue ? $"User {cancelledByUserId.Value}" : "Người dân/Khách vãng lai";

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
                    assignment.Notes = $"[THU HỒI XE]: Sự cố bị hủy bởi {actorDescription}. Lý do: {reason}";
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

        var recallNote = recalledUnitsCount > 0 ? $" (Đã thu hồi {recalledUnitsCount} xe đang xuất phát)" : string.Empty;
        incident.OperatorNotes = $"[ĐÃ HỦY bởi {actorDescription}{recallNote}]: {reason}".Trim();
        incident.UpdatedAt = DateTime.UtcNow;

        await _incidentRepository.UpdateAsync(incident);

        _logger.LogInformation("Sự cố {IncidentId} đã bị hủy bởi {Actor}. Thu hồi {Count} xe. Lý do: {Reason}",
            id, actorDescription, recalledUnitsCount, reason);

        var successMessage = recalledUnitsCount > 0
            ? $"Đã hủy sự cố thành công và tự động phát lệnh thu hồi {recalledUnitsCount} xe cứu hộ quay về trạm."
            : "Đã hủy sự cố thành công.";

        var responseDto = MapToResponseDto(incident);
        await _realtimeNotifier.IncidentUpdatedAsync(responseDto, cancellationToken);

        return ApiResponseDto<IncidentResponseDto>.SuccessResult(responseDto, successMessage);
    }

    private static IncidentResponseDto MapToResponseDto(
        Incident incident,
        AiClassificationResultDto? directAiResult = null,
        string? emergencySmsLog = null)
    {
        AiClassificationResultDto? aiDto = directAiResult;

        if (aiDto == null && incident.AiClassification != null)
        {
            aiDto = new AiClassificationResultDto
            {
                SeverityScore = (int)incident.AiClassification.SeverityScore,
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
                PhoneNumber = incident.ReportedByUser.PhoneNumber ?? string.Empty,
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
                    PhoneNumber = c.PhoneNumber,
                    Relationship = c.Relationship,
                    IsPrimary = c.IsPrimary,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt
                }).ToList() ?? new()
            };
        }

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
            ReporterPhone = incident.ReporterPhone ?? incident.ReportedByUser?.PhoneNumber,
            VerifiedByUserId = incident.VerifiedByUserId,
            VerifiedByUserName = incident.VerifiedByUser?.FullName,
            VerifiedAt = incident.VerifiedAt,
            OperatorNotes = incident.OperatorNotes,
            CreatedAt = incident.CreatedAt,
            UpdatedAt = incident.UpdatedAt,
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
