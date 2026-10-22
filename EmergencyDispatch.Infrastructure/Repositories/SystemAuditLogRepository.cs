using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using EmergencyDispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Repositories;

public class SystemAuditLogRepository : ISystemAuditLogRepository
{
    private readonly ApplicationDbContext _context;

    public SystemAuditLogRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(SystemAuditLog log, CancellationToken cancellationToken = default)
    {
        await _context.SystemAuditLogs.AddAsync(log, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<SystemAuditLog> Items, int TotalCount)> GetPagedAsync(
        string? entityType,
        Guid? entityId,
        Guid? actorId,
        SystemAuditAction? action,
        DateTime? from,
        DateTime? to,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.SystemAuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(l => l.EntityType == entityType);
        }
        if (entityId.HasValue)
        {
            query = query.Where(l => l.EntityId == entityId);
        }
        if (actorId.HasValue)
        {
            query = query.Where(l => l.ActorId == actorId);
        }
        if (action.HasValue)
        {
            query = query.Where(l => l.Action == action);
        }
        if (from.HasValue)
        {
            query = query.Where(l => l.Timestamp >= from);
        }
        if (to.HasValue)
        {
            query = query.Where(l => l.Timestamp <= to);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(l => l.Timestamp)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }
}
