using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using EmergencyDispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Repositories;

public class FieldRequestRepository : GenericRepository<FieldRequest>, IFieldRequestRepository
{
    public FieldRequestRepository(ApplicationDbContext context) : base(context)
    {
    }

    private IQueryable<FieldRequest> WithDetails()
        => _dbSet
            .Include(r => r.Incident)
            .Include(r => r.Assignment)
            .Include(r => r.RescueUnit)
            .Include(r => r.RequestedByUser);

    public Task<FieldRequest?> GetWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
        => WithDetails().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<FieldRequest>> GetOpenAsync(Guid? responsibleStationId, CancellationToken cancellationToken = default)
        => await WithDetails()
            .Where(r => r.Status == FieldRequestStatus.Open || r.Status == FieldRequestStatus.Acknowledged)
            .Where(r => !responsibleStationId.HasValue || r.Incident!.ResponsibleStationId == null || r.Incident.ResponsibleStationId == responsibleStationId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FieldRequest>> GetByIncidentAsync(Guid incidentId, CancellationToken cancellationToken = default)
        => await WithDetails()
            .Where(r => r.IncidentId == incidentId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FieldRequest>> GetOverdueAsync(DateTime createdBeforeUtc, CancellationToken cancellationToken = default)
        => await WithDetails()
            .Where(r => (r.Status == FieldRequestStatus.Open || r.Status == FieldRequestStatus.Acknowledged)
                        && r.EscalatedAt == null
                        && r.CreatedAt <= createdBeforeUtc)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => _context.SaveChangesAsync(cancellationToken);
}
