using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using EmergencyDispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Repositories;

public class EscalationRepository : IEscalationRepository
{
    private readonly ApplicationDbContext _context;

    public EscalationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<EscalationRule>> GetActiveRulesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.EscalationRules
            .Where(r => r.IsActive)
            .OrderByDescending(r => r.MinSeverityLevel)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EscalationRule>> GetAllRulesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.EscalationRules
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<EscalationRule?> GetRuleByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.EscalationRules
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task AddRuleAsync(EscalationRule rule, CancellationToken cancellationToken = default)
    {
        await _context.EscalationRules.AddAsync(rule, cancellationToken);
    }

    public void UpdateRule(EscalationRule rule)
    {
        _context.EscalationRules.Update(rule);
    }

    public void DeleteRule(EscalationRule rule)
    {
        rule.IsDeleted = true;
        rule.UpdatedAt = DateTime.UtcNow;
        _context.EscalationRules.Update(rule);
    }

    public async Task<IReadOnlyList<Incident>> GetEscalatableIncidentsAsync(IReadOnlyCollection<SeverityLevel> severities, DateTime cutoffTime, CancellationToken cancellationToken = default)
    {
        // Sự cố thuộc các mức độ cần quét, tạo trước cutoffTime,
        // chưa có đơn vị cứu hộ nhận (Accepted/EnRoute/OnScene) và chưa hoàn thành/hủy/đã leo thang
        var nonEscalatedStatuses = new[]
        {
            IncidentStatus.Pending,
            IncidentStatus.AiProcessing,
            IncidentStatus.AiProcessed,
            IncidentStatus.Verified,
            IncidentStatus.Dispatched
        };

        return await _context.Incidents
            .Include(i => i.DispatchAssignments)
            .Where(i => !i.IsDeleted
                        && severities.Contains(i.Severity)
                        && i.CreatedAt <= cutoffTime
                        && nonEscalatedStatuses.Contains(i.Status)
                        // Nếu là Dispatched thì kiểm tra xem chưa có assignment nào được chấp nhận
                        && !i.DispatchAssignments.Any(da => da.Status == DispatchAssignmentStatus.Accepted
                                                         || da.Status == DispatchAssignmentStatus.EnRoute
                                                         || da.Status == DispatchAssignmentStatus.OnScene
                                                         || da.Status == DispatchAssignmentStatus.Completed))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasRecentEscalationLogAsync(Guid incidentId, Guid ruleId, DateTime since, CancellationToken cancellationToken = default)
    {
        return await _context.EscalationLogs
            .AnyAsync(l => l.IncidentId == incidentId
                        && l.EscalationRuleId == ruleId
                        && l.TriggerTime >= since, cancellationToken);
    }

    public async Task AddLogAsync(EscalationLog log, CancellationToken cancellationToken = default)
    {
        await _context.EscalationLogs.AddAsync(log, cancellationToken);
    }

    public async Task<IReadOnlyList<EscalationLog>> GetLogsAsync(Guid? incidentId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _context.EscalationLogs
            .Include(l => l.Incident)
            .AsQueryable();

        if (incidentId.HasValue && incidentId.Value != Guid.Empty)
        {
            query = query.Where(l => l.IncidentId == incidentId.Value);
        }

        return await query
            .OrderByDescending(l => l.TriggerTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
}
