using EmergencyDispatch.Domain.Models;

namespace EmergencyDispatch.Domain.Interfaces;

public interface ICitizenMapRepository
{
    Task<(IReadOnlyList<CitizenMapIncident> Items, int Total)> GetAsync(CitizenMapBounds? bounds, int pageIndex, int pageSize, CancellationToken ct = default);
    Task<CitizenMapIncident?> GetAsync(Guid id, CancellationToken ct = default);
    Task<CitizenMapIncident?> GetByAlertAsync(Guid publicAlertId, CancellationToken ct = default);
    Task<string> GetRevisionAsync(CancellationToken ct = default);
}
