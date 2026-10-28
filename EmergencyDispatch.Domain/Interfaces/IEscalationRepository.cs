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
    Task<IReadOnlyList<Incident>> GetEscalatableIncidentsAsync(IReadOnlyCollection<SeverityLevel> severities, DateTime cutoffTime, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra lại trên DB (cùng điều kiện với GetEscalatableIncidentsAsync) — gọi sau khi đã khóa sự cố,
    /// vì Operator có thể vừa điều xe / hủy kể từ lúc lấy danh sách
    /// </summary>
    Task<bool> IsStillEscalatableAsync(Guid incidentId, IReadOnlyCollection<SeverityLevel> severities, DateTime cutoffTime, CancellationToken cancellationToken = default);

    /// <summary>
    /// BR-17 cấp 2: sự cố đã leo thang cấp 1, tạo trước cutoffTime (gấp đôi ngưỡng) mà vẫn chưa có đơn vị nhận, chưa leo thang cấp 2
    /// </summary>
    /// <summary>
    /// Đánh dấu đã xử lý các lượt leo thang còn mở của sự cố (chưa SaveChanges)
    /// </summary>
    Task<int> ResolveOpenLogsAsync(Guid incidentId, DateTime resolvedAtUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Incident>> GetTier2CandidatesAsync(DateTime cutoffTime, CancellationToken cancellationToken = default);

    Task<bool> HasRecentEscalationLogAsync(Guid incidentId, Guid ruleId, DateTime since, CancellationToken cancellationToken = default);
    Task AddLogAsync(EscalationLog log, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EscalationLog>> GetLogsAsync(Guid? incidentId, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
