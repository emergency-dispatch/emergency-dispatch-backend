using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.DTOs.Notification;
using EmergencyDispatch.Application.DTOs.Settings;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Localization;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Application.Services;

/// <summary>Station ownership handover. Never creates, cancels or reassigns a rescue mission.</summary>
public sealed class StationTransferService(
    IIncidentRepository incidents,
    IIncidentStationTransferRepository transfers,
    IStationRepository stations,
    IUserRepository users,
    ISystemSettingsService settings,
    IRoutingService routing,
    ICurrentUserContext currentUser,
    IRealtimeNotifier realtime,
    INotificationService notifications,
    ILogger<StationTransferService> logger,
    TimeProvider? timeProvider = null) : IStationTransferService
{
    private DateTime Now => (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    private static bool Closed(Incident i) => i.Status is IncidentStatus.Completed or IncidentStatus.Cancelled or IncidentStatus.Linked
        || i.DuplicateOfIncidentId.HasValue;

    public async Task InitializeAsync(Incident incident, bool readyForOperator, Guid? acknowledgedBy = null, CancellationToken cancellationToken = default)
    {
        if (!incident.ResponsibleStationId.HasValue || Closed(incident)) return;
        var config = await settings.GetStationTransferSettingsAsync(cancellationToken) ?? new();
        var escalation = await settings.GetEscalationSettingsAsync(cancellationToken) ?? new();
        incident.StationAssignedAt = Now;
        incident.StationTransferChainId = Guid.NewGuid();
        incident.StationAssignmentVersion = 1;
        incident.StationResponseDueAt = readyForOperator ? StationTransferLifecycle.Deadline(Now, escalation, config) : null;
        if (acknowledgedBy.HasValue) StationTransferLifecycle.Acknowledge(incident, acknowledgedBy.Value, Now);
    }

    public async Task MarkReadyForOperatorAsync(Guid incidentId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await incidents.BeginLockedTransactionAsync(incidentId, cancellationToken);
        incidents.ClearChangeTracker();
        var incident = await incidents.GetIncidentWithDetailsAsync(incidentId, cancellationToken);
        if (incident == null || !incident.StationAssignedAt.HasValue || incident.StationResponseDueAt.HasValue
            || !StationTransferLifecycle.IsWaitingStatus(incident.Status) || StationTransferLifecycle.HasDispatchedWork(incident)) return;
        var config = await settings.GetStationTransferSettingsAsync(cancellationToken) ?? new();
        var escalation = await settings.GetEscalationSettingsAsync(cancellationToken) ?? new();
        incident.StationResponseDueAt = StationTransferLifecycle.Deadline(Now, escalation, config);
        await incidents.SaveChangesAsync(cancellationToken);
        if (transaction != null) await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ApiResponseDto<StationAcknowledgementDto>> AcknowledgeAsync(
        Guid incidentId, Guid actorUserId, int? expectedVersion, CancellationToken cancellationToken = default)
    {
        await using var transaction = await incidents.BeginLockedTransactionAsync(incidentId, cancellationToken);
        incidents.ClearChangeTracker();
        var incident = await incidents.GetIncidentWithDetailsAsync(incidentId, cancellationToken);
        if (incident == null) return ApiResponseDto<StationAcknowledgementDto>.NotFoundResult("Không tìm thấy sự cố.");
        var actor = await users.GetByIdAsync(actorUserId);
        if (!CanCommand(actor, incident)) return ApiResponseDto<StationAcknowledgementDto>.ForbiddenResult("Bạn không có quyền xử lý sự cố này.");
        if (Closed(incident)) return ApiResponseDto<StationAcknowledgementDto>.FailureResult(StationTransferMessages.Closed);
        if (!incident.ResponsibleStationId.HasValue) return ApiResponseDto<StationAcknowledgementDto>.FailureResult(StationTransferMessages.Changed);
        if (expectedVersion.HasValue && expectedVersion != incident.StationAssignmentVersion)
            return ApiResponseDto<StationAcknowledgementDto>.ConflictResult(StationTransferMessages.Changed);
        if (incident.IsLockedByOther(actorUserId, Now))
            return ApiResponseDto<StationAcknowledgementDto>.ConflictResult(StationTransferMessages.Locked);
        var first = !incident.StationAcknowledgedAt.HasValue;
        StationTransferLifecycle.Acknowledge(incident, actorUserId, Now);
        if (first)
        {
            await incidents.AddAuditTrailAsync(new IncidentAuditTrail
            {
                IncidentId = incident.Id, ActorId = actorUserId, ActorRole = actor!.Role.ToString(),
                Action = AuditAction.StationAcknowledged, Description = StationTransferMessages.Acknowledged,
                NewStateJson = System.Text.Json.JsonSerializer.Serialize(new { incident.ResponsibleStationId, incident.StationAssignmentVersion })
            }, cancellationToken);
            await incidents.SaveChangesAsync(cancellationToken);
        }
        if (transaction != null) await transaction.CommitAsync(cancellationToken);
        if (first) await realtime.IncidentUpdatedAsync(IncidentService.MapToResponseDto(incident), cancellationToken);
        return ApiResponseDto<StationAcknowledgementDto>.SuccessResult(new()
        {
            IncidentId = incident.Id, ResponsibleStationId = incident.ResponsibleStationId,
            AssignmentVersion = incident.StationAssignmentVersion, AssignedAt = incident.StationAssignedAt,
            ResponseDueAt = incident.StationResponseDueAt, AcknowledgedAt = incident.StationAcknowledgedAt,
            AcknowledgedByUserId = incident.StationAcknowledgedByUserId
        }, StationTransferMessages.Acknowledged);
    }

    private static bool CanCommand(User? actor, Incident incident) =>
        actor is { IsDeleted: false, Status: UserStatus.Active }
        && (actor.Role == UserRole.Admin || actor.Role == UserRole.Operator
            && actor.StationId.HasValue && actor.StationId == incident.ResponsibleStationId);

    private async Task<List<User>> OperatorsAsync()
        => (await users.GetActiveByRolesAsync(new[] { UserRole.Operator }) ?? new List<User>())
            .Where(u => !u.IsDeleted && u.Status == UserStatus.Active && u.Role == UserRole.Operator).ToList();

    public async Task<ApiResponseDto<IncidentResponseDto>> TransferAsync(
        Guid incidentId, TransferIncidentDto dto, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await incidents.BeginLockedTransactionAsync(incidentId, cancellationToken);
        incidents.ClearChangeTracker();
        var incident = await incidents.GetIncidentWithDetailsAsync(incidentId, cancellationToken);
        if (incident == null) return ApiResponseDto<IncidentResponseDto>.NotFoundResult("Không tìm thấy sự cố.");
        var actor = await users.GetByIdAsync(actorUserId);
        if (!CanCommand(actor, incident)) return ApiResponseDto<IncidentResponseDto>.ForbiddenResult("Bạn không có quyền xử lý sự cố này.");
        if (Closed(incident)) return ApiResponseDto<IncidentResponseDto>.FailureResult(StationTransferMessages.Closed);
        if (dto.ExpectedAssignmentVersion.HasValue && dto.ExpectedAssignmentVersion != incident.StationAssignmentVersion)
            return ApiResponseDto<IncidentResponseDto>.ConflictResult(StationTransferMessages.Changed);
        if (incident.IsLockedByOther(actorUserId, Now) && actor!.Role != UserRole.Admin)
            return ApiResponseDto<IncidentResponseDto>.ConflictResult(StationTransferMessages.Locked);
        if (StationTransferLifecycle.HasDispatchedWork(incident) && (actor!.Role != UserRole.Admin || !dto.ConfirmActiveAssignments))
            return ApiResponseDto<IncidentResponseDto>.ConflictResult(StationTransferMessages.ActiveWork);
        if ((dto.Reason?.Trim().Length ?? 0) < 5)
            return ApiResponseDto<IncidentResponseDto>.FailureResult("Lý do chuyển trạm tối thiểu 5 ký tự.");
        if (dto.StationId == incident.ResponsibleStationId)
            return ApiResponseDto<IncidentResponseDto>.FailureResult("Sự cố đã thuộc trạm này.");
        var target = await stations.GetByIdAsync(dto.StationId);
        if (target == null || target.IsDeleted || !target.IsActive)
            return ApiResponseDto<IncidentResponseDto>.FailureResult("Trạm nhận đang tạm ngưng hoạt động.");
        var operators = await OperatorsAsync();
        if (!operators.Any(u => u.StationId == target.Id))
            return ApiResponseDto<IncidentResponseDto>.FailureResult(StationTransferMessages.NeedOperator);
        var config = await settings.GetStationTransferSettingsAsync(cancellationToken) ?? new();
        var escalation = await settings.GetEscalationSettingsAsync(cancellationToken) ?? new();
        var record = await ApplyTransferAsync(incident, target, "Manual", dto.Reason!.Trim(), actor, false,
            new StationSelection(target, "Manual", null), config, escalation, cancellationToken);
        if (transaction != null) await transaction.CommitAsync(cancellationToken);
        await PublishTransferAsync(incident, record, operators, cancellationToken);
        return ApiResponseDto<IncidentResponseDto>.SuccessResult(IncidentService.MapToResponseDto(incident), StationTransferMessages.Transferred);
    }

    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        var config = await settings.GetStationTransferSettingsAsync(cancellationToken) ?? new();
        if (!config.Enabled) return 0;
        var escalation = await settings.GetEscalationSettingsAsync(cancellationToken) ?? new();
        var count = 0;
        foreach (var id in await transfers.GetCandidateIdsAsync(Now, 100, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { if (await ProcessOneAsync(id, config, escalation, cancellationToken)) count++; }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                // Discard tracked mutations of a failed transaction before processing another incident.
                incidents.ClearChangeTracker();
                logger.LogError(ex, "Automatic station handover failed for incident {IncidentId}.", id);
            }
        }
        return count;
    }

    private async Task<bool> ProcessOneAsync(Guid id, StationTransferSettingsDto config,
        EscalationSettingsDto escalation, CancellationToken ct)
    {
        var snapshot = await incidents.GetIncidentWithDetailsAsync(id, ct);
        if (snapshot == null) return false;
        var operators = await OperatorsAsync();
        var source = snapshot.ResponsibleStationId.HasValue ? await stations.GetByIdAsync(snapshot.ResponsibleStationId.Value) : null;
        var reason = StationTransferLifecycle.AutomaticReason(snapshot, source,
            operators.Any(u => u.StationId == snapshot.ResponsibleStationId), Now);
        if (reason == null) return false;
        var version = snapshot.StationAssignmentVersion;
        var sourceId = snapshot.ResponsibleStationId;
        var history = await transfers.GetHistoryAsync(id, ct);
        var visited = history.Where(h => h.IsAutomatic && h.ChainId == snapshot.StationTransferChainId)
            .SelectMany(h => new Guid?[] { h.FromStationId, h.ToStationId }).Where(x => x.HasValue).Select(x => x!.Value).ToHashSet();
        if (sourceId.HasValue) visited.Add(sourceId.Value);
        var operatorStations = operators.Where(u => u.StationId.HasValue).Select(u => u.StationId!.Value).ToHashSet();
        var eligible = (await stations.GetAllAsync(s => s.IsActive)).Where(s =>
            s.IsActive && !s.IsDeleted && !visited.Contains(s.Id) && operatorStations.Contains(s.Id)).ToList();
        // External routing is outside the transaction so a slow provider does not block acknowledgements.
        var selected = snapshot.AutomaticStationTransferCount < config.MaxAutomaticTransfers
            ? await StationAssignment.SelectResponsibleStationAsync(eligible, snapshot.Latitude, snapshot.Longitude, routing, logger, ct)
            : null;

        IncidentStationTransfer? record = null;
        string? alert = null;
        await using (var transaction = await incidents.BeginLockedTransactionAsync(id, ct))
        {
            // Reload after locking: auto-dispatch, acknowledgement or a manual transfer may have won.
            incidents.ClearChangeTracker();
            var fresh = await incidents.GetIncidentWithDetailsAsync(id, ct);
            if (fresh == null || fresh.StationAssignmentVersion != version || fresh.ResponsibleStationId != sourceId) return false;
            source = sourceId.HasValue ? await stations.GetByIdAsync(sourceId.Value) : null;
            operators = await OperatorsAsync();
            reason = StationTransferLifecycle.AutomaticReason(fresh, source,
                operators.Any(u => u.StationId == sourceId), Now);
            if (reason == null) return false;
            // A setting change can disable handover while OSRM is answering.
            var liveConfig = await settings.GetStationTransferSettingsAsync(ct) ?? new();
            if (!liveConfig.Enabled) return false;
            if (fresh.AutomaticStationTransferCount >= liveConfig.MaxAutomaticTransfers)
                alert = "TransferLimit";
            else if (selected == null)
                alert = "NoTarget";
            else
            {
                var target = await stations.GetByIdAsync(selected.Station.Id);
                if (target == null || target.IsDeleted || !target.IsActive || !operators.Any(u => u.StationId == target.Id))
                    alert = "NoTarget";
                else
                    record = await ApplyTransferAsync(fresh, target, reason, ReasonText(reason), null, true,
                        selected, liveConfig, escalation, ct);
            }
            if (alert != null)
            {
                // Rotate blocked rows through the queue without repeating alerts or starving later incidents.
                fresh.StationTransferLastCheckedAt = Now;
                if (fresh.StationTransferAlertCode == alert)
                {
                    await incidents.SaveChangesAsync(ct);
                    if (transaction != null) await transaction.CommitAsync(ct);
                    return false;
                }
                fresh.StationTransferAlertCode = alert;
                fresh.StationTransferAlertedAt = Now;
                fresh.UpdatedAt = Now;
                await incidents.AddAuditTrailAsync(new IncidentAuditTrail
                {
                    IncidentId = fresh.Id, ActorRole = "System", Action = AuditAction.StationTransferBlocked,
                    Description = alert == "NoTarget" ? StationTransferMessages.NoTarget : StationTransferMessages.Limit
                }, ct);
                await incidents.SaveChangesAsync(ct);
            }
            if (transaction != null) await transaction.CommitAsync(ct);
            snapshot = fresh;
        }
        if (record != null) await PublishTransferAsync(snapshot, record, operators, ct);
        else if (alert != null)
        {
            await notifications.NotifyRolesAsync(new[] { UserRole.Admin }, new NotificationRequest
            {
                Type = NotificationType.Escalation, Title = StationTransferMessages.AlertTitle,
                Message = alert == "NoTarget" ? StationTransferMessages.NoTarget : StationTransferMessages.Limit,
                IncidentId = id, HighPriority = true
            }, ct);
            await realtime.IncidentUpdatedAsync(IncidentService.MapToResponseDto(snapshot), ct);
        }
        return record != null;
    }

    private async Task<IncidentStationTransfer> ApplyTransferAsync(Incident incident, Station target, string reasonCode,
        string reason, User? actor, bool automatic, StationSelection selection,
        StationTransferSettingsDto config, EscalationSettingsDto escalation, CancellationToken ct)
    {
        var now = Now;
        var from = incident.ResponsibleStationId.HasValue ? await stations.GetByIdAsync(incident.ResponsibleStationId.Value) : null;
        var chain = automatic ? incident.StationTransferChainId ?? Guid.NewGuid() : Guid.NewGuid();
        var record = new IncidentStationTransfer
        {
            IncidentId = incident.Id, FromStationId = incident.ResponsibleStationId, ToStationId = target.Id,
            FromStationName = from?.Name, ToStationName = target.Name, IsAutomatic = automatic,
            ChainId = chain, AssignmentVersion = incident.StationAssignmentVersion + 1, ReasonCode = reasonCode,
            Reason = reason, ActorUserId = actor?.Id, ActorName = actor?.FullName, CreatedAt = now,
            PreviousAssignedAt = incident.StationAssignedAt, PreviousAcknowledgedAt = incident.StationAcknowledgedAt,
            PreviousAcknowledgedByUserId = incident.StationAcknowledgedByUserId,
            SelectionMethod = selection.Method, SelectionDistanceKm = selection.DistanceKm
        };
        incident.ResponsibleStationId = target.Id;
        incident.ResponsibleStation = target;
        incident.StationAssignedAt = now;
        incident.StationResponseDueAt = StationTransferLifecycle.Deadline(now, escalation, config);
        incident.StationAcknowledgedAt = null;
        incident.StationAcknowledgedByUserId = null;
        incident.StationTransferChainId = chain;
        incident.StationAssignmentVersion = record.AssignmentVersion;
        incident.AutomaticStationTransferCount = automatic ? incident.AutomaticStationTransferCount + 1 : 0;
        incident.StationTransferAlertCode = null;
        incident.StationTransferAlertedAt = null;
        incident.StationTransferLastCheckedAt = null;
        incident.LockedByOperatorId = null;
        incident.LockExpiresAt = null;
        incident.UpdatedAt = now;
        await transfers.AddAsync(record, ct);
        await transfers.MoveLinkedReportsAsync(incident.Id, target.Id, ct);
        await incidents.AddAuditTrailAsync(new IncidentAuditTrail
        {
            IncidentId = incident.Id, ActorId = actor?.Id, ActorRole = actor?.Role.ToString() ?? "System",
            Action = automatic ? AuditAction.AutoTransferredStation : AuditAction.TransferredStation,
            Description = reason,
            OldStateJson = System.Text.Json.JsonSerializer.Serialize(new { responsibleStationId = record.FromStationId }),
            NewStateJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                responsibleStationId = target.Id, record.ReasonCode, record.AssignmentVersion, record.IsAutomatic,
                record.SelectionMethod, record.SelectionDistanceKm
            }), Timestamp = now
        }, ct);
        await incidents.SaveChangesAsync(ct);
        return record;
    }

    private async Task PublishTransferAsync(Incident incident, IncidentStationTransfer record, List<User> operators, CancellationToken ct)
    {
        if (record.FromStationId.HasValue)
            await realtime.IncidentLockChangedAsync(new IncidentLockEventDto { IncidentId = incident.Id, ChangedAt = record.CreatedAt }, record.FromStationId, ct);
        await realtime.IncidentTransferredAsync(new IncidentTransferredDto
        {
            IncidentId = incident.Id, IncidentTitle = incident.Title,
            FromStationId = record.FromStationId, FromStationName = record.FromStationName,
            ToStationId = record.ToStationId, ToStationName = record.ToStationName,
            Reason = LocalizationHelper.Localize(record.Reason),
            TransferredByName = record.ActorName ?? "System", TransferredAt = record.CreatedAt,
            IsAutomatic = record.IsAutomatic, ReasonCode = record.ReasonCode, AssignmentVersion = record.AssignmentVersion
        }, IncidentService.MapToResponseDto(incident), ct);
        var admins = await users.GetActiveByRolesAsync(new[] { UserRole.Admin }) ?? new List<User>();
        var recipients = operators.Where(u => u.StationId == record.FromStationId || u.StationId == record.ToStationId)
            .Select(u => u.Id).Concat(admins.Select(u => u.Id)).Distinct().ToArray();
        await notifications.NotifyUsersAsync(recipients, new NotificationRequest
        {
            Type = NotificationType.StatusUpdate, Title = StationTransferMessages.TransferTitle,
            Message = new LocalizedText(StationTransferMessages.TransferBody, incident.Title,
                record.FromStationName ?? StationTransferMessages.NoStationName, record.ToStationName),
            IncidentId = incident.Id, HighPriority = true
        }, ct);
    }

    public async Task<ApiResponseDto<List<IncidentStationTransferDto>>> GetHistoryAsync(Guid incidentId, CancellationToken cancellationToken = default)
    {
        incidents.ClearChangeTracker();
        var incident = await incidents.GetIncidentWithDetailsAsync(incidentId, cancellationToken);
        if (incident == null) return ApiResponseDto<List<IncidentStationTransferDto>>.NotFoundResult("Không tìm thấy sự cố.");
        if (currentUser.Role is not (UserRole.Admin or UserRole.Operator)
            || currentUser.CheckIncident(incident.ResponsibleStationId) != null)
            return ApiResponseDto<List<IncidentStationTransferDto>>.ForbiddenResult("Bạn không có quyền xử lý sự cố này.");
        var records = await transfers.GetHistoryAsync(incidentId, cancellationToken);
        return ApiResponseDto<List<IncidentStationTransferDto>>.SuccessResult(records.Select(h => new IncidentStationTransferDto
        {
            Id = h.Id, IncidentId = h.IncidentId, FromStationId = h.FromStationId, ToStationId = h.ToStationId,
            FromStationName = h.FromStationName, ToStationName = h.ToStationName, AssignmentVersion = h.AssignmentVersion,
            IsAutomatic = h.IsAutomatic, ReasonCode = h.ReasonCode, Reason = LocalizationHelper.Localize(h.Reason),
            ActorUserId = h.ActorUserId, ActorName = h.ActorName, TransferredAt = h.CreatedAt,
            PreviousAssignedAt = h.PreviousAssignedAt, PreviousAcknowledgedAt = h.PreviousAcknowledgedAt,
            PreviousAcknowledgedByUserId = h.PreviousAcknowledgedByUserId,
            SelectionMethod = h.SelectionMethod, SelectionDistanceKm = h.SelectionDistanceKm
        }).ToList());
    }

    private static string ReasonText(string code) => code switch
    {
        "NoOperator" => StationTransferMessages.NoOperator,
        "StationUnavailable" => StationTransferMessages.Unavailable,
        _ => StationTransferMessages.NoResponse
    };
}
