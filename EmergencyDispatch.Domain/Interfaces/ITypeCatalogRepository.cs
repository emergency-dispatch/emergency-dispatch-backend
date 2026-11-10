using EmergencyDispatch.Domain.Entities;

namespace EmergencyDispatch.Domain.Interfaces;

/// <summary>
/// Danh mục loại xe, loại sự cố và quy tắc loại xe theo loại sự cố
/// </summary>
public interface ITypeCatalogRepository
{
    Task<IReadOnlyList<RescueUnitTypeDefinition>> GetUnitTypesAsync(bool includeInactive, CancellationToken cancellationToken = default);
    Task<RescueUnitTypeDefinition?> GetUnitTypeAsync(string code, CancellationToken cancellationToken = default);
    Task<RescueUnitTypeDefinition?> GetUnitTypeByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> IsUnitTypeInUseAsync(string code, CancellationToken cancellationToken = default);
    Task AddUnitTypeAsync(RescueUnitTypeDefinition type, CancellationToken cancellationToken = default);
    void RemoveUnitType(RescueUnitTypeDefinition type);

    Task<IReadOnlyList<IncidentTypeDefinition>> GetIncidentTypesAsync(bool includeInactive, CancellationToken cancellationToken = default);
    Task<IncidentTypeDefinition?> GetIncidentTypeAsync(string code, CancellationToken cancellationToken = default);
    Task<IncidentTypeDefinition?> GetIncidentTypeByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> IsIncidentTypeInUseAsync(string code, CancellationToken cancellationToken = default);
    Task AddIncidentTypeAsync(IncidentTypeDefinition type, CancellationToken cancellationToken = default);
    void RemoveIncidentType(IncidentTypeDefinition type);

    /// <summary>
    /// Quy tắc loại xe của một loại sự cố (kèm thứ tự ưu tiên)
    /// </summary>
    Task<IReadOnlyList<IncidentTypeUnitRule>> GetUnitRulesAsync(string incidentTypeCode, CancellationToken cancellationToken = default);
    void RemoveUnitRules(IEnumerable<IncidentTypeUnitRule> rules);
    Task AddUnitRulesAsync(IEnumerable<IncidentTypeUnitRule> rules, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
