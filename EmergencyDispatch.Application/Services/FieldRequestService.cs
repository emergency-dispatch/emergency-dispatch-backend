using System.Text.Json;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.DTOs.Notification;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Localization;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Application.Services;

/// <summary>
/// Yêu cầu từ hiện trường (Backup / Other). Vòng đời: Open → Acknowledged → Fulfilled / Declined, hoặc Cancelled (staff tự hủy / sự cố đã đóng).
/// </summary>
public class FieldRequestService : IFieldRequestService
{
    public const int MinReasonLength = 5;
    public const int MaxDescriptionLength = 1000;
    public const int MaxNoteLength = 500;
    public const int MaxBackupQuantity = 5;
    private const string SystemRole = "System";

    private readonly IFieldRequestRepository _requestRepository;
    private readonly IDispatchAssignmentRepository _assignmentRepository;
    private readonly IIncidentRepository _incidentRepository;
    private readonly IUserRepository _userRepository;
    private readonly IRealtimeNotifier _realtimeNotifier;
    private readonly INotificationService _notificationService;
    private readonly ICurrentUserContext _currentUser;
    private readonly ISystemSettingsService _settingsService;
    private readonly ILogger<FieldRequestService> _logger;

    public FieldRequestService(
        IFieldRequestRepository requestRepository,
        IDispatchAssignmentRepository assignmentRepository,
        IIncidentRepository incidentRepository,
        IUserRepository userRepository,
        IRealtimeNotifier realtimeNotifier,
        INotificationService notificationService,
        ICurrentUserContext currentUser,
        ISystemSettingsService settingsService,
        ILogger<FieldRequestService> logger)
    {
        _requestRepository = requestRepository;
        _assignmentRepository = assignmentRepository;
        _incidentRepository = incidentRepository;
        _userRepository = userRepository;
        _realtimeNotifier = realtimeNotifier;
        _notificationService = notificationService;
        _currentUser = currentUser;
        _settingsService = settingsService;
        _logger = logger;
    }

    // =====================================================================================
    // Staff gửi yêu cầu
    // =====================================================================================
    public async Task<ApiResponseDto<FieldRequestDto>> CreateAsync(Guid assignmentId, CreateFieldRequestDto dto, Guid staffUserId, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(dto.Type))
        {
            return ApiResponseDto<FieldRequestDto>.FailureResult("Loại yêu cầu chỉ nhận Backup (chi viện thêm xe) hoặc Other (vấn đề khác).");
        }

        var description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        if (description is { Length: > MaxDescriptionLength })
        {
            return ApiResponseDto<FieldRequestDto>.FailureResult(
                LocalizationHelper.Localize("Mô tả tối đa {0} ký tự.", MaxDescriptionLength));
        }

        if (dto.Type == FieldRequestType.Other && (description?.Length ?? 0) < MinReasonLength)
        {
            return ApiResponseDto<FieldRequestDto>.FailureResult(
                LocalizationHelper.Localize("Yêu cầu loại Other cần mô tả tình huống (tối thiểu {0} ký tự).", MinReasonLength));
        }

        if (dto.Type == FieldRequestType.Backup)
        {
            if (dto.Quantity < 1 || dto.Quantity > MaxBackupQuantity)
            {
                return ApiResponseDto<FieldRequestDto>.FailureResult(
                    LocalizationHelper.Localize("Số xe chi viện phải từ 1 đến {0}.", MaxBackupQuantity));
            }

            if (dto.RequestedUnitType is { } unitType && !Enum.IsDefined(unitType))
            {
                return ApiResponseDto<FieldRequestDto>.FailureResult("Loại xe yêu cầu không hợp lệ.");
            }
        }

        var assignment = await _assignmentRepository.GetByIdWithDetailsAsync(assignmentId);
        if (assignment == null)
        {
            return ApiResponseDto<FieldRequestDto>.NotFoundResult("Không tìm thấy lượt điều phối.");
        }

        var staff = await _userRepository.GetByIdAsync(staffUserId);
        if (staff == null || staff.Role != UserRole.RescueStaff || assignment.RescueUnit == null
            || !CrewScope.CanWorkOn(staff.StationId, await _userRepository.GetTeamIdAsync(staff.Id, cancellationToken), assignment.RescueUnit))
        {
            return ApiResponseDto<FieldRequestDto>.ForbiddenResult("Chỉ đội nhận lệnh của xe (tổ phụ trách xe) được gửi yêu cầu cho lượt điều phối này.");
        }

        if (assignment.Status is not (DispatchAssignmentStatus.Accepted or DispatchAssignmentStatus.EnRoute or DispatchAssignmentStatus.OnScene))
        {
            return ApiResponseDto<FieldRequestDto>.FailureResult(
                LocalizationHelper.Localize("Chỉ gửi yêu cầu được khi đội đã nhận nhiệm vụ và chưa hoàn tất (trạng thái hiện tại: {0}).", assignment.Status));
        }

        var existing = await _requestRepository.GetByIncidentAsync(assignment.IncidentId, cancellationToken);
        if (existing.Any(r => r.AssignmentId == assignment.Id && r.Type == dto.Type && r.IsOpen))
        {
            return ApiResponseDto<FieldRequestDto>.ConflictResult("Đội đã có một yêu cầu cùng loại đang chờ xử lý. Vui lòng chờ phản hồi hoặc hủy yêu cầu cũ.");
        }

        var request = new FieldRequest
        {
            IncidentId = assignment.IncidentId,
            AssignmentId = assignment.Id,
            RescueUnitId = assignment.RescueUnitId,
            RequestedByUserId = staffUserId,
            Type = dto.Type,
            Status = FieldRequestStatus.Open,
            RequestedUnitType = dto.Type == FieldRequestType.Backup ? dto.RequestedUnitType : null,
            Quantity = dto.Type == FieldRequestType.Backup ? dto.Quantity : 0,
            Description = description
        };

        await _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
        {
            IncidentId = request.IncidentId,
            ActorId = staffUserId,
            ActorRole = UserRole.RescueStaff.ToString(),
            Action = AuditAction.FieldRequestCreated,
            Description = $"Xe {assignment.RescueUnit.PlateNumber} gửi yêu cầu {Describe(request)}",
            NewStateJson = JsonSerializer.Serialize(new
            {
                requestId = request.Id,
                type = request.Type.ToString(),
                requestedUnitType = request.RequestedUnitType?.ToString(),
                quantity = request.Quantity,
                description
            }),
            IpAddress = _currentUser.IpAddress
        }, cancellationToken);
        await _requestRepository.AddAsync(request);

        var saved = await _requestRepository.GetWithDetailsAsync(request.Id, cancellationToken) ?? request;
        var result = Map(saved);
        await PublishAsync("Created", result, cancellationToken);

        await NotifyOperatorsAsync(saved, new NotificationRequest
        {
            Type = NotificationType.StatusUpdate,
            Title = request.Type == FieldRequestType.Backup ? "YÊU CẦU CHI VIỆN TỪ HIỆN TRƯỜNG" : "Yêu cầu từ hiện trường",
            Message = $"Xe {result.PlateNumber} ({saved.Incident?.Title}): {Describe(request)}",
            IncidentId = request.IncidentId,
            Data = new { fieldRequestId = request.Id },
            HighPriority = request.Type == FieldRequestType.Backup
        }, cancellationToken);

        return ApiResponseDto<FieldRequestDto>.SuccessResult(result, "Đã gửi yêu cầu về trung tâm điều phối.");
    }

    // =====================================================================================
    // Xem
    // =====================================================================================
    public async Task<ApiResponseDto<List<FieldRequestDto>>> GetOpenAsync(CancellationToken cancellationToken = default)
    {
        var stationId = _currentUser.FilterStationId();
        var requests = await _requestRepository.GetOpenAsync(null, cancellationToken);

        // Operator: yêu cầu của sự cố trạm mình + của xe trạm mình đi hỗ trợ trạm khác
        if (stationId.HasValue)
        {
            requests = requests.Where(r => r.Incident?.ResponsibleStationId == null
                                      || r.Incident.ResponsibleStationId == stationId
                                      || r.RescueUnit?.StationId == stationId)
                .ToList();
        }

        return ApiResponseDto<List<FieldRequestDto>>.SuccessResult(requests.Select(Map).ToList());
    }

    public async Task<ApiResponseDto<List<FieldRequestDto>>> GetByIncidentAsync(Guid incidentId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var incident = await _incidentRepository.GetIncidentWithDetailsAsync(incidentId, cancellationToken);
        if (incident == null)
        {
            return ApiResponseDto<List<FieldRequestDto>>.NotFoundResult("Không tìm thấy sự cố.");
        }

        var requests = await _requestRepository.GetByIncidentAsync(incidentId, cancellationToken);

        if (_currentUser.Role == UserRole.RescueStaff)
        {
            // Staff chỉ thấy yêu cầu của xe thuộc tổ mình (xe chưa gán tổ: xe trạm mình)
            var staffStation = _currentUser.StationId;
            var staffTeam = _currentUser.UserId is Guid currentId ? await _userRepository.GetTeamIdAsync(currentId, cancellationToken) : null;
            requests = requests.Where(r => CrewScope.CanWorkOn(staffStation, staffTeam, r.RescueUnit)).ToList();
            if (requests.Count == 0 && !incident.DispatchAssignments.Any(a => CrewScope.CanWorkOn(staffStation, staffTeam, a.RescueUnit)))
            {
                return ApiResponseDto<List<FieldRequestDto>>.ForbiddenResult("Bạn không có quyền xem yêu cầu của sự cố này.");
            }
        }
        else
        {
            var scopeError = CheckScope(incident.ResponsibleStationId, incident.DispatchAssignments.Select(a => a.RescueUnit?.StationId));
            if (scopeError != null)
            {
                return ApiResponseDto<List<FieldRequestDto>>.ForbiddenResult(scopeError);
            }
        }

        return ApiResponseDto<List<FieldRequestDto>>.SuccessResult(requests.Select(Map).ToList());
    }

    // =====================================================================================
    // Operator phản hồi
    // =====================================================================================
    public async Task<ApiResponseDto<FieldRequestDto>> AcknowledgeAsync(Guid requestId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var (request, error) = await LoadForOperatorAsync(requestId, cancellationToken);
        if (error != null) return error;

        if (request!.Status != FieldRequestStatus.Open)
        {
            return ApiResponseDto<FieldRequestDto>.ConflictResult(
                LocalizationHelper.Localize("Yêu cầu đã ở trạng thái {0}, không thể tiếp nhận.", request.Status));
        }

        var now = DateTime.UtcNow;
        request.Status = FieldRequestStatus.Acknowledged;
        request.AcknowledgedByUserId = actorUserId;
        request.AcknowledgedAt = now;
        request.UpdatedAt = now;

        await AuditAsync(request, actorUserId, OperatorRole(), AuditAction.FieldRequestAcknowledged,
            $"Trung tâm đã tiếp nhận yêu cầu {Describe(request)}", FieldRequestStatus.Open, null, cancellationToken);
        await _requestRepository.SaveChangesAsync(cancellationToken);

        var result = Map(request);
        await PublishAsync("Acknowledged", result, cancellationToken);
        await NotifyRequesterAsync(request, "Trung tâm đã tiếp nhận yêu cầu", $"Yêu cầu {Describe(request)} đang được xử lý.", cancellationToken);
        return ApiResponseDto<FieldRequestDto>.SuccessResult(result, "Đã tiếp nhận yêu cầu.");
    }

    public async Task<ApiResponseDto<FieldRequestDto>> FulfillAsync(Guid requestId, string? note, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var (request, error) = await LoadForOperatorAsync(requestId, cancellationToken);
        if (error != null) return error;

        if (!request!.IsOpen)
        {
            return ApiResponseDto<FieldRequestDto>.ConflictResult(
                LocalizationHelper.Localize("Yêu cầu đã ở trạng thái {0}, không thể cập nhật.", request.Status));
        }

        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed is { Length: > MaxNoteLength })
        {
            return ApiResponseDto<FieldRequestDto>.FailureResult(
                LocalizationHelper.Localize("Ghi chú tối đa {0} ký tự.", MaxNoteLength));
        }

        await ResolveAsync(request, FieldRequestStatus.Fulfilled, trimmed, actorUserId, OperatorRole(), AuditAction.FieldRequestFulfilled,
            $"Trung tâm đã đáp ứng yêu cầu {Describe(request)}" + (trimmed != null ? $". Ghi chú: {trimmed}" : string.Empty), cancellationToken);

        var result = Map(request);
        await PublishAsync("Fulfilled", result, cancellationToken);
        await NotifyRequesterAsync(request, "Yêu cầu đã được đáp ứng", trimmed ?? $"Trung tâm đã đáp ứng yêu cầu {Describe(request)}.", cancellationToken);
        return ApiResponseDto<FieldRequestDto>.SuccessResult(result, "Đã đánh dấu đáp ứng yêu cầu.");
    }

    public async Task<ApiResponseDto<FieldRequestDto>> DeclineAsync(Guid requestId, string? reason, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        if (trimmed.Length < MinReasonLength)
        {
            return ApiResponseDto<FieldRequestDto>.FailureResult(
                LocalizationHelper.Localize("Vui lòng nhập lý do từ chối (tối thiểu {0} ký tự).", MinReasonLength));
        }

        if (trimmed.Length > MaxNoteLength)
        {
            return ApiResponseDto<FieldRequestDto>.FailureResult(
                LocalizationHelper.Localize("Ghi chú tối đa {0} ký tự.", MaxNoteLength));
        }

        var (request, error) = await LoadForOperatorAsync(requestId, cancellationToken);
        if (error != null) return error;

        if (!request!.IsOpen)
        {
            return ApiResponseDto<FieldRequestDto>.ConflictResult(
                LocalizationHelper.Localize("Yêu cầu đã ở trạng thái {0}, không thể cập nhật.", request.Status));
        }

        await ResolveAsync(request, FieldRequestStatus.Declined, trimmed, actorUserId, OperatorRole(), AuditAction.FieldRequestDeclined,
            $"Trung tâm từ chối yêu cầu {Describe(request)}. Lý do: {trimmed}", cancellationToken);

        var result = Map(request);
        await PublishAsync("Declined", result, cancellationToken);
        await NotifyRequesterAsync(request, "Yêu cầu bị từ chối", $"Lý do: {trimmed}", cancellationToken);
        return ApiResponseDto<FieldRequestDto>.SuccessResult(result, "Đã từ chối yêu cầu.");
    }

    public async Task<ApiResponseDto<FieldRequestDto>> CancelAsync(Guid requestId, string? reason, Guid staffUserId, CancellationToken cancellationToken = default)
    {
        var request = await _requestRepository.GetWithDetailsAsync(requestId, cancellationToken);
        if (request == null)
        {
            return ApiResponseDto<FieldRequestDto>.NotFoundResult("Không tìm thấy yêu cầu.");
        }

        var staff = await _userRepository.GetByIdAsync(staffUserId);
        if (staff == null || staff.Role != UserRole.RescueStaff
            || !CrewScope.CanWorkOn(staff.StationId, await _userRepository.GetTeamIdAsync(staff.Id, cancellationToken), request.RescueUnit))
        {
            return ApiResponseDto<FieldRequestDto>.ForbiddenResult("Chỉ đội nhận lệnh của xe (tổ phụ trách xe) được hủy yêu cầu này.");
        }

        if (!request.IsOpen)
        {
            return ApiResponseDto<FieldRequestDto>.ConflictResult(
                LocalizationHelper.Localize("Yêu cầu đã ở trạng thái {0}, không thể cập nhật.", request.Status));
        }

        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmed is { Length: > MaxNoteLength })
        {
            return ApiResponseDto<FieldRequestDto>.FailureResult(
                LocalizationHelper.Localize("Ghi chú tối đa {0} ký tự.", MaxNoteLength));
        }

        await ResolveAsync(request, FieldRequestStatus.Cancelled, trimmed, staffUserId, UserRole.RescueStaff.ToString(), AuditAction.FieldRequestCancelled,
            $"Đội tự hủy yêu cầu {Describe(request)}" + (trimmed != null ? $". Lý do: {trimmed}" : string.Empty), cancellationToken);

        var result = Map(request);
        await PublishAsync("Cancelled", result, cancellationToken);
        return ApiResponseDto<FieldRequestDto>.SuccessResult(result, "Đã hủy yêu cầu.");
    }

    // =====================================================================================
    // Giao thêm xe theo yêu cầu (gọi từ DispatchService)
    // =====================================================================================
    public async Task<string?> ValidateForDispatchAsync(Guid requestId, Guid incidentId, CancellationToken cancellationToken = default)
    {
        var request = await _requestRepository.GetWithDetailsAsync(requestId, cancellationToken);
        if (request == null || request.IncidentId != incidentId)
        {
            return LocalizationHelper.Localize("Không tìm thấy yêu cầu chi viện của sự cố này.");
        }

        if (request.Type != FieldRequestType.Backup)
        {
            return LocalizationHelper.Localize("Chỉ yêu cầu loại Backup mới được dùng để giao thêm xe.");
        }

        return request.IsOpen
            ? null
            : LocalizationHelper.Localize("Yêu cầu đã ở trạng thái {0}, không thể cập nhật.", request.Status);
    }

    public async Task MarkFulfilledByDispatchAsync(Guid requestId, IReadOnlyCollection<string> plates, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = await _requestRepository.GetWithDetailsAsync(requestId, cancellationToken);
            if (request == null || !request.IsOpen)
            {
                return;
            }

            var note = $"Đã điều thêm xe: {string.Join(", ", plates)}";
            await ResolveAsync(request, FieldRequestStatus.Fulfilled, note, actorUserId, OperatorRole(), AuditAction.FieldRequestFulfilled,
                $"Đáp ứng yêu cầu {Describe(request)}. {note}", cancellationToken);

            await PublishAsync("Fulfilled", Map(request), cancellationToken);
            await NotifyRequesterAsync(request, "Đã điều xe chi viện", note, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Việc giao xe đã thành công; không làm hỏng phản hồi vì lỗi cập nhật yêu cầu
            _logger.LogError(ex, "Không cập nhật được yêu cầu hiện trường {RequestId} sau khi giao xe", requestId);
        }
    }

    // =====================================================================================
    // Escalation daemon
    // =====================================================================================
    public async Task<int> ProcessOverdueAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetEscalationSettingsAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var overdue = await _requestRepository.GetOverdueAsync(now.AddMinutes(-settings.FieldRequestThresholdMinutes), cancellationToken);
        if (overdue.Count == 0)
        {
            return 0;
        }

        var admins = await _userRepository.GetActiveByRolesAsync(new[] { UserRole.Admin });
        var operators = await _userRepository.GetActiveByRolesAsync(new[] { UserRole.Operator });
        var escalated = 0;

        foreach (var request in overdue)
        {
            // Sự cố đã đóng / lượt điều phối đã kết thúc → yêu cầu không còn ý nghĩa, tự hủy thay vì báo động
            var incidentClosed = request.Incident?.Status is IncidentStatus.Completed or IncidentStatus.Cancelled;
            var assignmentEnded = request.Assignment is { IsActive: false };
            if (incidentClosed || assignmentEnded)
            {
                await ResolveAsync(request, FieldRequestStatus.Cancelled, "Tự hủy: sự cố / lượt điều phối đã kết thúc.", null, SystemRole,
                    AuditAction.FieldRequestCancelled, $"Hệ thống tự hủy yêu cầu {Describe(request)} vì sự cố / lượt điều phối đã kết thúc", cancellationToken);
                await PublishAsync("Cancelled", Map(request), cancellationToken);
                continue;
            }

            request.EscalatedAt = now;
            request.UpdatedAt = now;
            var minutes = Math.Round((now - request.CreatedAt).TotalMinutes, 1);
            await AuditAsync(request, null, SystemRole, AuditAction.FieldRequestEscalated,
                $"Yêu cầu {Describe(request)} quá {settings.FieldRequestThresholdMinutes} phút chưa được xử lý ({minutes} phút) — đã báo Admin",
                request.Status, null, cancellationToken);
            await _requestRepository.SaveChangesAsync(cancellationToken);
            escalated++;

            var result = Map(request);
            await PublishAsync("Escalated", result, cancellationToken);

            var stations = new[] { request.Incident?.ResponsibleStationId, request.RescueUnit?.StationId };
            var recipients = admins.Select(u => u.Id)
                .Concat(operators.Where(o => o.StationId.HasValue && stations.Contains(o.StationId)).Select(o => o.Id))
                .Distinct()
                .ToList();
            if (recipients.Count > 0)
            {
                await _notificationService.NotifyUsersAsync(recipients, new NotificationRequest
                {
                    Type = NotificationType.Escalation,
                    Title = "YÊU CẦU HIỆN TRƯỜNG QUÁ HẠN",
                    Message = $"Xe {result.PlateNumber} ({request.Incident?.Title}) chờ {minutes} phút: {Describe(request)}",
                    IncidentId = request.IncidentId,
                    Data = new { fieldRequestId = request.Id },
                    HighPriority = true
                }, cancellationToken);
            }
        }

        if (escalated > 0)
        {
            _logger.LogWarning("Đã báo {Count} yêu cầu hiện trường quá hạn", escalated);
        }

        return escalated;
    }

    // =====================================================================================
    // Helpers
    // =====================================================================================
    private async Task<(FieldRequest? Request, ApiResponseDto<FieldRequestDto>? Error)> LoadForOperatorAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var request = await _requestRepository.GetWithDetailsAsync(requestId, cancellationToken);
        if (request == null)
        {
            return (null, ApiResponseDto<FieldRequestDto>.NotFoundResult("Không tìm thấy yêu cầu."));
        }

        var scopeError = CheckScope(request.Incident?.ResponsibleStationId, new[] { request.RescueUnit?.StationId });
        return scopeError != null
            ? (null, ApiResponseDto<FieldRequestDto>.ForbiddenResult(scopeError))
            : (request, null);
    }

    /// <summary>
    /// Operator trạm phụ trách sự cố hoặc trạm của xe (trạm hỗ trợ) đều được xử lý; Admin không giới hạn
    /// </summary>
    private string? CheckScope(Guid? responsibleStationId, IEnumerable<Guid?> unitStationIds)
    {
        var incidentError = _currentUser.CheckIncident(responsibleStationId);
        if (incidentError == null)
        {
            return null;
        }

        return unitStationIds.Any(s => s.HasValue && _currentUser.CheckStation(s) == null) ? null : incidentError;
    }

    private async Task ResolveAsync(FieldRequest request, FieldRequestStatus status, string? note, Guid? actorUserId, string actorRole,
        AuditAction action, string description, CancellationToken cancellationToken)
    {
        var oldStatus = request.Status;
        var now = DateTime.UtcNow;
        request.Status = status;
        request.ResolvedByUserId = actorUserId;
        request.ResolvedAt = now;
        request.ResponseNote = note;
        request.UpdatedAt = now;

        await AuditAsync(request, actorUserId, actorRole, action, description, oldStatus, note, cancellationToken);
        await _requestRepository.SaveChangesAsync(cancellationToken);
    }

    private Task AuditAsync(FieldRequest request, Guid? actorUserId, string actorRole, AuditAction action, string description,
        FieldRequestStatus oldStatus, string? note, CancellationToken cancellationToken)
        => _incidentRepository.AddAuditTrailAsync(new IncidentAuditTrail
        {
            IncidentId = request.IncidentId,
            ActorId = actorUserId,
            ActorRole = actorRole,
            Action = action,
            Description = description,
            OldStateJson = JsonSerializer.Serialize(new { requestId = request.Id, status = oldStatus.ToString() }),
            NewStateJson = JsonSerializer.Serialize(new { requestId = request.Id, status = request.Status.ToString(), note, escalatedAt = request.EscalatedAt }),
            IpAddress = actorRole == SystemRole ? null : _currentUser.IpAddress
        }, cancellationToken);

    private async Task PublishAsync(string action, FieldRequestDto dto, CancellationToken cancellationToken)
    {
        // Chỉ đội nhận lệnh của xe gửi yêu cầu (tổ khác cùng trạm không nhận)
        var crewIds = dto.UnitStationId == Guid.Empty
            ? Array.Empty<Guid>()
            : await _notificationService.GetCrewUserIdsAsync(dto.UnitStationId, dto.UnitTeamId, cancellationToken) ?? (IReadOnlyCollection<Guid>)Array.Empty<Guid>();
        await _realtimeNotifier.FieldRequestChangedAsync(new FieldRequestEventDto { Action = action, Request = dto, ChangedAt = DateTime.UtcNow }, crewIds, cancellationToken);
    }

    private async Task NotifyOperatorsAsync(FieldRequest request, NotificationRequest notification, CancellationToken cancellationToken)
    {
        var stations = new[] { request.Incident?.ResponsibleStationId, request.RescueUnit?.StationId };
        var operators = await _userRepository.GetActiveByRolesAsync(new[] { UserRole.Operator });
        var ids = operators
            .Where(o => o.StationId.HasValue && stations.Contains(o.StationId))
            .Select(o => o.Id)
            .ToList();

        // Sự cố chưa có trạm / trạm chưa có Operator → mọi Operator đều nhận để không bị bỏ sót
        if (ids.Count == 0)
        {
            ids = operators.Select(o => o.Id).ToList();
        }

        if (ids.Count > 0)
        {
            await _notificationService.NotifyUsersAsync(ids, notification, cancellationToken);
        }
    }

    private Task NotifyRequesterAsync(FieldRequest request, string title, string message, CancellationToken cancellationToken)
        => _notificationService.NotifyUsersAsync(new[] { request.RequestedByUserId }, new NotificationRequest
        {
            Type = NotificationType.StatusUpdate,
            Title = title,
            Message = message,
            IncidentId = request.IncidentId,
            Data = new { fieldRequestId = request.Id, status = request.Status.ToString() }
        }, cancellationToken);

    private string OperatorRole() => _currentUser.Role?.ToString() ?? UserRole.Operator.ToString();

    private static string Describe(FieldRequest r) => r.Type == FieldRequestType.Backup
        ? $"chi viện {r.Quantity} xe{(r.RequestedUnitType is { } t ? $" {t}" : string.Empty)}{(r.Description != null ? $" — {r.Description}" : string.Empty)}"
        : $"khác — {r.Description}";

    internal static FieldRequestDto Map(FieldRequest r) => new()
    {
        Id = r.Id,
        IncidentId = r.IncidentId,
        IncidentTitle = r.Incident?.Title,
        IncidentAddress = r.Incident?.LocationAddress,
        IncidentSeverity = r.Incident?.Severity,
        ResponsibleStationId = r.Incident?.ResponsibleStationId,
        AssignmentId = r.AssignmentId,
        RescueUnitId = r.RescueUnitId,
        PlateNumber = r.RescueUnit?.PlateNumber,
        UnitStationId = r.RescueUnit?.StationId ?? Guid.Empty,
        UnitTeamId = r.RescueUnit?.StationTeamId,
        RequestedByUserId = r.RequestedByUserId,
        RequestedByName = r.RequestedByUser?.FullName,
        Type = r.Type,
        Status = r.Status,
        RequestedUnitType = r.RequestedUnitType,
        Quantity = r.Quantity,
        Description = r.Description,
        CreatedAt = r.CreatedAt,
        AcknowledgedByUserId = r.AcknowledgedByUserId,
        AcknowledgedAt = r.AcknowledgedAt,
        ResolvedByUserId = r.ResolvedByUserId,
        ResolvedAt = r.ResolvedAt,
        ResponseNote = r.ResponseNote,
        EscalatedAt = r.EscalatedAt
    };
}
