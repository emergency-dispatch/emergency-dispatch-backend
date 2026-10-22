using System.Text.Json;
using EmergencyDispatch.Application.DTOs.Audit;
using EmergencyDispatch.Application.DTOs.Escalation;
using EmergencyDispatch.Application.DTOs.Notification;
using EmergencyDispatch.Application.DTOs.Settings;
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
    private readonly ISystemSettingsService _settingsService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<EscalationService> _logger;
    private readonly ISystemAuditService _auditService;
    private readonly IUserRepository _userRepository;

    private static readonly UserRole[] DefaultNotifyRoles = { UserRole.Operator, UserRole.Admin };
    private static readonly UserRole[] AllowedNotifyRoles = { UserRole.Admin, UserRole.Operator, UserRole.RescueStaff };

    public EscalationService(
        IEscalationRepository escalationRepository,
        IRealtimeNotifier realtimeNotifier,
        ISystemSettingsService settingsService,
        INotificationService notificationService,
        ILogger<EscalationService> logger,
        ISystemAuditService auditService,
        IUserRepository userRepository)
    {
        _userRepository = userRepository;
        _escalationRepository = escalationRepository;
        _realtimeNotifier = realtimeNotifier;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _logger = logger;
        _auditService = auditService;
    }

    public async Task<IReadOnlyList<EscalationRuleDto>> GetRulesAsync(CancellationToken cancellationToken = default)
    {
        var rules = await _escalationRepository.GetAllRulesAsync(cancellationToken);
        var settings = await _settingsService.GetEscalationSettingsAsync(cancellationToken);
        return rules.Select(r => MapToRuleDto(r, settings)).ToList();
    }

    public async Task<EscalationRuleDto?> GetRuleByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rule = await _escalationRepository.GetRuleByIdAsync(id, cancellationToken);
        return rule == null ? null : MapToRuleDto(rule, await _settingsService.GetEscalationSettingsAsync(cancellationToken));
    }

    public async Task<EscalationRuleDto> CreateRuleAsync(CreateEscalationRuleDto dto, CancellationToken cancellationToken = default)
    {
        var rule = new EscalationRule
        {
            Id = Guid.NewGuid(),
            MinSeverityLevel = dto.MinSeverityLevel,
            NotifyRoles = NormalizeNotifyRoles(dto.NotifyRoles),
            IsActive = dto.IsActive,
            Description = dto.Description,
            CreatedAt = DateTime.UtcNow
        };

        await _escalationRepository.AddRuleAsync(rule, cancellationToken);
        await _escalationRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Đã tạo mới quy tắc leo thang {RuleId}: MinSeverity={Severity}, NotifyRoles={Roles}",
            rule.Id, rule.MinSeverityLevel, rule.NotifyRoles);
        await _auditService.LogAsync(SystemAuditAction.Created, AuditEntityTypes.EscalationRule, rule.Id, rule.Description,
            $"Tạo quy tắc leo thang từ {rule.MinSeverityLevel}, thông báo {rule.NotifyRoles}", newValue: RuleSnapshot(rule), cancellationToken: cancellationToken);

        return MapToRuleDto(rule, await _settingsService.GetEscalationSettingsAsync(cancellationToken));
    }

    /// <summary>
    /// Kiểm tra và chuẩn hóa danh sách vai trò nhận cảnh báo về dạng JSON ["Admin","Operator"]
    /// </summary>
    public static string NormalizeNotifyRoles(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return JsonSerializer.Serialize(DefaultNotifyRoles.Select(r => r.ToString()));
        }

        List<string>? names;
        try
        {
            names = JsonSerializer.Deserialize<List<string>>(json);
        }
        catch (JsonException)
        {
            throw new ArgumentException("NotifyRoles phải là mảng JSON, VD: [\"Admin\",\"Operator\"].");
        }

        var roles = new List<UserRole>();
        foreach (var name in names ?? new List<string>())
        {
            if (!Enum.TryParse<UserRole>(name, true, out var role) || !AllowedNotifyRoles.Contains(role))
            {
                throw new ArgumentException($"Vai trò \"{name}\" không hợp lệ. Chỉ chấp nhận Admin, Operator, RescueStaff.");
            }
            if (!roles.Contains(role)) roles.Add(role);
        }
        if (roles.Count == 0)
        {
            throw new ArgumentException("Cần chọn ít nhất một vai trò nhận cảnh báo leo thang.");
        }

        return JsonSerializer.Serialize(roles.Select(r => r.ToString()));
    }

    private static UserRole[] ParseNotifyRoles(string? json)
    {
        try
        {
            var roles = (JsonSerializer.Deserialize<List<string>>(json ?? "[]") ?? new List<string>())
                .Select(n => Enum.TryParse<UserRole>(n, true, out var r) ? r : (UserRole?)null)
                .Where(r => r.HasValue && AllowedNotifyRoles.Contains(r.Value))
                .Select(r => r!.Value)
                .Distinct()
                .ToArray();
            return roles.Length > 0 ? roles : DefaultNotifyRoles;
        }
        catch (JsonException)
        {
            return DefaultNotifyRoles;
        }
    }

    private static object RuleSnapshot(EscalationRule r) => new
    {
        r.MinSeverityLevel,
        r.NotifyRoles,
        r.IsActive,
        r.Description
    };

    public async Task<EscalationRuleDto?> UpdateRuleAsync(Guid id, UpdateEscalationRuleDto dto, CancellationToken cancellationToken = default)
    {
        var rule = await _escalationRepository.GetRuleByIdAsync(id, cancellationToken);
        if (rule == null)
        {
            return null;
        }

        var before = RuleSnapshot(rule);
        if (dto.MinSeverityLevel.HasValue)
        {
            rule.MinSeverityLevel = dto.MinSeverityLevel.Value;
        }

        if (dto.NotifyRoles != null)
        {
            rule.NotifyRoles = NormalizeNotifyRoles(dto.NotifyRoles);
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
        await _auditService.LogAsync(SystemAuditAction.Updated, AuditEntityTypes.EscalationRule, rule.Id, rule.Description,
            "Cập nhật quy tắc leo thang", before, RuleSnapshot(rule), cancellationToken);

        return MapToRuleDto(rule, await _settingsService.GetEscalationSettingsAsync(cancellationToken));
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
        await _auditService.LogAsync(SystemAuditAction.Deleted, AuditEntityTypes.EscalationRule, rule.Id, rule.Description,
            "Xóa quy tắc leo thang", RuleSnapshot(rule), cancellationToken: cancellationToken);
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
                TimeoutMinutes = 5,
                NotifyRoles = "[\"Admin\",\"Operator\"]",
                ExpandRadiusPercent = 100,
                IsActive = true,
                Description = "Quy tắc mặc định: leo thang sự cố Level 3 và Level 0 chưa có đơn vị tiếp nhận quá ngưỡng cấu hình (ngày 5 phút / đêm 2.5 phút)",
                CreatedAt = DateTime.UtcNow
            };
            await _escalationRepository.AddRuleAsync(defaultRule, cancellationToken);
            await _escalationRepository.SaveChangesAsync(cancellationToken);
            rules = new List<EscalationRule> { defaultRule };
        }

        int totalEscalated = 0;

        // FR-SYS-02 / BR-19: ngưỡng chờ đọc từ Settings, ban đêm (22:00–06:00) rút ngắn
        var settings = await _settingsService.GetEscalationSettingsAsync(cancellationToken);
        var nowUtc = DateTime.UtcNow;
        var thresholdMinutes = EscalationTiming.GetThresholdMinutes(nowUtc, settings);
        var isNight = EscalationTiming.IsNightShift(nowUtc, settings.NightStart, settings.NightEnd);

        foreach (var rule in rules)
        {
            var cutoffTime = nowUtc.AddMinutes(-thresholdMinutes);
            var severities = EscalationTiming.SeveritiesInScope(rule.MinSeverityLevel);
            var candidates = await _escalationRepository.GetEscalatableIncidentsAsync(severities, cutoffTime, cancellationToken);

            foreach (var incident in candidates)
            {
                // Kiểm tra xem sự cố này đã được kích hoạt log với quy tắc này gần đây chưa
                var hasRecentLog = await _escalationRepository.HasRecentEscalationLogAsync(
                    incident.Id, rule.Id, nowUtc.AddMinutes(-thresholdMinutes), cancellationToken);

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
                    Notes = $"Tự động leo thang: Sự cố {incident.Severity} chưa có đơn vị tiếp nhận sau {thresholdMinutes} phút ({(isNight ? "ca đêm" : "ca ngày")}). Bán kính tìm xe ×{settings.RadiusMultiplier}.",
                    ChangedAt = DateTime.UtcNow
                });

                // Lưu vết kiểm toán
                incident.AuditTrails.Add(new IncidentAuditTrail
                {
                    Id = Guid.NewGuid(),
                    IncidentId = incident.Id,
                    Action = AuditAction.Escalated,
                    ActorRole = "System",
                    Description = $"Kích hoạt leo thang tự động bởi quy tắc '{rule.Description ?? rule.Id.ToString()}' (ngưỡng {thresholdMinutes} phút). Bán kính tìm xe ×{settings.RadiusMultiplier}, tối đa {settings.MaxRadiusKm} km.",
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
                    ActionTaken = $"Phát cảnh báo khẩn cấp hệ thống & mở rộng bán kính tìm xe ×{settings.RadiusMultiplier} (tối đa {settings.MaxRadiusKm} km)",
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
                    TimeoutMinutes = (int)Math.Ceiling(thresholdMinutes),
                    ElapsedMinutes = elapsedMinutes,
                    ActionTaken = escalationLog.ActionTaken,
                    ExpandRadiusPercent = rule.ExpandRadiusPercent,
                    TriggeredAt = escalationLog.TriggerTime,
                    ResponsibleStationId = incident.ResponsibleStationId,
                    Tier = 1
                }, cancellationToken);

                // FR-SYS-02 cấp 1: còi báo động tới vai trò trong quy tắc, thuộc trạm phụ trách (Admin luôn nhận)
                await _notificationService.NotifyUsersAsync(
                    await ResolveRecipientsAsync(ParseNotifyRoles(rule.NotifyRoles), incident.ResponsibleStationId),
                    new NotificationRequest
                    {
                        Type = NotificationType.Escalation,
                        Title = $"LEO THANG KHẨN CẤP {incident.Severity}",
                        Message = $"{incident.Title} — {incident.LocationAddress}: chưa có đơn vị tiếp nhận sau {elapsedMinutes} phút.",
                        IncidentId = incident.Id,
                        HighPriority = true
                    }, cancellationToken);

                totalEscalated++;
            }
        }

        // BR-17 cấp 2: quá gấp đôi ngưỡng vẫn chưa có đơn vị nhận → báo MỌI Operator (trạm khác hỗ trợ) và Admin
        var tier2Cutoff = nowUtc.AddMinutes(-2 * thresholdMinutes);
        foreach (var incident in await _escalationRepository.GetTier2CandidatesAsync(tier2Cutoff, cancellationToken))
        {
            var rule = rules[0];
            var log = new EscalationLog
            {
                Id = Guid.NewGuid(),
                IncidentId = incident.Id,
                EscalationRuleId = rule.Id,
                EscalationLevel = 2,
                NotifiedRoles = "[\"Operator\",\"Admin\"]",
                ActionTaken = $"Leo thang cấp 2: quá {2 * thresholdMinutes} phút chưa có đơn vị nhận, báo toàn bộ Operator các trạm để hỗ trợ",
                TriggerTime = DateTime.UtcNow
            };
            await _escalationRepository.AddLogAsync(log, cancellationToken);
            await _escalationRepository.SaveChangesAsync(cancellationToken);

            var elapsed = Math.Round((DateTime.UtcNow - incident.CreatedAt).TotalMinutes, 1);
            _logger.LogWarning("🚨🚨 [ESCALATION TIER 2] Sự cố {IncidentId} ({Severity}) vẫn chưa có đơn vị nhận sau {Elapsed} phút!",
                incident.Id, incident.Severity, elapsed);

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
                TimeoutMinutes = (int)Math.Ceiling(2 * thresholdMinutes),
                ElapsedMinutes = elapsed,
                ActionTaken = log.ActionTaken,
                TriggeredAt = log.TriggerTime,
                ResponsibleStationId = incident.ResponsibleStationId,
                Tier = 2
            }, cancellationToken);

            await _notificationService.NotifyRolesAsync(new[] { UserRole.Operator, UserRole.Admin }, new NotificationRequest
            {
                Type = NotificationType.Escalation,
                Title = $"LEO THANG CẤP 2 — CẦN TRẠM KHÁC HỖ TRỢ ({incident.Severity})",
                Message = $"{incident.Title} — {incident.LocationAddress}: vẫn chưa có đơn vị nhận sau {elapsed} phút.",
                IncidentId = incident.Id,
                HighPriority = true
            }, cancellationToken);

            totalEscalated++;
        }

        return totalEscalated;
    }

    /// <summary>
    /// Người nhận cảnh báo cấp 1: Admin + các vai trò trong quy tắc thuộc trạm phụ trách
    /// (sự cố chưa có trạm hoặc trạm chưa có Operator → toàn bộ vai trò đó)
    /// </summary>
    private async Task<List<Guid>> ResolveRecipientsAsync(IReadOnlyCollection<UserRole> roles, Guid? stationId)
    {
        var users = await _userRepository.GetActiveByRolesAsync(roles.Append(UserRole.Admin).Distinct().ToList());
        var scoped = users.Where(u => u.Role == UserRole.Admin || (stationId.HasValue && u.StationId == stationId)).ToList();
        var stationHasOperator = scoped.Any(u => u.Role == UserRole.Operator);
        return (stationHasOperator ? scoped : users).Select(u => u.Id).Distinct().ToList();
    }

    private static EscalationRuleDto MapToRuleDto(EscalationRule rule, EscalationSettingsDto settings)
    {
        return new EscalationRuleDto
        {
            Id = rule.Id,
            MinSeverityLevel = rule.MinSeverityLevel,
            NotifyRoles = rule.NotifyRoles,
            IsActive = rule.IsActive,
            Description = rule.Description,
            CreatedAt = rule.CreatedAt,
            UpdatedAt = rule.UpdatedAt,
            EffectiveDayThresholdMinutes = settings.DayThresholdMinutes,
            EffectiveNightThresholdMinutes = settings.NightThresholdMinutes,
            EffectiveNightStart = settings.NightStart,
            EffectiveNightEnd = settings.NightEnd,
            EffectiveRadiusMultiplier = settings.RadiusMultiplier,
            EffectiveMaxRadiusKm = settings.MaxRadiusKm
        };
    }
}
