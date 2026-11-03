using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Interfaces;
using EmergencyDispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Repositories;

public class IncidentSupplementRepository : GenericRepository<IncidentSupplement>, IIncidentSupplementRepository
{
    public IncidentSupplementRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyList<IncidentSupplement>> GetByIncidentIdsAsync(IReadOnlyCollection<Guid> incidentIds, CancellationToken cancellationToken = default)
        => await _dbSet
            .Where(s => incidentIds.Contains(s.IncidentId))
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(cancellationToken);
}
