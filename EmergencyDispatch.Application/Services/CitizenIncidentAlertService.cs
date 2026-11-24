using System.Text.Json;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Notification;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Localization;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Application.Services;

public sealed class CitizenIncidentAlertService(
    ICitizenIncidentAlertRepository alerts, IIncidentRepository incidents, IUserRepository users,
    ISystemSettingsService settings, IPushNotificationSender push, IRealtimeNotifier realtime,
    ILogger<CitizenIncidentAlertService> logger, TimeProvider? timeProvider = null) : ICitizenIncidentAlertService
{
    private DateTime Now => (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    public async Task<int> PublishPendingAsync(CancellationToken ct = default)
    {
        var config = await settings.GetCitizenIncidentAlertSettingsAsync(ct);
        if (config is not { Enabled: true, EnabledFromUtc: not null }) return 0;
        var count = 0;
        foreach (var id in await alerts.GetCandidateIdsAsync(config.EnabledFromUtc.Value, 100, ct))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var transaction = await incidents.BeginLockedTransactionAsync(id, ct);
                incidents.ClearChangeTracker();
                var liveConfig = await settings.GetCitizenIncidentAlertSettingsAsync(ct);
                if (liveConfig is not { Enabled: true, EnabledFromUtc: not null }) break;
                var incident = await alerts.GetIncidentSnapshotAsync(id, ct);
                var trigger = incident == null ? null : CitizenIncidentAlertPolicy.Trigger(incident, liveConfig.EnabledFromUtc.Value);
                if (trigger == null || await alerts.GetByIncidentAsync(id, ct) != null) continue;
                var area = CitizenIncidentAlertPolicy.Area(incident!);
                var alert = new CitizenIncidentAlert
                {
                    IncidentId = id, AreaVi = area.Vi, AreaEn = area.En,
                    TriggerKind = trigger.Value.Kind, TriggeredAt = trigger.Value.At, CreatedAt = Now
                };
                var recipients = (await users.GetActiveByRolesAsync(new[] { UserRole.Citizen }))
                    .Where(u => u.Role == UserRole.Citizen && u.Status == UserStatus.Active && !u.IsDeleted).DistinctBy(u => u.Id).ToList();
                var inbox = new List<Notification>(); var deliveries = new List<CitizenIncidentAlertDelivery>();
                foreach (var citizen in recipients)
                {
                    var notification = new Notification
                    {
                        UserId = citizen.Id, Type = NotificationType.CitizenIncidentAlert,
                        // IncidentId stays null: citizens must not open another person's private incident.
                        Title = new LocalizedText(CitizenIncidentAlertMessages.Title).Vi,
                        TitleEn = new LocalizedText(CitizenIncidentAlertMessages.Title).En,
                        Message = new LocalizedText(CitizenIncidentAlertMessages.Body, area.Vi).Vi,
                        MessageEn = new LocalizedText(CitizenIncidentAlertMessages.Body, area.En).En,
                        DataJson = JsonSerializer.Serialize(new { type = "citizen_incident_alert", publicAlertId = alert.Id }),
                        CreatedAt = Now, FcmSentSuccess = null
                    };
                    inbox.Add(notification);
                    deliveries.Add(new CitizenIncidentAlertDelivery
                    {
                        AlertId = alert.Id, UserId = citizen.Id, NotificationId = notification.Id, CreatedAt = Now, NextAttemptAt = Now
                    });
                }
                // Unique incident and recipient keys plus the incident lock protect concurrent AI/human confirmation.
                await alerts.AddAsync(alert, inbox, deliveries, ct);
                if (transaction != null) await transaction.CommitAsync(ct);
                count++;
                logger.LogInformation("Citizen area announcement {AlertId} persisted for {Count} recipients.", alert.Id, recipients.Count);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                incidents.ClearChangeTracker();
                logger.LogError(ex, "Could not persist citizen area announcement for {IncidentId}; the scan will retry.", id);
            }
        }
        return count;
    }

    public async Task<int> DeliverPendingAsync(CancellationToken ct = default)
    {
        if ((await settings.GetCitizenIncidentAlertSettingsAsync(ct)) is not { Enabled: true }) return 0;
        var handled = 0;
        for (var n = 0; n < 100; n++)
        {
            ct.ThrowIfCancellationRequested();
            incidents.ClearChangeTracker();
            if ((await settings.GetCitizenIncidentAlertSettingsAsync(ct)) is not { Enabled: true }) break;
            var claim = await alerts.TryClaimAsync(Now, ct);
            if (claim?.LeaseId == null) break;
            var terminal = false; var attemptedRealtime = false; bool? pushSuccess = null;
            string? messageId = null; string? error = null;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                var alert = await alerts.GetAlertAsync(claim.AlertId, timeout.Token);
                var incident = alert == null ? null : await alerts.GetIncidentSnapshotAsync(alert.IncidentId, timeout.Token);
                var user = await users.GetByIdAsync(claim.UserId);
                var notification = await alerts.GetNotificationAsync(claim.NotificationId, timeout.Token);
                if (alert == null || incident == null || CitizenIncidentAlertPolicy.Trigger(incident, DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)) == null
                    || user is not { IsDeleted: false, Status: UserStatus.Active, Role: UserRole.Citizen } || notification == null)
                {
                    terminal = true; error = "Announcement or recipient is no longer eligible.";
                }
                else
                {
                    var english = user.UiLanguage?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true;
                    var dto = NotificationDto(notification, english);
                    if (!claim.RealtimeAttemptedAt.HasValue)
                    {
                        try
                        {
                            await realtime.NotificationCreatedAsync(user.Id, dto, timeout.Token);
                            attemptedRealtime = true;
                        }
                        catch (Exception ex) when (!timeout.IsCancellationRequested)
                        {
                            error = "Temporary realtime delivery failure.";
                            logger.LogWarning(ex, "Realtime delivery {DeliveryId} failed; push will still be attempted.", claim.Id);
                        }
                    }
                    if (string.IsNullOrWhiteSpace(user.FcmToken))
                    {
                        // Inbox is committed. Retry only when realtime itself failed, not just for an absent token.
                        terminal = attemptedRealtime || claim.RealtimeAttemptedAt.HasValue;
                    }
                    else
                    {
                        var result = await push.SendAsync(user.FcmToken, dto.Title, dto.Message,
                            new Dictionary<string, string>
                            {
                                ["type"] = NotificationType.CitizenIncidentAlert.ToString(),
                                ["notificationId"] = notification.Id.ToString(), ["publicAlertId"] = alert.Id.ToString()
                            }, false, timeout.Token); // Citizen information is not a rescue staff siren.
                        pushSuccess = result.Success; messageId = result.MessageId; error = result.Error;
                        terminal = result.Success || result.TokenInvalid;
                        if (result.TokenInvalid) await alerts.ClearInvalidTokenAsync(user.Id, user.FcmToken, timeout.Token);
                    }
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                error = "Temporary delivery failure.";
                logger.LogWarning(ex, "Citizen announcement delivery {DeliveryId} failed; it will be retried within the attempt limit.", claim.Id);
            }
            if (!ct.IsCancellationRequested)
            {
                await alerts.FinishAttemptAsync(claim.Id, claim.LeaseId.Value, Now, terminal, attemptedRealtime,
                    pushSuccess, messageId, error, ct);
                handled++;
            }
        }
        return handled;
    }

    public async Task<PaginatedResultDto<CitizenIncidentAlertDto>> GetMineAsync(Guid userId, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        pageIndex = Math.Max(1, pageIndex); pageSize = Math.Clamp(pageSize, 1, 100);
        var result = await alerts.GetForUserAsync(userId, pageIndex, pageSize, ct);
        return new(result.Items.Select(Map), result.Total, pageIndex, pageSize);
    }

    public async Task<ApiResponseDto<CitizenIncidentAlertDto>> GetMineAsync(Guid alertId, Guid userId, CancellationToken ct = default)
    {
        var alert = await alerts.GetForUserAsync(alertId, userId, ct);
        return alert == null ? ApiResponseDto<CitizenIncidentAlertDto>.NotFoundResult(CitizenIncidentAlertMessages.NotFound)
            : ApiResponseDto<CitizenIncidentAlertDto>.SuccessResult(Map(alert));
    }

    private static CitizenIncidentAlertDto Map(CitizenIncidentAlert alert)
    {
        var area = LocalizationHelper.IsEnglish ? alert.AreaEn : alert.AreaVi;
        return new() { Id = alert.Id, Area = area, Message = LocalizationHelper.Localize(CitizenIncidentAlertMessages.Body, area), CreatedAt = alert.CreatedAt };
    }

    private static NotificationDto NotificationDto(Notification n, bool english) => new()
    {
        Id = n.Id, Type = n.Type, Title = english ? n.TitleEn ?? n.Title : n.Title,
        Message = english ? n.MessageEn ?? n.Message : n.Message, DataJson = n.DataJson,
        IsRead = n.IsRead, ReadAt = n.ReadAt, CreatedAt = n.CreatedAt
    };
}
