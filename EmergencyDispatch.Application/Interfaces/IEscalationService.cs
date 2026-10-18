using EmergencyDispatch.Application.DTOs.Escalation;

namespace EmergencyDispatch.Application.Interfaces;

/// <summary>
/// Service quản lý cấu hình quy tắc và xử lý leo thang tự động cho sự cố
/// </summary>
public interface IEscalationService
{
    Task<IReadOnlyList<EscalationRuleDto>> GetRulesAsync(CancellationToken cancellationToken = default);
    Task<EscalationRuleDto?> GetRuleByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<EscalationRuleDto> CreateRuleAsync(CreateEscalationRuleDto dto, CancellationToken cancellationToken = default);
    Task<EscalationRuleDto?> UpdateRuleAsync(Guid id, UpdateEscalationRuleDto dto, CancellationToken cancellationToken = default);
    Task<bool> DeleteRuleAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EscalationLogDto>> GetLogsAsync(Guid? incidentId = null, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Quét các sự cố đáp ứng điều kiện leo thang của các quy tắc và thực hiện cảnh báo tự động
    /// </summary>
    Task<int> ProcessEscalationsAsync(CancellationToken cancellationToken = default);
}
