using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Interfaces;

public interface IIncidentRepository : IGenericRepository<Incident>
{
    Task<Incident?> GetIncidentWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<(List<Incident> Items, int TotalCount)> GetFilteredIncidentsAsync(
        IncidentStatus? status,
        SeverityLevel? severity,
        string? searchTerm,
        DateTime? fromDate,
        DateTime? toDate,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task<List<Incident>> GetPendingQueueAsync(CancellationToken cancellationToken = default);
}
