using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using EmergencyDispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Repositories;

public sealed class CitizenIncidentAlertRepository(ApplicationDbContext db) : ICitizenIncidentAlertRepository
{
    public async Task<IReadOnlyList<Guid>> GetCandidateIdsAsync(DateTime since, int batchSize, CancellationToken ct = default)
        => await db.Incidents.AsNoTracking().Where(i => i.DuplicateOfIncidentId == null
            && (i.Status == IncidentStatus.Verified || i.Status == IncidentStatus.Dispatched
                || i.Status == IncidentStatus.InProgress || i.Status == IncidentStatus.Escalated)
            && i.Severity >= SeverityLevel.Level1 && i.Severity <= SeverityLevel.Level3 && i.EventType != IncidentEventType.None
            && !db.CitizenIncidentAlerts.Any(a => a.IncidentId == i.Id)
            && (i.VerifiedByUserId != null && i.VerifiedAt >= since
                || i.AutoDispatchState == AutoDispatchState.Dispatched && i.DispatchAssignments.Any(a => !a.IsDeleted
                    && a.AssignedByUserId == null && a.AcceptedAt >= since
                    && (a.Status == DispatchAssignmentStatus.Accepted || a.Status == DispatchAssignmentStatus.EnRoute || a.Status == DispatchAssignmentStatus.OnScene)
                    && a.StatusHistories.Any(h => !h.IsDeleted && h.NewStatus == DispatchAssignmentStatus.Accepted
                        && h.ChangedByUserId != null && h.ChangedByUser != null && h.ChangedByUser.Role == UserRole.RescueStaff))))
            .OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).Take(batchSize).Select(i => i.Id).ToListAsync(ct);

    public Task<Incident?> GetIncidentSnapshotAsync(Guid incidentId, CancellationToken ct = default)
        => db.Incidents.AsNoTracking().Where(i => i.Id == incidentId).Select(i => new Incident
        {
            Id = i.Id, Status = i.Status, Severity = i.Severity, EventType = i.EventType, IsDeleted = i.IsDeleted,
            DuplicateOfIncidentId = i.DuplicateOfIncidentId, LocationAddress = i.LocationAddress,
            Latitude = i.Latitude, Longitude = i.Longitude, AutoDispatchState = i.AutoDispatchState,
            VerifiedByUserId = i.VerifiedByUserId, VerifiedAt = i.VerifiedAt,
            DispatchAssignments = i.DispatchAssignments.Select(a => new DispatchAssignment
            {
                Id = a.Id, IsDeleted = a.IsDeleted, Status = a.Status, AssignedByUserId = a.AssignedByUserId, AcceptedAt = a.AcceptedAt,
                StatusHistories = a.StatusHistories.Select(h => new AssignmentStatusHistory
                {
                    Id = h.Id, IsDeleted = h.IsDeleted, NewStatus = h.NewStatus, ChangedByUserId = h.ChangedByUserId,
                    ChangedByUser = h.ChangedByUser == null ? null : new User { Id = h.ChangedByUser.Id, Role = h.ChangedByUser.Role }
                }).ToList()
            }).ToList()
        }).FirstOrDefaultAsync(ct);

    public Task<CitizenIncidentAlert?> GetByIncidentAsync(Guid incidentId, CancellationToken ct = default)
        => db.CitizenIncidentAlerts.AsNoTracking().FirstOrDefaultAsync(a => a.IncidentId == incidentId, ct);

    public async Task AddAsync(CitizenIncidentAlert alert, IReadOnlyList<Notification> notifications,
        IReadOnlyList<CitizenIncidentAlertDelivery> deliveries, CancellationToken ct = default)
    {
        await db.CitizenIncidentAlerts.AddAsync(alert, ct);
        await db.Notifications.AddRangeAsync(notifications, ct);
        await db.CitizenIncidentAlertDeliveries.AddRangeAsync(deliveries, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<CitizenIncidentAlertDelivery?> TryClaimAsync(DateTime now, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var next = await db.CitizenIncidentAlertDeliveries.FromSqlInterpolated($"""
            SELECT * FROM "CitizenIncidentAlertDeliveries"
            WHERE "CompletedAt" IS NULL AND "NextAttemptAt" <= {now}
              AND ("LeaseUntil" IS NULL OR "LeaseUntil" <= {now}) AND NOT "IsDeleted"
            ORDER BY "NextAttemptAt", "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
            """).FirstOrDefaultAsync(ct);
        if (next == null) return null;
        next.LeaseId = Guid.NewGuid();
        next.LeaseUntil = now.AddMinutes(2);
        next.Attempts++;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        db.Entry(next).State = EntityState.Detached;
        return next;
    }

    public Task<CitizenIncidentAlert?> GetAlertAsync(Guid id, CancellationToken ct = default)
        => db.CitizenIncidentAlerts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<Notification?> GetNotificationAsync(Guid id, CancellationToken ct = default)
        => db.Notifications.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, ct);

    public async Task FinishAttemptAsync(Guid id, Guid leaseId, DateTime now, bool terminal, bool realtimeAttempted,
        bool? pushSuccess, string? pushMessageId, string? error, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var delivery = await db.CitizenIncidentAlertDeliveries
            .FromSqlInterpolated($"SELECT * FROM \"CitizenIncidentAlertDeliveries\" WHERE \"Id\" = {id} FOR UPDATE")
            .FirstOrDefaultAsync(ct);
        if (delivery == null || delivery.LeaseId != leaseId) return;
        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == delivery.NotificationId, ct);
        if (notification != null)
        {
            notification.FcmSentSuccess = pushSuccess;
            notification.FcmMessageId = pushMessageId;
        }
        delivery.CompletedAt = terminal || delivery.Attempts >= 5 ? now : null;
        delivery.NextAttemptAt = now.AddSeconds(Math.Min(300, 10 * Math.Pow(2, Math.Min(delivery.Attempts - 1, 5))));
        if (realtimeAttempted) delivery.RealtimeAttemptedAt ??= now;
        delivery.LastError = string.IsNullOrWhiteSpace(error) ? null : error[..Math.Min(error.Length, 500)];
        delivery.LeaseId = null; delivery.LeaseUntil = null; delivery.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task ClearInvalidTokenAsync(Guid userId, string token, CancellationToken ct = default)
        => await db.Users.Where(u => u.Id == userId && u.FcmToken == token)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.FcmToken, (string?)null), ct);

    public async Task<(IReadOnlyList<CitizenIncidentAlert> Items, int Total)> GetForUserAsync(Guid userId, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        var query = db.CitizenIncidentAlerts.AsNoTracking().Where(a => db.CitizenIncidentAlertDeliveries.Any(d => d.AlertId == a.Id && d.UserId == userId));
        var total = await query.CountAsync(ct);
        return (await query.OrderByDescending(a => a.CreatedAt).ThenBy(a => a.Id).Skip((pageIndex - 1) * pageSize).Take(pageSize).ToListAsync(ct), total);
    }

    public Task<CitizenIncidentAlert?> GetForUserAsync(Guid alertId, Guid userId, CancellationToken ct = default)
        => db.CitizenIncidentAlerts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == alertId
            && db.CitizenIncidentAlertDeliveries.Any(d => d.AlertId == a.Id && d.UserId == userId), ct);
}
