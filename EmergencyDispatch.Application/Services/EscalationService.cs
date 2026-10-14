using EmergencyDispatch.Application.DTOs.Escalation;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Application.Services;

public class EscalationService : IEscalationService
{
    private readonly IEscalationRepository _escalationRepository;
    private readonly IRealtimeNotifier _realtimeNotifier;
    private readonly ILogger<EscalationService> _logger;

    public EscalationService(
        IEscalationRepository escalationRepository,
        IRealtimeNotifier realtimeNotifier,
        ILogger<EscalationService> logger)
    {
        _escalationRepository = escalationRepository;
        _realtimeNotifier = realtimeNotifier;
        _logger = logger;
    }

    public async Task<IReadOnlyList<EscalationRuleDto>> GetRulesAsync(CancellationToken cancellationToken = default)
    {
        var rules = await _escalationRepository.GetAllRulesAsync(cancellationToken);
        return rules.Select(MapToRuleDto).ToList();
    }

    public async Task<EscalationRuleDto?> GetRuleByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rule = await _escalationRepository.GetRuleByIdAsync(id, cancellationToken);
        return rule == null ? null : MapToRuleDto(rule);
    }

    public async Task<EscalationRuleDto> CreateRuleAsync(CreateEscalationRuleDto dto, CancellationToken cancellationToken = default)
    {
        var rule = new EscalationRule
        {
            Id = Guid.NewGuid(),
            MinSeverityLevel = dto.MinSeverityLevel,
            TimeoutMinutes = dto.TimeoutMinutes,
            NotifyRoles = string.IsNullOrWhiteSpace(dto.NotifyRoles) ? "[\"Admin\",\"Operator\"]" : dto.NotifyRoles,
            ExpandRadiusPercent = dto.ExpandRadiusPercent,
            IsActive = dto.IsActive,
            Description = dto.Description,
            CreatedAt = DateTime.UtcNow
        };

        await _escalationRepository.AddRuleAsync(rule, cancellationToken);
        await _escalationRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Đã tạo mới quy tắc leo thang {RuleId}: MinSeverity={Severity}, Timeout={Timeout}m",
            rule.Id, rule.MinSeverityLevel, rule.TimeoutMinutes);

        return MapToRuleDto(rule);
    }

    public async Task<EscalationRuleDto?> UpdateRuleAsync(Guid id, UpdateEscalationRuleDto dto, CancellationToken cancellationToken = default)
    {
        var rule = await _escalationRepository.GetRuleByIdAsync(id, cancellationToken);
        if (rule == null)
        {
            return null;
        }

        if (dto.MinSeverityLevel.HasValue)
        {
            rule.MinSeverityLevel = dto.MinSeverityLevel.Value;
        }

        if (dto.TimeoutMinutes.HasValue)
        {
            rule.TimeoutMinutes = dto.TimeoutMinutes.Value;
        }

        if (!string.IsNullOrWhiteSpace(dto.NotifyRoles))
        {
            rule.NotifyRoles = dto.NotifyRoles;
        }

        if (dto.ExpandRadiusPercent.HasValue)
        {
            rule.ExpandRadiusPercent = dto.ExpandRadiusPercent.Value;
        }

        if (dto.IsActive.HasValue)
        {
            rule.IsActive = dto.IsActive.Value;
        }

        if (dto.Description != null)
        {
            rule.Description = dto.Description;
        }

        rule.UpdatedAt = DateTime.UtcNow;
        _escalationRepository.UpdateRule(rule);
        await _escalationRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Đã cập nhật quy tắc leo thang {RuleId}", rule.Id);

        return MapToRuleDto(rule);
    }

    public async Task<bool> DeleteRuleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rule = await _escalationRepository.GetRuleByIdAsync(id, cancellationToken);
        if (rule == null)
        {
            return false;
        }

        _escalationRepository.DeleteRule(rule);
        await _escalationRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Đã xóa mềm quy tắc leo thang {RuleId}", rule.Id);
        return true;
    }

    public async Task<IReadOnlyList<EscalationLogDto>> GetLogsAsync(Guid? incidentId = null, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var logs = await _escalationRepository.GetLogsAsync(incidentId, page, pageSize, cancellationToken);
        return logs.Select(l => new EscalationLogDto
        {
            Id = l.Id,
            IncidentId = l.IncidentId,
            IncidentTitle = l.Incident?.Title,
            IncidentSeverity = l.Incident?.Severity ?? SeverityLevel.Unclassified,
            IncidentStatus = l.Incident?.Status ?? IncidentStatus.Pending,
            EscalationRuleId = l.EscalationRuleId,
            EscalationLevel = l.EscalationLevel,
            NotifiedRoles = l.NotifiedRoles,
            NotifiedUserIds = l.NotifiedUserIds,
            ActionTaken = l.ActionTaken,
            TriggerTime = l.TriggerTime,
            ResolvedAt = l.ResolvedAt,
            ResolutionTimeMs = l.ResolutionTimeMs
        }).ToList();
    }

    public async Task<int> ProcessEscalationsAsync(CancellationToken cancellationToken = default)
    {
        var rules = await _escalationRepository.GetActiveRulesAsync(cancellationToken);
        if (!rules.Any())
        {
            // Tự động khởi tạo quy tắc mặc định nếu chưa có
            var defaultRule = new EscalationRule
            {
                Id = Guid.NewGuid(),
                MinSeverityLevel = SeverityLevel.Level3,
                TimeoutMinutes = 3,
                NotifyRoles = "[\"Admin\",\"Operator\"]",
                ExpandRadiusPercent = 50,
                IsActive = true,
                Description = "Quy tắc mặc định: Tự động leo thang sự cố từ Level 3 trở lên nếu quá 3 phút chưa có đơn vị tiếp nhận",
                CreatedAt = DateTime.UtcNow
            };
            await _escalationRepository.AddRuleAsync(defaultRule, cancellationToken);
            await _escalationRepository.SaveChangesAsync(cancellationToken);
            rules = new List<EscalationRule> { defaultRule };
        }

        int totalEscalated = 0;

        foreach (var rule in rules)
        {
            var cutoffTime = DateTime.UtcNow.AddMinutes(-rule.TimeoutMinutes);
            var candidates = await _escalationRepository.GetEscalatableIncidentsAsync(rule.MinSeverityLevel, cutoffTime, cancellationToken);

            foreach (var incident in candidates)
            {
                // Kiểm tra xem sự cố này đã được kích hoạt log với quy tắc này gần đây chưa
                var hasRecentLog = await _escalationRepository.HasRecentEscalationLogAsync(
                    incident.Id, rule.Id, DateTime.UtcNow.AddMinutes(-rule.TimeoutMinutes), cancellationToken);

                if (hasRecentLog)
                {
                    continue;
                }

                var oldStatus = incident.Status;
                incident.Status = IncidentStatus.Escalated;
                incident.UpdatedAt = DateTime.UtcNow;

                // Lưu lịch sử trạng thái sự cố
                incident.StatusHistories.Add(new IncidentStatusHistory
                {
                    Id = Guid.NewGuid(),
                    IncidentId = incident.Id,
                    OldStatus = oldStatus,
                    NewStatus = IncidentStatus.Escalated,
                    ChangedByRole = "System (EscalationRuleEngine)",
                    Notes = $"Tự động leo thang: Sự cố {incident.Severity} chưa có đơn vị tiếp nhận sau {rule.TimeoutMinutes} phút. Mở rộng bán kính +{rule.ExpandRadiusPercent}%.",
                    ChangedAt = DateTime.UtcNow
                });

                // Lưu vết kiểm toán
                incident.AuditTrails.Add(new IncidentAuditTrail
                {
                    Id = Guid.NewGuid(),
                    IncidentId = incident.Id,
                    Action = AuditAction.Escalated,
                    ActorRole = "System",
                    Description = $"Kích hoạt leo thang tự động bởi quy tắc '{rule.Description ?? rule.Id.ToString()}'. Mở rộng bán kính điều phối +{rule.ExpandRadiusPercent}%.",
                    OldStateJson = $"{{\"status\":\"{oldStatus}\"}}",
                    NewStateJson = $"{{\"status\":\"{IncidentStatus.Escalated}\"}}",
                    Timestamp = DateTime.UtcNow
                });

                var escalationLog = new EscalationLog
                {
                    Id = Guid.NewGuid(),
                    IncidentId = incident.Id,
                    EscalationRuleId = rule.Id,
                    EscalationLevel = 1,
                    NotifiedRoles = rule.NotifyRoles,
                    ActionTaken = $"Phát cảnh báo khẩn cấp hệ thống & mở rộng bán kính điều phối +{rule.ExpandRadiusPercent}%",
                    TriggerTime = DateTime.UtcNow
                };

                await _escalationRepository.AddLogAsync(escalationLog, cancellationToken);
                await _escalationRepository.SaveChangesAsync(cancellationToken);

                var elapsedMinutes = Math.Round((DateTime.UtcNow - incident.CreatedAt).TotalMinutes, 1);

                _logger.LogWarning("🚨 [ESCALATION] Sự cố {IncidentId} ({Title}, Severity={Severity}) đã bị LEO THANG sau {Elapsed} phút chưa có đơn vị nhận!",
                    incident.Id, incident.Title, incident.Severity, elapsedMinutes);

                // Bắn sự kiện SignalR tới toàn bộ Operator và Admin
                await _realtimeNotifier.EscalationAlertAsync(new EscalationAlertDto
                {
                    IncidentId = incident.Id,
                    IncidentTitle = incident.Title,
                    Severity = incident.Severity,
                    Status = incident.Status,
                    LocationAddress = incident.LocationAddress,
                    Latitude = incident.Latitude,
                    Longitude = incident.Longitude,
                    RuleId = rule.Id,
                    TimeoutMinutes = rule.TimeoutMinutes,
                    ElapsedMinutes = elapsedMinutes,
                    ActionTaken = escalationLog.ActionTaken,
                    ExpandRadiusPercent = rule.ExpandRadiusPercent,
                    TriggeredAt = escalationLog.TriggerTime
                }, cancellationToken);

                totalEscalated++;
            }
        }

        return totalEscalated;
    }

    private static EscalationRuleDto MapToRuleDto(EscalationRule rule)
    {
        return new EscalationRuleDto
        {
            Id = rule.Id,
            MinSeverityLevel = rule.MinSeverityLevel,
            TimeoutMinutes = rule.TimeoutMinutes,
            NotifyRoles = rule.NotifyRoles,
            ExpandRadiusPercent = rule.ExpandRadiusPercent,
            IsActive = rule.IsActive,
            Description = rule.Description,
            CreatedAt = rule.CreatedAt,
            UpdatedAt = rule.UpdatedAt
        };
    }
}
