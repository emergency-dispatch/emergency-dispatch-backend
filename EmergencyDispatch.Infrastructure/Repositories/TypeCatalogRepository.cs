using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Interfaces;
using EmergencyDispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EmergencyDispatch.Infrastructure.Repositories;

public class TypeCatalogRepository : ITypeCatalogRepository
{
    private readonly ApplicationDbContext _context;

    public TypeCatalogRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<RescueUnitTypeDefinition>> GetUnitTypesAsync(bool includeInactive, CancellationToken cancellationToken = default)
        => await _context.RescueUnitTypes
            .Where(t => includeInactive || t.IsActive)
            .OrderBy(t => t.DisplayOrder).ThenBy(t => t.NameVi)
            .ToListAsync(cancellationToken);

    public Task<RescueUnitTypeDefinition?> GetUnitTypeAsync(string code, CancellationToken cancellationToken = default)
        => _context.RescueUnitTypes.FirstOrDefaultAsync(t => t.Code == code, cancellationToken);

    public Task<RescueUnitTypeDefinition?> GetUnitTypeByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.RescueUnitTypes.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<bool> IsUnitTypeInUseAsync(string code, CancellationToken cancellationToken = default)
        => await _context.RescueUnits.IgnoreQueryFilters().AnyAsync(u => u.UnitType == code, cancellationToken)
           || await _context.FieldRequests.IgnoreQueryFilters().AnyAsync(r => r.RequestedUnitType == code, cancellationToken)
           || await _context.IncidentTypeUnitRules.AnyAsync(r => r.UnitTypeCode == code, cancellationToken);

    public async Task AddUnitTypeAsync(RescueUnitTypeDefinition type, CancellationToken cancellationToken = default)
        => await _context.RescueUnitTypes.AddAsync(type, cancellationToken);

    public void RemoveUnitType(RescueUnitTypeDefinition type) => _context.RescueUnitTypes.Remove(type);

    public async Task<IReadOnlyList<IncidentTypeDefinition>> GetIncidentTypesAsync(bool includeInactive, CancellationToken cancellationToken = default)
        => await _context.IncidentTypes
            .Include(t => t.UnitRules)
            .Where(t => includeInactive || t.IsActive)
            .OrderBy(t => t.DisplayOrder).ThenBy(t => t.NameVi)
            .ToListAsync(cancellationToken);

    public Task<IncidentTypeDefinition?> GetIncidentTypeAsync(string code, CancellationToken cancellationToken = default)
        => _context.IncidentTypes.Include(t => t.UnitRules).FirstOrDefaultAsync(t => t.Code == code, cancellationToken);

    public Task<IncidentTypeDefinition?> GetIncidentTypeByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _context.IncidentTypes.Include(t => t.UnitRules).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<bool> IsIncidentTypeInUseAsync(string code, CancellationToken cancellationToken = default)
        => await _context.Incidents.IgnoreQueryFilters()
            .AnyAsync(i => i.IncidentTypeCode == code || i.ReportedIncidentTypeCode == code, cancellationToken);

    public async Task AddIncidentTypeAsync(IncidentTypeDefinition type, CancellationToken cancellationToken = default)
        => await _context.IncidentTypes.AddAsync(type, cancellationToken);

    public void RemoveIncidentType(IncidentTypeDefinition type) => _context.IncidentTypes.Remove(type);

    public async Task<IReadOnlyList<IncidentTypeUnitRule>> GetUnitRulesAsync(string incidentTypeCode, CancellationToken cancellationToken = default)
        => await _context.IncidentTypeUnitRules
            .AsNoTracking()
            .Where(r => r.IncidentType!.Code == incidentTypeCode)
            .OrderBy(r => r.Severity).ThenBy(r => r.Priority)
            .ToListAsync(cancellationToken);

    public void RemoveUnitRules(IEnumerable<IncidentTypeUnitRule> rules) => _context.IncidentTypeUnitRules.RemoveRange(rules);

    public Task AddUnitRulesAsync(IEnumerable<IncidentTypeUnitRule> rules, CancellationToken cancellationToken = default)
        => _context.IncidentTypeUnitRules.AddRangeAsync(rules, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => _context.SaveChangesAsync(cancellationToken);
}
