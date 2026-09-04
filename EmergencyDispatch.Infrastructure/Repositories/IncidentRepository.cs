using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using EmergencyDispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Repositories;

public class IncidentRepository : GenericRepository<Incident>, IIncidentRepository
{
    public IncidentRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<Incident?> GetIncidentWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Incidents
            .Include(i => i.ReportedByUser)
            .Include(i => i.VerifiedByUser)
            .Include(i => i.MediaItems)
            .Include(i => i.AiClassification)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
    }

    public async Task<(List<Incident> Items, int TotalCount)> GetFilteredIncidentsAsync(
        IncidentStatus? status,
        SeverityLevel? severity,
        string? searchTerm,
        DateTime? fromDate,
        DateTime? toDate,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Incidents
            .Include(i => i.ReportedByUser)
            .Include(i => i.VerifiedByUser)
            .Include(i => i.MediaItems)
            .Include(i => i.AiClassification)
            .AsNoTracking();

        if (status.HasValue)
        {
            query = query.Where(i => i.Status == status.Value);
        }

        if (severity.HasValue)
        {
            query = query.Where(i => i.Severity == severity.Value);
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLower();
            query = query.Where(i => i.Title.ToLower().Contains(term) ||
                                     i.Description.ToLower().Contains(term) ||
                                     i.LocationAddress.ToLower().Contains(term));
        }

        if (fromDate.HasValue)
        {
            query = query.Where(i => i.CreatedAt >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(i => i.CreatedAt <= toDate.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<List<Incident>> GetPendingQueueAsync(CancellationToken cancellationToken = default)
    {
        // Hàng đợi sự cố: lấy các sự cố Pending hoặc AiProcessed chưa được xác minh
        // Sắp xếp ưu tiên: Severity giảm dần (Level 5 -> Level 1), các vụ Unclassified (Severity 0) ưu tiên lên đầu để Operator thẩm tra khẩn cấp
        return await _context.Incidents
            .Include(i => i.ReportedByUser)
            .Include(i => i.MediaItems)
            .Include(i => i.AiClassification)
            .Where(i => i.Status == IncidentStatus.Pending || i.Status == IncidentStatus.AiProcessed)
            .OrderByDescending(i => i.Severity == SeverityLevel.Unclassified) // Unclassified lên đầu
            .ThenByDescending(i => i.Severity)                               // Sau đó giảm dần 5 -> 1
            .ThenBy(i => i.CreatedAt)                                        // Cùng mức thì ai đến trước xử lý trước (FIFO)
            .ToListAsync(cancellationToken);
    }
}
