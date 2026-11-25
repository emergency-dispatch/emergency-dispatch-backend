using EmergencyDispatch.Domain.Entities;

namespace EmergencyDispatch.Domain.Interfaces;

public interface ICitizenIncidentAlertRepository
{
    Task<IReadOnlyList<Guid>> GetCandidateIdsAsync(DateTime enabledSinceUtc, int batchSize, CancellationToken cancellationToken = default);
    Task<Incident?> GetIncidentSnapshotAsync(Guid incidentId, CancellationToken cancellationToken = default);
    Task<CitizenIncidentAlert?> GetByIncidentAsync(Guid incidentId, CancellationToken cancellationToken = default);
    Task AddAsync(CitizenIncidentAlert alert, IReadOnlyList<Notification> notifications, IReadOnlyList<CitizenIncidentAlertDelivery> deliveries, CancellationToken cancellationToken = default);
    Task<CitizenIncidentAlertDelivery?> TryClaimAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
    Task<CitizenIncidentAlert?> GetAlertAsync(Guid alertId, CancellationToken cancellationToken = default);
    Task<Notification?> GetNotificationAsync(Guid notificationId, CancellationToken cancellationToken = default);
    Task FinishAttemptAsync(Guid deliveryId, Guid leaseId, DateTime nowUtc, bool terminal, bool realtimeAttempted,
        bool? pushSuccess, string? pushMessageId, string? error, CancellationToken cancellationToken = default);
    Task ClearInvalidTokenAsync(Guid userId, string token, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<CitizenIncidentAlert> Items, int Total)> GetForUserAsync(Guid userId, int pageIndex, int pageSize, CancellationToken cancellationToken = default);
    Task<CitizenIncidentAlert?> GetForUserAsync(Guid alertId, Guid userId, CancellationToken cancellationToken = default);
}
