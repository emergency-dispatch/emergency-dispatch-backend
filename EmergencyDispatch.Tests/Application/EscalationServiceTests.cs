using EmergencyDispatch.Application.DTOs.Escalation;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.DTOs.Notification;
using EmergencyDispatch.Application.DTOs.Settings;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EmergencyDispatch.Tests.Application;

public class EscalationServiceTests
{
    private readonly Mock<IEscalationRepository> _esc = new();
    private readonly Mock<IIncidentRepository> _incidents = new();
    private readonly Mock<IRealtimeNotifier> _rt = new();
    private readonly Mock<ISystemSettingsService> _settings = new();
    private readonly Mock<INotificationService> _notify = new();
    private readonly Mock<ISystemAuditService> _audit = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IIncidentLockScope> _lock = new();
    private readonly Mock<ITrafficHotspotRepository> _hotspots = new();
    private readonly EscalationService _svc;
    private readonly EscalationRule _rule = new() { MinSeverityLevel = SeverityLevel.Level3, NotifyRoles = "[\"Operator\"]", IsActive = true, Description = "r" };

    public EscalationServiceTests()
    {
        _settings.Setup(s => s.GetEscalationSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EscalationSettingsDto { DayThresholdMinutes = 5, NightThresholdMinutes = 5 });
        _esc.Setup(e => e.GetActiveRulesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<EscalationRule> { _rule });
        _esc.Setup(e => e.GetEscalatableIncidentsAsync(It.IsAny<IReadOnlyCollection<SeverityLevel>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Incident>());
        _esc.Setup(e => e.GetTier2CandidatesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<Incident>());
        _esc.Setup(e => e.IsStillEscalatableAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<SeverityLevel>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _incidents.Setup(i => i.BeginLockedTransactionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(_lock.Object);
        _users.Setup(u => u.GetActiveByRolesAsync(It.IsAny<IReadOnlyCollection<UserRole>>())).ReturnsAsync(new List<User>());
        _hotspots.Setup(h => h.GetEscalatableAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TrafficHotspot>());
        _svc = new EscalationService(_esc.Object, _incidents.Object, _rt.Object, _settings.Object, _notify.Object,
            NullLogger<EscalationService>.Instance, _audit.Object, _users.Object, _hotspots.Object);
    }

    private TrafficHotspot BlockedHotspot(Guid? station = null, bool markSucceeds = true)
    {
        var h = new TrafficHotspot
        {
            Title = "Cây đổ chắn đường D2", LocationAddress = "Đường D2", CongestionLevel = TrafficCongestionLevel.Blocked,
            Status = TrafficHotspotStatus.Active, ResponsibleStationId = station, CreatedAt = DateTime.UtcNow.AddMinutes(-6),
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };
        _hotspots.Setup(x => x.GetEscalatableAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TrafficHotspot> { h });
        _hotspots.Setup(x => x.TryMarkEscalatedAsync(h.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(markSucceeds);
        return h;
    }

    private Incident Candidate(Guid? station = null)
    {
        var inc = new Incident { Title = "Cháy", Severity = SeverityLevel.Level3, Status = IncidentStatus.Verified, ResponsibleStationId = station, CreatedAt = DateTime.UtcNow.AddMinutes(-6) };
        _esc.Setup(e => e.GetEscalatableIncidentsAsync(It.IsAny<IReadOnlyCollection<SeverityLevel>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Incident> { inc });
        return inc;
    }

    // ---------- NormalizeNotifyRoles ----------
    [Theory]
    [InlineData(null, "[\"Operator\",\"Admin\"]")]
    [InlineData("", "[\"Operator\",\"Admin\"]")]
    [InlineData("[\"admin\",\"ADMIN\",\"RescueStaff\"]", "[\"Admin\",\"RescueStaff\"]")]
    public void NormalizeNotifyRoles_DefaultsAndDeduplicates(string? input, string expected)
    {
        Assert.Equal(expected, EscalationService.NormalizeNotifyRoles(input));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[\"Citizen\"]")]
    [InlineData("[\"Hacker\"]")]
    [InlineData("[]")]
    public void NormalizeNotifyRoles_InvalidInput_Throws(string input)
    {
        Assert.Throws<ArgumentException>(() => EscalationService.NormalizeNotifyRoles(input));
    }

    // ---------- Rule CRUD ----------
    [Fact]
    public async Task CreateRule_NormalizesRolesSavesAndAudits()
    {
        EscalationRule? added = null;
        _esc.Setup(e => e.AddRuleAsync(It.IsAny<EscalationRule>(), It.IsAny<CancellationToken>())).Callback<EscalationRule, CancellationToken>((r, _) => added = r);

        var dto = await _svc.CreateRuleAsync(new CreateEscalationRuleDto { MinSeverityLevel = SeverityLevel.Level2, NotifyRoles = "[\"operator\"]", IsActive = true });

        Assert.Equal("[\"Operator\"]", added!.NotifyRoles);
        Assert.Equal(SeverityLevel.Level2, dto.MinSeverityLevel);
        Assert.Equal(5, dto.EffectiveDayThresholdMinutes);
        _esc.Verify(e => e.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------- ProcessEscalations ----------
    [Fact]
    public async Task Process_NoRules_CreatesDefaultLevel3Rule()
    {
        _esc.Setup(e => e.GetActiveRulesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<EscalationRule>());

        await _svc.ProcessEscalationsAsync();

        _esc.Verify(e => e.AddRuleAsync(It.Is<EscalationRule>(r => r.MinSeverityLevel == SeverityLevel.Level3 && r.IsActive), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Process_NoCandidates_ReturnsZeroAndSendsNothing()
    {
        Assert.Equal(0, await _svc.ProcessEscalationsAsync());
        _rt.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Process_QueriesUnclassifiedAndLevel3WithThresholdCutoff()
    {
        IReadOnlyCollection<SeverityLevel>? sev = null;
        DateTime cutoff = default;
        _esc.Setup(e => e.GetEscalatableIncidentsAsync(It.IsAny<IReadOnlyCollection<SeverityLevel>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<SeverityLevel>, DateTime, CancellationToken>((s, c, _) => { sev = s; cutoff = c; })
            .ReturnsAsync(new List<Incident>());

        await _svc.ProcessEscalationsAsync();

        Assert.Equal(new[] { SeverityLevel.Unclassified, SeverityLevel.Level3 }, sev);
        Assert.InRange((DateTime.UtcNow - cutoff).TotalMinutes, 4.9, 5.1);
    }

    [Fact]
    public async Task Process_Candidate_IsEscalatedLoggedCommittedAndBroadcast()
    {
        var inc = Candidate();

        var count = await _svc.ProcessEscalationsAsync();

        Assert.Equal(1, count);
        Assert.Equal(IncidentStatus.Escalated, inc.Status);
        Assert.Contains(inc.StatusHistories, h => h.OldStatus == IncidentStatus.Verified && h.NewStatus == IncidentStatus.Escalated);
        Assert.Contains(inc.AuditTrails, a => a.Action == AuditAction.Escalated);
        _esc.Verify(e => e.AddLogAsync(It.Is<EscalationLog>(l => l.IncidentId == inc.Id && l.EscalationLevel == 1), It.IsAny<CancellationToken>()), Times.Once);
        _lock.Verify(l => l.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        _rt.Verify(r => r.IncidentUpdatedAsync(It.Is<IncidentResponseDto>(d => d.Status == IncidentStatus.Escalated), It.IsAny<CancellationToken>()), Times.Once);
        _rt.Verify(r => r.EscalationAlertAsync(It.Is<EscalationAlertDto>(a => a.IncidentId == inc.Id && a.Tier == 1 && a.TimeoutMinutes == 5), It.IsAny<CancellationToken>()), Times.Once);
        _notify.Verify(n => n.NotifyUsersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.Is<NotificationRequest>(r => r.HighPriority && r.IncidentId == inc.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Process_IncidentHandledMeanwhile_IsSkipped()
    {
        var inc = Candidate();
        _esc.Setup(e => e.IsStillEscalatableAsync(inc.Id, It.IsAny<IReadOnlyCollection<SeverityLevel>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        Assert.Equal(0, await _svc.ProcessEscalationsAsync());
        Assert.Equal(IncidentStatus.Verified, inc.Status);
        _lock.Verify(l => l.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Process_RecentLogForSameRule_IsSkipped()
    {
        var inc = Candidate();
        _esc.Setup(e => e.HasRecentEscalationLogAsync(inc.Id, _rule.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Assert.Equal(0, await _svc.ProcessEscalationsAsync());
        _esc.Verify(e => e.AddLogAsync(It.IsAny<EscalationLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Process_RecipientsAreStationOperatorsPlusAdmins()
    {
        var station = Guid.NewGuid();
        Candidate(station);
        var admin = new User { Role = UserRole.Admin };
        var opHere = new User { Role = UserRole.Operator, StationId = station };
        var opElsewhere = new User { Role = UserRole.Operator, StationId = Guid.NewGuid() };
        _users.Setup(u => u.GetActiveByRolesAsync(It.IsAny<IReadOnlyCollection<UserRole>>())).ReturnsAsync(new List<User> { admin, opHere, opElsewhere });
        IReadOnlyCollection<Guid>? ids = null;
        _notify.Setup(n => n.NotifyUsersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<Guid>, NotificationRequest, CancellationToken>((i, _, _) => ids = i);

        await _svc.ProcessEscalationsAsync();

        Assert.Equal(new[] { admin.Id, opHere.Id }.OrderBy(x => x), ids!.OrderBy(x => x));
    }

    [Fact]
    public async Task Process_StationWithoutOperator_NotifiesAllOperators()
    {
        Candidate(Guid.NewGuid());
        var admin = new User { Role = UserRole.Admin };
        var opElsewhere = new User { Role = UserRole.Operator, StationId = Guid.NewGuid() };
        _users.Setup(u => u.GetActiveByRolesAsync(It.IsAny<IReadOnlyCollection<UserRole>>())).ReturnsAsync(new List<User> { admin, opElsewhere });
        IReadOnlyCollection<Guid>? ids = null;
        _notify.Setup(n => n.NotifyUsersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<Guid>, NotificationRequest, CancellationToken>((i, _, _) => ids = i);

        await _svc.ProcessEscalationsAsync();

        Assert.Contains(opElsewhere.Id, ids!);
    }

    [Fact]
    public async Task Process_Tier2Candidate_NotifiesAllOperatorsAndAdmins()
    {
        var inc = new Incident { Title = "Cháy", Severity = SeverityLevel.Level3, Status = IncidentStatus.Escalated, CreatedAt = DateTime.UtcNow.AddMinutes(-11) };
        _esc.Setup(e => e.GetTier2CandidatesAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<Incident> { inc });

        Assert.Equal(1, await _svc.ProcessEscalationsAsync());

        _esc.Verify(e => e.AddLogAsync(It.Is<EscalationLog>(l => l.EscalationLevel == 2 && l.IncidentId == inc.Id), It.IsAny<CancellationToken>()), Times.Once);
        _rt.Verify(r => r.EscalationAlertAsync(It.Is<EscalationAlertDto>(a => a.Tier == 2 && a.TimeoutMinutes == 10), It.IsAny<CancellationToken>()), Times.Once);
        _notify.Verify(n => n.NotifyRolesAsync(It.Is<IReadOnlyCollection<UserRole>>(r => r.Contains(UserRole.Operator) && r.Contains(UserRole.Admin)),
            It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------- Traffic hotspot escalation ----------
    [Fact]
    public async Task Process_QueriesHotspotsWithSameThresholdCutoff()
    {
        DateTime cutoff = default;
        _hotspots.Setup(h => h.GetEscalatableAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<DateTime, DateTime, CancellationToken>((c, _, _) => cutoff = c)
            .ReturnsAsync(new List<TrafficHotspot>());

        await _svc.ProcessEscalationsAsync();

        Assert.InRange((DateTime.UtcNow - cutoff).TotalMinutes, 4.9, 5.1);
    }

    [Fact]
    public async Task Process_BlockedHotspotPastThreshold_IsEscalatedAuditedAndBroadcast()
    {
        var h = BlockedHotspot(Guid.NewGuid());
        NotificationRequest? sent = null;
        _notify.Setup(n => n.NotifyUsersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<Guid>, NotificationRequest, CancellationToken>((_, r, _) => sent = r);

        Assert.Equal(1, await _svc.ProcessEscalationsAsync());

        _rt.Verify(r => r.TrafficHotspotEscalatedAsync(It.Is<EmergencyDispatch.Application.DTOs.Traffic.TrafficHotspotResponseDto>(d => d.Id == h.Id && d.EscalatedAt != null),
            It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.LogAsync(SystemAuditAction.TrafficEscalated, It.IsAny<string>(), h.Id, h.Title, It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(NotificationType.Escalation, sent!.Type);
        Assert.True(sent.HighPriority);
        Assert.Contains("Đường D2", sent.Message);
    }

    [Fact]
    public async Task Process_HotspotAssignedOrEscalatedMeanwhile_IsSkipped()
    {
        BlockedHotspot(markSucceeds: false);

        Assert.Equal(0, await _svc.ProcessEscalationsAsync());

        _rt.Verify(r => r.TrafficHotspotEscalatedAsync(It.IsAny<EmergencyDispatch.Application.DTOs.Traffic.TrafficHotspotResponseDto>(), It.IsAny<CancellationToken>()), Times.Never);
        _notify.Verify(n => n.NotifyUsersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Process_HotspotRecipientsAreStationOperatorsPlusAdmins()
    {
        var station = Guid.NewGuid();
        BlockedHotspot(station);
        var admin = new User { Role = UserRole.Admin };
        var opHere = new User { Role = UserRole.Operator, StationId = station };
        var opElsewhere = new User { Role = UserRole.Operator, StationId = Guid.NewGuid() };
        _users.Setup(u => u.GetActiveByRolesAsync(It.IsAny<IReadOnlyCollection<UserRole>>())).ReturnsAsync(new List<User> { admin, opHere, opElsewhere });
        IReadOnlyCollection<Guid>? ids = null;
        _notify.Setup(n => n.NotifyUsersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<Guid>, NotificationRequest, CancellationToken>((i, _, _) => ids = i);

        await _svc.ProcessEscalationsAsync();

        Assert.Equal(new[] { admin.Id, opHere.Id }.OrderBy(x => x), ids!.OrderBy(x => x));
    }
}
