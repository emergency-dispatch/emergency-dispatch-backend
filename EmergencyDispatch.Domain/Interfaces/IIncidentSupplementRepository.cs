using EmergencyDispatch.Domain.Entities;

namespace EmergencyDispatch.Domain.Interfaces;

/// <summary>
/// Repository thông tin bổ sung của người báo
/// </summary>
public interface IIncidentSupplementRepository : IGenericRepository<IncidentSupplement>
{
    /// <summary>
    /// Thông tin bổ sung của các sự cố (theo thời gian tăng dần)
    /// </summary>
    Task<IReadOnlyList<IncidentSupplement>> GetByIncidentIdsAsync(IReadOnlyCollection<Guid> incidentIds, CancellationToken cancellationToken = default);
}
