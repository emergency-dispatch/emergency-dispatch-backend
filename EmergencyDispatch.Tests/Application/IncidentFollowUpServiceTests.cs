using EmergencyDispatch.Application.DTOs.Ai;
using EmergencyDispatch.Application.DTOs.Common;
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

/// <summary>Severity change after verification (B), duplicate linking / unlinking (C), reporter supplements (D).</summary>
public class IncidentFollowUpServiceTests
{
    private const string Photo = "https://res.cloudinary.com/demo/image/upload/a.jpg";
    private readonly Mock<IIncidentRepository> _incidents = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IIncidentSupplementRepository> _supplements = new();
    private readonly Mock<IRealtimeNotifier> _rt = new();
    private readonly Mock<INotificationService> _notify = new();
    private readonly Mock<ICurrentUserContext> _me = new();
    private readonly Mock<ISystemSettingsService> _settings = new();
    private readonly Mock<IAiClassificationService> _ai = new();
    private readonly Mock<IAiAnalysisQueue> _queue = new();
    private readonly Mock<IGuestCancellationTokenService> _guest = new();
    private readonly List<IncidentAuditTrail> _audits = new();
    private readonly List<IncidentSupplement> _savedSupplements = new();
    private readonly Guid _station = Guid.NewGuid();
    private readonly Guid _opId = Guid.NewGuid();
    private readonly DuplicateDetectionSettingsDto _dup = new();
    private readonly IncidentFollowUpService _svc;

    public IncidentFollowUpServiceTests()
    {
        _incidents.Setup(i => i.AddAuditTrailAsync(It.IsAny<IncidentAuditTrail>(), It.IsAny<CancellationToken>()))
            .Callback<IncidentAuditTrail, CancellationToken>((a, _) => _audits.Add(a)).Returns(Task.CompletedTask);
        _incidents.Setup(i => i.GetLinkedReportsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<Incident>());
        _incidents.Setup(i => i.FindLinkCandidatesAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Incident>());
        _supplements.Setup(s => s.AddAsync(It.IsAny<IncidentSupplement>())).Callback<IncidentSupplement>(_savedSupplements.Add).ReturnsAsync((IncidentSupplement s) => s);
        _supplements.Setup(s => s.GetByIncidentIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) => _savedSupplements.Where(x => ids.Contains(x.IncidentId)).ToList());
        _settings.Setup(s => s.GetDuplicateDetectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_dup);
        _guest.Setup(g => g.Validate(It.IsAny<Guid>(), "good")).Returns(true);
        AsRole(UserRole.Operator, _station);
        _svc = new IncidentFollowUpService(_incidents.Object, _users.Object, _supplements.Object, _rt.Object, _notify.Object, _me.Object,
            _settings.Object, _ai.Object, _queue.Object, _guest.Object, TestHelpers.Config(("Cloudinary:CloudName", "demo")),
            NullLogger<IncidentFollowUpService>.Instance);
    }

    private void AsRole(UserRole? role, Guid? station = null)
    {
        _me.SetupGet(m => m.Role).Returns(role);
        _me.SetupGet(m => m.StationId).Returns(station);
    }

    private Incident Incident(IncidentStatus status = IncidentStatus.Verified, Action<Incident>? tweak = null)
    {
        var inc = new Incident
        {
            Title = "Cháy", Status = status, ResponsibleStationId = _station, Severity = SeverityLevel.Level2,
            EventType = IncidentEventType.FireExplosion, Latitude = 10.7335, Longitude = 106.7192,
            VerifiedAt = status is IncidentStatus.Pending or IncidentStatus.Linked ? null : DateTime.UtcNow.AddMinutes(-5),
            CreatedAt = DateTime.UtcNow.AddMinutes(-6)
        };
        tweak?.Invoke(inc);
        _incidents.Setup(i => i.GetIncidentWithDetailsAsync(inc.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inc);
        return inc;
    }

    private Incident LinkedReport(Incident parent, Action<Incident>? tweak = null)
        => Incident(IncidentStatus.Linked, r =>
        {
            r.DuplicateOfIncidentId = parent.Id;
            r.EventType = IncidentEventType.None;
            r.LinkedAt = DateTime.UtcNow.AddMinutes(-1);
            r.LinkDistanceMeters = 40;
            tweak?.Invoke(r);
        });

    private User Citizen()
    {
        var u = new User { Role = UserRole.Citizen, FullName = "Dân" };
        _users.Setup(x => x.GetByIdAsync(u.Id)).ReturnsAsync(u);
        return u;
    }

    private static ChangeSeverityDto Raise(string reason = "Cháy lan sang nhà bên") => new() { NewSeverity = SeverityLevel.Level3, Reason = reason };

    // =============== B. ChangeSeverity ===============
    [Fact]
    public async Task ChangeSeverity_Verified_ChangesAuditsAndPublishes()
    {
        var inc = Incident(IncidentStatus.Dispatched);

        var r = await _svc.ChangeSeverityAsync(inc.Id, Raise(), _opId);

        Assert.True(r.Success);
        Assert.Equal(SeverityLevel.Level3, inc.Severity);
        var audit = Assert.Single(_audits);
        Assert.Equal(AuditAction.SeverityChanged, audit.Action);
        Assert.Contains("Level2 → Level3", audit.Description);
        Assert.Contains("Cháy lan sang nhà bên", audit.Description);
        Assert.Contains("\"severity\":\"Level2\"", audit.OldStateJson);
        _rt.Verify(x => x.IncidentUpdatedAsync(It.IsAny<IncidentResponseDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangeSeverity_RaisedNotifiesActiveUnitStationsWithHighPriority()
    {
        var unitStation = Guid.NewGuid();
        var inc = Incident(IncidentStatus.InProgress, i => i.DispatchAssignments.Add(new DispatchAssignment
        {
            Status = DispatchAssignmentStatus.OnScene, RescueUnit = new RescueUnit { StationId = unitStation }
        }));

        await _svc.ChangeSeverityAsync(inc.Id, Raise(), _opId);

        _notify.Verify(n => n.NotifyStationStaffAsync(unitStation, It.Is<NotificationRequest>(q => q.HighPriority), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangeSeverity_LoweredWithManyUnits_WarnsInMessage()
    {
        var inc = Incident(IncidentStatus.Dispatched, i =>
        {
            i.Severity = SeverityLevel.Level3;
            for (var k = 0; k < 3; k++)
                i.DispatchAssignments.Add(new DispatchAssignment { Status = DispatchAssignmentStatus.EnRoute, RescueUnit = new RescueUnit { StationId = _station } });
        });

        var r = await _svc.ChangeSeverityAsync(inc.Id, new ChangeSeverityDto { NewSeverity = SeverityLevel.Level1, Reason = "Đã khống chế" }, _opId);

        Assert.True(r.Success);
        Assert.Contains("3", r.Message);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("   ")]
    public async Task ChangeSeverity_ShortReason_Fails(string reason)
    {
        var inc = Incident();
        Assert.False((await _svc.ChangeSeverityAsync(inc.Id, Raise(reason), _opId)).Success);
        Assert.Equal(SeverityLevel.Level2, inc.Severity);
    }

    [Fact]
    public async Task ChangeSeverity_ToUnclassified_Fails()
        => Assert.False((await _svc.ChangeSeverityAsync(Incident().Id, new ChangeSeverityDto { NewSeverity = SeverityLevel.Unclassified, Reason = "Không rõ" }, _opId)).Success);

    [Fact]
    public async Task ChangeSeverity_InvalidEventType_Fails()
        => Assert.False((await _svc.ChangeSeverityAsync(Incident().Id, new ChangeSeverityDto { NewSeverity = SeverityLevel.Level3, NewEventType = IncidentEventType.None, Reason = "Đổi loại" }, _opId)).Success);

    [Theory]
    [InlineData(IncidentStatus.Pending)]
    [InlineData(IncidentStatus.Completed)]
    [InlineData(IncidentStatus.Cancelled)]
    public async Task ChangeSeverity_NotVerifiedOrClosed_Fails(IncidentStatus status)
        => Assert.False((await _svc.ChangeSeverityAsync(Incident(status).Id, Raise(), _opId)).Success);

    [Fact]
    public async Task ChangeSeverity_OtherStation_IsForbidden()
    {
        var inc = Incident(tweak: i => i.ResponsibleStationId = Guid.NewGuid());
        Assert.Equal(ApiErrorCodes.Forbidden, (await _svc.ChangeSeverityAsync(inc.Id, Raise(), _opId)).ErrorCode);
    }

    [Fact]
    public async Task ChangeSeverity_LockedByOther_IsConflict()
    {
        var inc = Incident(tweak: i => { i.LockedByOperatorId = Guid.NewGuid(); i.LockExpiresAt = DateTime.UtcNow.AddMinutes(1); });
        Assert.Equal(ApiErrorCodes.Conflict, (await _svc.ChangeSeverityAsync(inc.Id, Raise(), _opId)).ErrorCode);
    }

    [Fact]
    public async Task ChangeSeverity_NoChange_Fails()
        => Assert.False((await _svc.ChangeSeverityAsync(Incident().Id, new ChangeSeverityDto { NewSeverity = SeverityLevel.Level2, Reason = "Giữ nguyên" }, _opId)).Success);

    [Fact]
    public async Task ChangeSeverity_TypeOnly_IsAllowed()
    {
        var inc = Incident();
        var r = await _svc.ChangeSeverityAsync(inc.Id, new ChangeSeverityDto { NewSeverity = SeverityLevel.Level2, NewEventType = IncidentEventType.TrafficAccident, Reason = "Thực tế là tai nạn" }, _opId);
        Assert.True(r.Success);
        Assert.Equal(IncidentEventType.TrafficAccident, inc.EventType);
    }

    [Fact]
    public async Task ChangeSeverity_NotFound()
        => Assert.Equal(ApiErrorCodes.NotFound, (await _svc.ChangeSeverityAsync(Guid.NewGuid(), Raise(), _opId)).ErrorCode);

    // =============== C. Find / link ===============
    [Fact]
    public async Task FindLinkParent_Disabled_ReturnsNull()
    {
        _dup.Enabled = false;
        Assert.Null(await _svc.FindLinkParentAsync(new Incident { Latitude = 10.7335, Longitude = 106.7192 }));
        _incidents.Verify(i => i.FindLinkCandidatesAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FindLinkParent_PicksNearbyCandidateWithinWindow()
    {
        var parent = new Incident { Latitude = 10.7335, Longitude = 106.7192, Status = IncidentStatus.Verified, EventType = IncidentEventType.FireExplosion, CreatedAt = DateTime.UtcNow.AddMinutes(-2) };
        _incidents.Setup(i => i.FindLinkCandidatesAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Incident> { parent });

        var r = await _svc.FindLinkParentAsync(new Incident { Latitude = 10.7338, Longitude = 106.7192, Description = "khói" });

        Assert.Same(parent, r!.Value.Parent);
        _incidents.Verify(i => i.FindLinkCandidatesAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(),
            It.Is<DateTime>(d => Math.Abs((DateTime.UtcNow.AddMinutes(-15) - d).TotalSeconds) < 5), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task OnReportLinked_IncrementsCounterAuditsParentAndReturnsPlates()
    {
        var parent = Incident(IncidentStatus.Dispatched, i => i.DispatchAssignments.Add(new DispatchAssignment
        {
            Status = DispatchAssignmentStatus.EnRoute, RescueUnit = new RescueUnit { PlateNumber = "51B-001" }
        }));
        var report = new Incident { CreatedAt = DateTime.UtcNow };

        var dto = await _svc.OnReportLinkedAsync(report, parent, 42.4);

        _incidents.Verify(i => i.AdjustLinkedReportCountAsync(parent.Id, 1, report.CreatedAt, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains(_audits, a => a.IncidentId == parent.Id && a.Action == AuditAction.ReportLinked);
        Assert.Equal(parent.Id, dto.IncidentId);
        Assert.Equal(42, dto.DistanceMeters);
        Assert.Equal(new[] { "51B-001" }, dto.AssignedUnitPlates);
    }

    // =============== C. Quick check ===============
    [Fact]
    public async Task QuickCheck_ConflictingType_UnlinksAndQueuesFullAi()
    {
        var parent = Incident();
        var report = LinkedReport(parent, r => r.MediaItems.Add(new IncidentMedia { MediaUrl = Photo, MediaType = MediaType.Photo }));
        _ai.Setup(a => a.QuickCheckEventTypeAsync(Photo, It.IsAny<CancellationToken>())).ReturnsAsync(IncidentEventType.TrafficAccident);

        await _svc.HandleQuickCheckAsync(new AiAnalysisJob(report.Id, Photo, null, DateTime.UtcNow, QuickCheckOnly: true));

        Assert.Equal(IncidentStatus.AiProcessing, report.Status);
        Assert.Null(report.DuplicateOfIncidentId);
        _incidents.Verify(i => i.AdjustLinkedReportCountAsync(parent.Id, -1, null, It.IsAny<CancellationToken>()), Times.Once);
        _queue.Verify(q => q.EnqueueAsync(It.Is<AiAnalysisJob>(j => j.IncidentId == report.Id && !j.QuickCheckOnly), It.IsAny<CancellationToken>()), Times.Once);
        _rt.Verify(x => x.IncidentCreatedAsync(It.Is<IncidentResponseDto>(d => d.Id == report.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QuickCheck_SameType_KeepsLinkAndStoresResult()
    {
        var parent = Incident();
        var report = LinkedReport(parent);
        _ai.Setup(a => a.QuickCheckEventTypeAsync(Photo, It.IsAny<CancellationToken>())).ReturnsAsync(IncidentEventType.FireExplosion);

        await _svc.HandleQuickCheckAsync(new AiAnalysisJob(report.Id, Photo, null, DateTime.UtcNow, QuickCheckOnly: true));

        Assert.Equal(IncidentStatus.Linked, report.Status);
        Assert.Equal(IncidentEventType.FireExplosion, report.QuickCheckEventType);
    }

    [Fact]
    public async Task QuickCheck_YoloUnavailable_KeepsLink()
    {
        var report = LinkedReport(Incident());
        await _svc.HandleQuickCheckAsync(new AiAnalysisJob(report.Id, Photo, null, DateTime.UtcNow, QuickCheckOnly: true));
        Assert.Equal(IncidentStatus.Linked, report.Status);
    }

    [Fact]
    public async Task QuickCheck_ReportAlreadyUnlinked_IsIgnored()
    {
        var report = Incident(IncidentStatus.Pending);
        _ai.Setup(a => a.QuickCheckEventTypeAsync(Photo, It.IsAny<CancellationToken>())).ReturnsAsync(IncidentEventType.TrafficAccident);
        await _svc.HandleQuickCheckAsync(new AiAnalysisJob(report.Id, Photo, null, DateTime.UtcNow, QuickCheckOnly: true));
        Assert.Null(report.QuickCheckEventType);
    }

    // =============== C. Linked reports / unlink / confirm ===============
    [Fact]
    public async Task GetLinkedReports_FlagsOutliersAndMasksPhone()
    {
        var parent = Incident();
        var close = LinkedReport(parent, r => r.ReporterPhone = "0901234567");
        var edge = LinkedReport(parent, r => r.LinkDistanceMeters = 180);
        _incidents.Setup(i => i.GetLinkedReportsAsync(parent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Incident> { close, edge });

        var list = (await _svc.GetLinkedReportsAsync(parent.Id)).Data!;

        Assert.False(list.Single(x => x.IncidentId == close.Id).IsOutlier);
        Assert.True(list.Single(x => x.IncidentId == edge.Id).IsOutlier);
        Assert.DoesNotContain("1234567", list.Single(x => x.IncidentId == close.Id).MaskedReporterPhone);
    }

    [Fact]
    public async Task GetLinkedReports_OtherStation_IsForbidden()
    {
        var parent = Incident(tweak: i => i.ResponsibleStationId = Guid.NewGuid());
        Assert.Equal(ApiErrorCodes.Forbidden, (await _svc.GetLinkedReportsAsync(parent.Id)).ErrorCode);
    }

    [Fact]
    public async Task Unlink_ByOperator_ReturnsToPendingKeepingCreatedAt()
    {
        var parent = Incident();
        var report = LinkedReport(parent);
        var created = report.CreatedAt;

        var r = await _svc.UnlinkAsync(report.Id, new UnlinkIncidentDto(), _opId);

        Assert.True(r.Success);
        Assert.Equal(IncidentStatus.Pending, report.Status);
        Assert.Equal(created, report.CreatedAt);
        Assert.Null(report.DuplicateOfIncidentId);
        Assert.Contains(_audits, a => a.IncidentId == report.Id && a.Action == AuditAction.ReportUnlinked);
        Assert.Contains(_audits, a => a.IncidentId == parent.Id && a.Action == AuditAction.ReportUnlinked);
        _queue.Verify(q => q.EnqueueAsync(It.IsAny<AiAnalysisJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unlink_GuestWithValidToken_Succeeds_InvalidToken_Forbidden()
    {
        AsRole(null);
        var report = LinkedReport(Incident());

        Assert.Equal(ApiErrorCodes.Forbidden, (await _svc.UnlinkAsync(report.Id, new UnlinkIncidentDto { GuestCancellationToken = "bad" }, null)).ErrorCode);
        Assert.True((await _svc.UnlinkAsync(report.Id, new UnlinkIncidentDto { GuestCancellationToken = "good" }, null)).Success);
        Assert.Contains(_audits, a => a.ActorRole == "Guest");
    }

    [Fact]
    public async Task Unlink_GuestTokenCannotActOnAccountReport()
    {
        AsRole(null);
        var report = LinkedReport(Incident(), r => r.ReportedByUserId = Guid.NewGuid());
        Assert.Equal(ApiErrorCodes.Forbidden, (await _svc.UnlinkAsync(report.Id, new UnlinkIncidentDto { GuestCancellationToken = "good" }, null)).ErrorCode);
    }

    [Fact]
    public async Task Unlink_ByOwningCitizen_Succeeds_OtherCitizenForbidden()
    {
        AsRole(UserRole.Citizen);
        var owner = Citizen();
        var stranger = Citizen();
        var report = LinkedReport(Incident(), r => r.ReportedByUserId = owner.Id);

        Assert.Equal(ApiErrorCodes.Forbidden, (await _svc.UnlinkAsync(report.Id, new UnlinkIncidentDto(), stranger.Id)).ErrorCode);
        Assert.True((await _svc.UnlinkAsync(report.Id, new UnlinkIncidentDto { Reason = "Tôi ở tòa nhà khác" }, owner.Id)).Success);
    }

    [Fact]
    public async Task Unlink_NotLinked_Fails()
        => Assert.False((await _svc.UnlinkAsync(Incident(IncidentStatus.Pending).Id, new UnlinkIncidentDto(), _opId)).Success);

    [Fact]
    public async Task ConfirmDuplicate_ClosesReportAsDuplicateAndDecrements()
    {
        var parent = Incident();
        var report = LinkedReport(parent);

        var r = await _svc.ConfirmDuplicateAsync(report.Id, null, _opId);

        Assert.True(r.Success);
        Assert.Equal(IncidentStatus.Cancelled, report.Status);
        Assert.Equal(CancellationReason.Duplicate, report.CancellationReason);
        Assert.Equal(parent.Id, report.DuplicateOfIncidentId);
        Assert.Contains(_audits, a => a.Action == AuditAction.DuplicateConfirmed);
        _incidents.Verify(i => i.AdjustLinkedReportCountAsync(parent.Id, -1, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmDuplicate_LongNote_IsTruncatedTo500()
    {
        var report = LinkedReport(Incident());
        await _svc.ConfirmDuplicateAsync(report.Id, new string('x', 800), _opId);
        Assert.Equal(500, report.CancellationNote!.Length);
    }

    [Fact]
    public async Task RestoreLinkedReports_UnlinksAllAndDecrementsOnce()
    {
        var parent = Incident();
        var a = LinkedReport(parent);
        var b = LinkedReport(parent, r => r.MediaItems.Add(new IncidentMedia { MediaUrl = Photo, MediaType = MediaType.Photo }));
        _incidents.Setup(i => i.GetLinkedReportsAsync(parent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Incident> { a, b });

        Assert.Equal(2, await _svc.RestoreLinkedReportsAsync(parent.Id, "Sự cố gốc bị hủy"));

        Assert.Equal(IncidentStatus.Pending, a.Status);
        Assert.Equal(IncidentStatus.AiProcessing, b.Status);
        Assert.Contains(_audits, x => x.Action == AuditAction.LinkedReportsRestored);
        _incidents.Verify(i => i.AdjustLinkedReportCountAsync(parent.Id, -2, null, It.IsAny<CancellationToken>()), Times.Once);
        _incidents.Verify(i => i.AdjustLinkedReportCountAsync(parent.Id, -1, null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CloseLinkedReports_ClosesAsDuplicate()
    {
        var parent = Incident(IncidentStatus.Completed);
        var a = LinkedReport(parent);
        _incidents.Setup(i => i.GetLinkedReportsAsync(parent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Incident> { a });

        Assert.Equal(1, await _svc.CloseLinkedReportsAsync(parent.Id, null));

        Assert.Equal(CancellationReason.Duplicate, a.CancellationReason);
        _incidents.Verify(i => i.AdjustLinkedReportCountAsync(parent.Id, -1, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // =============== D. Supplements ===============
    [Fact]
    public async Task AddSupplement_LinkedReport_GoesToParentAndNotifiesUnits()
    {
        AsRole(UserRole.Citizen);
        var owner = Citizen();
        var unitStation = Guid.NewGuid();
        var parent = Incident(IncidentStatus.Dispatched, i => i.DispatchAssignments.Add(new DispatchAssignment
        {
            Status = DispatchAssignmentStatus.EnRoute, RescueUnit = new RescueUnit { StationId = unitStation }
        }));
        var report = LinkedReport(parent, r => r.ReportedByUserId = owner.Id);

        var r = await _svc.AddSupplementAsync(report.Id, new AddSupplementDto { Note = " Có người kẹt tầng 2 ", MediaUrls = new() { Photo } }, owner.Id);

        Assert.True(r.Success);
        Assert.Equal(report.Id, r.Data!.IncidentId);
        Assert.Equal(parent.Id, r.Data.TargetIncidentId);
        Assert.Equal("Có người kẹt tầng 2", Assert.Single(_savedSupplements).Note);
        Assert.Contains(_audits, a => a.IncidentId == parent.Id && a.Action == AuditAction.SupplementAdded);
        _notify.Verify(n => n.NotifyStationStaffAsync(unitStation, It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _rt.Verify(x => x.IncidentSupplementAddedAsync(It.IsAny<IncidentSupplementDto>(), parent.ResponsibleStationId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddSupplement_GuestWithToken_Succeeds()
    {
        AsRole(null);
        var inc = Incident(IncidentStatus.Pending);
        Assert.True((await _svc.AddSupplementAsync(inc.Id, new AddSupplementDto { Note = "Thêm chi tiết", GuestCancellationToken = "good" }, null)).Success);
    }

    [Fact]
    public async Task AddSupplement_NotReporter_IsForbidden()
    {
        var inc = Incident(IncidentStatus.Pending, i => i.ReportedByUserId = Guid.NewGuid());
        Assert.Equal(ApiErrorCodes.Forbidden, (await _svc.AddSupplementAsync(inc.Id, new AddSupplementDto { Note = "Thêm" }, Citizen().Id)).ErrorCode);
    }

    [Fact]
    public async Task AddSupplement_EmptyOrTooLong_Fails()
    {
        var inc = Incident(IncidentStatus.Pending);
        Assert.False((await _svc.AddSupplementAsync(inc.Id, new AddSupplementDto { Note = "  " }, null)).Success);
        Assert.False((await _svc.AddSupplementAsync(inc.Id, new AddSupplementDto { Note = new string('x', 1001), GuestCancellationToken = "good" }, null)).Success);
    }

    [Fact]
    public async Task AddSupplement_UntrustedOrTooManyMedia_Fails()
    {
        var inc = Incident(IncidentStatus.Pending);
        Assert.False((await _svc.AddSupplementAsync(inc.Id, new AddSupplementDto { MediaUrls = new() { "https://evil.example/a.jpg" }, GuestCancellationToken = "good" }, null)).Success);
        Assert.False((await _svc.AddSupplementAsync(inc.Id, new AddSupplementDto { MediaUrls = Enumerable.Repeat(Photo, 6).ToList(), GuestCancellationToken = "good" }, null)).Success);
    }

    [Theory]
    [InlineData(IncidentStatus.Completed)]
    [InlineData(IncidentStatus.Cancelled)]
    public async Task AddSupplement_ClosedIncident_Fails(IncidentStatus status)
    {
        AsRole(null);
        var inc = Incident(status);
        Assert.False((await _svc.AddSupplementAsync(inc.Id, new AddSupplementDto { Note = "Thêm", GuestCancellationToken = "good" }, null)).Success);
    }

    [Fact]
    public async Task AddSupplement_LimitReached_IsTooManyRequests()
    {
        AsRole(null);
        var inc = Incident(IncidentStatus.Pending);
        for (var k = 0; k < IncidentFollowUpService.MaxSupplementsPerIncident; k++)
            _savedSupplements.Add(new IncidentSupplement { IncidentId = inc.Id, Note = "x" });

        Assert.Equal(ApiErrorCodes.TooManyRequests, (await _svc.AddSupplementAsync(inc.Id, new AddSupplementDto { Note = "Thêm", GuestCancellationToken = "good" }, null)).ErrorCode);
    }

    [Fact]
    public async Task GetSupplements_IncludesLinkedReports_ForOperator()
    {
        var op = new User { Role = UserRole.Operator, StationId = _station };
        _users.Setup(x => x.GetByIdAsync(op.Id)).ReturnsAsync(op);
        var parent = Incident();
        var report = LinkedReport(parent, r => r.ReporterName = "Lan");
        _incidents.Setup(i => i.GetLinkedReportsAsync(parent.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Incident> { report });
        _savedSupplements.Add(new IncidentSupplement { IncidentId = report.Id, Note = "Từ báo cáo gom" });
        _savedSupplements.Add(new IncidentSupplement { IncidentId = parent.Id, Note = "Từ gốc" });

        var list = (await _svc.GetSupplementsAsync(parent.Id, op.Id)).Data!;

        Assert.Equal(2, list.Count);
        Assert.All(list, s => Assert.Equal(parent.Id, s.TargetIncidentId));
        Assert.Equal("Lan", list.Single(s => s.IncidentId == report.Id).ReporterName);
    }

    [Fact]
    public async Task GetSupplements_Anonymous_IsForbidden()
        => Assert.Equal(ApiErrorCodes.Forbidden, (await _svc.GetSupplementsAsync(Incident().Id, null)).ErrorCode);
}
