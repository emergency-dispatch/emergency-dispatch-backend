using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Settings;

namespace EmergencyDispatch.Application.Interfaces;

/// <summary>
/// Nguồn tham số vận hành dùng chung cho động cơ điều phối và Escalation Daemon (Admin chỉnh qua /api/settings)
/// </summary>
public interface ISystemSettingsService
{
    Task<DispatchSettingsDto> GetDispatchSettingsAsync(CancellationToken cancellationToken = default);
    Task<EscalationSettingsDto> GetEscalationSettingsAsync(CancellationToken cancellationToken = default);

    Task<SettingsRecordDto<DispatchSettingsDto>> GetDispatchRecordAsync(CancellationToken cancellationToken = default);
    Task<SettingsRecordDto<EscalationSettingsDto>> GetEscalationRecordAsync(CancellationToken cancellationToken = default);

    Task<ApiResponseDto<SettingsRecordDto<DispatchSettingsDto>>> UpdateDispatchAsync(DispatchSettingsDto values, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<ApiResponseDto<SettingsRecordDto<EscalationSettingsDto>>> UpdateEscalationAsync(EscalationSettingsDto values, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<ServiceAreaSettingsDto> GetServiceAreaAsync(CancellationToken cancellationToken = default);
    Task<SettingsRecordDto<ServiceAreaSettingsDto>> GetServiceAreaRecordAsync(CancellationToken cancellationToken = default);
    Task<ApiResponseDto<SettingsRecordDto<ServiceAreaSettingsDto>>> UpdateServiceAreaAsync(ServiceAreaSettingsDto values, Guid actorUserId, CancellationToken cancellationToken = default);
}
