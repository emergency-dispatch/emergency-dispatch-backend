using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Interfaces;

/// <summary>
/// Repository quản lý cấu hình quy tắc và nhật ký leo thang sự cố
/// </summary>
public interface IEscalationRepository
{
    Task<IReadOnlyList<EscalationRule>> GetActiveRulesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EscalationRule>> GetAllRulesAsync(CancellationToken cancellationToken = default);
    Task<EscalationRule?> GetRuleByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddRuleAsync(EscalationRule rule, CancellationToken cancellationToken = default);
    void UpdateRule(EscalationRule rule);
    void DeleteRule(EscalationRule rule);

    /// <summary>
    /// Lấy các sự cố nghiêm trọng (>= minSeverity) được tạo trước cutoffTime chưa được tiếp nhận xử lý
    /// </summary>
    Task<IReadOnlyList<Incident>> GetEscalatableIncidentsAsync(SeverityLevel minSeverity, DateTime cutoffTime, CancellationToken cancellationToken = default);

    Task<bool> HasRecentEscalationLogAsync(Guid incidentId, Guid ruleId, DateTime since, CancellationToken cancellationToken = default);
    Task AddLogAsync(EscalationLog log, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EscalationLog>> GetLogsAsync(Guid? incidentId, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
