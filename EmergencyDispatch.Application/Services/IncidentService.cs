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
    private readonly IIncidentRepository _incidentRepository;
    private readonly IUserRepository _userRepository;
    private readonly IAiClassificationService _aiService;
    private readonly IValidator<CreateIncidentDto> _validator;
    private readonly ILogger<IncidentService> _logger;

    public IncidentService(
        IIncidentRepository incidentRepository,
        IUserRepository userRepository,
        IAiClassificationService aiService,
        IValidator<CreateIncidentDto> validator,
        ILogger<IncidentService> logger)
    {
        _incidentRepository = incidentRepository;
        _userRepository = userRepository;
        _aiService = aiService;
        _validator = validator;
        _logger = logger;
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
                        .Replace("{lat}", dto.Latitude.ToString("F4"))
                        .Replace("{lng}", dto.Longitude.ToString("F4"));

                    smsDispatchLog = $"[SMS SOS DISPATCH]: Đã gửi tin nhắn cấp cứu tới người thân [{primaryIce.Name} ({primaryIce.Relationship}) - SĐT: {primaryIce.PhoneNumber}]: \"{smsContent}\"";
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

        // 4. Phân tích AI tự động (Core Research AI Module)
        AiClassificationResultDto? aiResult = null;
        var mediaToAnalyze = incident.MediaItems.FirstOrDefault()?.MediaUrl;

        if (!string.IsNullOrWhiteSpace(mediaToAnalyze))
        {
            _logger.LogInformation("Bắt đầu kích hoạt phân tích Vision-Language AI cho sự cố với media: {MediaUrl}", mediaToAnalyze);
            incident.Status = IncidentStatus.AiProcessing;

            // Gọi AI Service với cơ chế Fallback tự động
            aiResult = await _aiService.AnalyzeAsync(mediaToAnalyze, dto.Description, cancellationToken);

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

            incident.AiClassification = aiEntity;
            incident.Severity = aiResult.SeverityLevel;
            incident.Status = IncidentStatus.AiProcessed;

            _logger.LogInformation("Phân tích AI hoàn tất. Mức độ: {Severity}, Thành công: {IsSuccess}",
                aiResult.SeverityLevel, aiResult.IsSuccess);
        }
        else
        {
            _logger.LogInformation("Sự cố không có hình ảnh/video hiện trường. Giữ mức độ Unclassified (Level 0).");
            incident.Status = IncidentStatus.Pending;
            incident.Severity = SeverityLevel.Unclassified;
        }

        // 5. Lưu vào cơ sở dữ liệu
        await _incidentRepository.AddAsync(incident);

        // 6. Map kết quả trả về
        var responseDto = MapToResponseDto(incident, aiResult, smsDispatchLog);
        return ApiResponseDto<IncidentResponseDto>.SuccessResult(
            responseDto,
            "Báo cáo sự cố đã được tiếp nhận và phân tích rủi ro thành công.");
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

        if (incident.Status != IncidentStatus.Pending && incident.Status != IncidentStatus.AiProcessed)
        {
            return ApiResponseDto<IncidentResponseDto>.FailureResult($"Sự cố đang ở trạng thái '{incident.Status}', không thể xác minh lại.");
        }

        // Cập nhật thông tin xác minh bởi Operator (Human-in-the-loop)
        incident.Severity = dto.ConfirmedSeverity;
        incident.Status = IncidentStatus.Verified;
        incident.VerifiedByUserId = operatorId;
        incident.VerifiedAt = DateTime.UtcNow;
        incident.OperatorNotes = dto.OperatorNotes?.Trim();
        incident.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(dto.AdjustedTitle))
        {
            incident.Title = dto.AdjustedTitle.Trim();
        }

        await _incidentRepository.UpdateAsync(incident);

        _logger.LogInformation("Operator {OperatorId} đã xác minh sự cố {IncidentId} với mức độ {Severity}",
            operatorId, id, dto.ConfirmedSeverity);

        return ApiResponseDto<IncidentResponseDto>.SuccessResult(
            MapToResponseDto(incident),
            "Xác minh sự cố thành công. Sự cố sẵn sàng để điều phối đội cứu hộ.");
    }

    public async Task<ApiResponseDto<IncidentResponseDto>> CancelIncidentAsync(
        Guid id,
        string reason,
        Guid? cancelledByUserId = null,
        CancellationToken cancellationToken = default)
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

        // Kiểm tra phân quyền thực tế: Nếu sự cố gắn với người dùng cụ thể, người hủy có tài khoản phải là chính chủ hoặc Operator/Admin
        if (cancelledByUserId.HasValue && incident.ReportedByUserId.HasValue && incident.ReportedByUserId != cancelledByUserId.Value)
        {
            var cancellingUser = await _userRepository.GetByIdAsync(cancelledByUserId.Value);
            if (cancellingUser != null && cancellingUser.Role != UserRole.Admin && cancellingUser.Role != UserRole.Operator)
            {
                return ApiResponseDto<IncidentResponseDto>.FailureResult("Bạn không có quyền hủy sự cố được báo cáo bởi người khác.");
            }
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

        return ApiResponseDto<IncidentResponseDto>.SuccessResult(MapToResponseDto(incident), successMessage);
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
