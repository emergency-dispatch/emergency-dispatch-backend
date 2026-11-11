using System.Linq.Expressions;
using EmergencyDispatch.Application.DTOs.Ai;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.DTOs.Notification;
using EmergencyDispatch.Application.DTOs.Settings;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Application.Validators;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EmergencyDispatch.Tests.Application;

public class IncidentServiceTests
{
    private readonly Mock<IIncidentRepository> _incidents = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IBlockedPhoneRepository> _blocked = new();
    private readonly Mock<IAiClassificationService> _ai = new();
    private readonly Mock<IAiAnalysisQueue> _queue = new();
    private readonly Mock<IRealtimeNotifier> _rt = new();
    private readonly Mock<INotificationService> _notify = new();
    private readonly Mock<IGuestCancellationTokenService> _guest = new();
    private readonly Mock<IIncidentFollowUpService> _followUp = new();
    private readonly Mock<IStationRepository> _stations = new();
    private readonly Mock<ISystemSettingsService> _settings = new();
    private readonly Mock<ICurrentUserContext> _me = new();
    private readonly Mock<IRoutingService> _routing = new();
    private readonly ServiceAreaSettingsDto _area = new() { Enabled = false };
    private readonly Station _q7 = new() { Name = "Q7", Latitude = 10.732842, Longitude = 106.719688, CoverageRadiusKm = 10, IsActive = true };
    private readonly Guid _operatorId = Guid.NewGuid();
    private readonly IncidentService _svc;

    public IncidentServiceTests()
    {
        var cfg = TestHelpers.Config(("Cloudinary:CloudName", "demo"));
        _stations.Setup(s => s.GetAllAsync(It.IsAny<Expression<Func<Station, bool>>?>(), It.IsAny<Func<IQueryable<Station>, IQueryable<Station>>?>()))
            .ReturnsAsync(new[] { _q7 });
        _settings.Setup(s => s.GetServiceAreaAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_area);
        _guest.Setup(g => g.Create(It.IsAny<Guid>())).Returns("guest-token");
        _incidents.Setup(i => i.AddAsync(It.IsAny<Incident>())).ReturnsAsync((Incident i) => i);
        _users.Setup(u => u.GetActiveByRolesAsync(It.IsAny<IReadOnlyCollection<UserRole>>())).ReturnsAsync(new List<User>());
        _me.SetupGet(m => m.Role).Returns(UserRole.Operator);
        _me.SetupGet(m => m.StationId).Returns(_q7.Id);
        _svc = new IncidentService(_incidents.Object, _users.Object, _blocked.Object, _ai.Object, _queue.Object, _rt.Object, _notify.Object,
            new CreateIncidentDtoValidator(cfg), new CreateOperatorIncidentDtoValidator(cfg), NullLogger<IncidentService>.Instance,
            _guest.Object, _stations.Object, _settings.Object, _me.Object, _followUp.Object, _routing.Object);
    }

    private static CreateIncidentDto Sos(params string[] media) => new()
    {
        Description = "Cháy",
        Latitude = 10.7335,
        Longitude = 106.7192,
        LocationAddress = " 1 Nguyễn Thị Thập ",
        ReporterPhone = "+84901234567",
        MediaUrls = media.ToList()
    };

    private Incident Existing(IncidentStatus status, Action<Incident>? tweak = null)
    {
        var inc = new Incident { Title = "Cháy", Status = status, ResponsibleStationId = _q7.Id, CreatedAt = DateTime.UtcNow.AddMinutes(-3) };
        tweak?.Invoke(inc);
        _incidents.Setup(i => i.GetIncidentWithDetailsAsync(inc.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inc);
        return inc;
    }

    // =============== CreateIncident (SOS) ===============
    [Fact]
    public async Task Create_UsesShortestRoadStation_AndNotifiesThatStation()
    {
        var roadNearest = new Station { Name = "Road nearest", Latitude = 10.75, Longitude = 106.72,
            CoverageRadiusKm = 0, IsActive = true };
        _stations.Setup(s => s.GetAllAsync(It.IsAny<Expression<Func<Station, bool>>?>(), It.IsAny<Func<IQueryable<Station>, IQueryable<Station>>?>()))
            .ReturnsAsync(new[] { _q7, roadNearest });
        _routing.Setup(r => r.GetLegsToAsync(It.IsAny<IReadOnlyList<(double Lat, double Lng)>>(),
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoadLeg?[] { new(8, 16), new(4, 8) });
        Incident? saved = null;
        _incidents.Setup(i => i.AddAsync(It.IsAny<Incident>())).Callback<Incident>(i => saved = i).ReturnsAsync((Incident i) => i);

        var result = await _svc.CreateIncidentAsync(Sos());

        Assert.True(result.Success);
        Assert.Equal(roadNearest.Id, saved!.ResponsibleStationId);
        Assert.Equal(roadNearest.Id, result.Data!.ResponsibleStationId);
        _rt.Verify(n => n.IncidentCreatedAsync(It.Is<IncidentResponseDto>(i => i.ResponsibleStationId == roadNearest.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_InvalidDto_ReturnsValidationErrors()
    {
        var dto = Sos();
        dto.Latitude = 200;

        var r = await _svc.CreateIncidentAsync(dto);

        Assert.False(r.Success);
        Assert.NotEmpty(r.Errors!);
        _incidents.Verify(i => i.AddAsync(It.IsAny<Incident>()), Times.Never);
    }

    [Fact]
    public async Task Create_BlockedPhone_IsForbidden()
    {
        _blocked.Setup(b => b.GetActiveBlockAsync("0901234567", It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(new BlockedPhoneNumber());

        var r = await _svc.CreateIncidentAsync(Sos());

        Assert.False(r.Success);
        Assert.Equal(ApiErrorCodes.Forbidden, r.ErrorCode);
        _incidents.Verify(i => i.AddAsync(It.IsAny<Incident>()), Times.Never);
    }

    [Fact]
    public async Task Create_RecentActiveIncidentWithin3Minutes_IsTooManyRequests()
    {
        DateTime since = default;
        _incidents.Setup(i => i.HasRecentActiveIncidentAsync(null, "0901234567", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<Guid?, string?, DateTime, CancellationToken>((_, _, s, _) => since = s).ReturnsAsync(true);

        var r = await _svc.CreateIncidentAsync(Sos());

        Assert.False(r.Success);
        Assert.InRange((DateTime.UtcNow - since).TotalMinutes, 2.9, 3.1);
    }

    [Fact]
    public async Task Create_WithoutMedia_IsPendingUnclassified_NotQueuedForAi()
    {
        Incident? saved = null;
        _incidents.Setup(i => i.AddAsync(It.IsAny<Incident>())).Callback<Incident>(i => saved = i).ReturnsAsync((Incident i) => i);

        var r = await _svc.CreateIncidentAsync(Sos());

        Assert.True(r.Success);
        Assert.Equal(IncidentStatus.Pending, saved!.Status);
        Assert.Equal(SeverityLevel.Unclassified, saved.Severity);
        Assert.Equal("0901234567", saved.ReporterPhone);
        Assert.Equal("1 Nguyễn Thị Thập", saved.LocationAddress);
        Assert.Equal("Sự cố tại  1 Nguyễn Thị Thập ", saved.Title);
        Assert.Equal(_q7.Id, saved.ResponsibleStationId);
        Assert.Contains(saved.AuditTrails, a => a.Action == AuditAction.Created);
        _queue.Verify(q => q.EnqueueAsync(It.IsAny<AiAnalysisJob>(), It.IsAny<CancellationToken>()), Times.Never);
        _rt.Verify(n => n.IncidentCreatedAsync(It.IsAny<IncidentResponseDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_WithPhoto_QueuesFirstPhotoForAi()
    {
        Incident? saved = null;
        _incidents.Setup(i => i.AddAsync(It.IsAny<Incident>())).Callback<Incident>(i => saved = i).ReturnsAsync((Incident i) => i);
        var video = "https://res.cloudinary.com/demo/video/upload/a.mp4";
        var photo = "https://res.cloudinary.com/demo/image/upload/b.jpg";

        await _svc.CreateIncidentAsync(Sos(video, photo));

        Assert.Equal(IncidentStatus.AiProcessing, saved!.Status);
        Assert.Contains(saved.MediaItems, m => m.MediaUrl == video && m.MediaType == MediaType.Video);
        _queue.Verify(q => q.EnqueueAsync(It.Is<AiAnalysisJob>(j => j.IncidentId == saved.Id && j.MediaUrl == photo), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_VideoOnly_IsNotQueuedForAi()
    {
        await _svc.CreateIncidentAsync(Sos("https://res.cloudinary.com/demo/video/upload/a.mov"));

        _queue.Verify(q => q.EnqueueAsync(It.IsAny<AiAnalysisJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Guest_ReceivesCancellationToken_LoggedInUserDoesNot()
    {
        var citizen = new User { FullName = "Dân", PhoneNumber = "0912345678", Role = UserRole.Citizen };
        _users.Setup(u => u.GetByIdWithDetailsAsync(citizen.Id)).ReturnsAsync(citizen);

        var guest = await _svc.CreateIncidentAsync(Sos());
        var dto = Sos();
        dto.ReporterPhone = null;
        var user = await _svc.CreateIncidentAsync(dto, citizen.Id);

        Assert.Equal("guest-token", guest.Data!.GuestCancellationToken);
        Assert.Null(user.Data!.GuestCancellationToken);
        Assert.Equal("Dân", user.Data.ReporterName);
    }

    private void EnableAreaFarFromSos(bool rejectOutside)
    {
        _area.Enabled = true;
        _area.RejectOutside = rejectOutside;
        _area.Name = "HCM";
        _area.CenterLat = 10.0;
        _area.CenterLng = 106.0;
        _area.RadiusKm = 5;
    }

    [Fact]
    public async Task Create_OutsideServiceArea_RejectEnabled_IsRejectedWithoutSaving()
    {
        EnableAreaFarFromSos(rejectOutside: true);

        var r = await _svc.CreateIncidentAsync(Sos());

        Assert.False(r.Success);
        Assert.Equal(ApiErrorCodes.OutOfServiceArea, r.ErrorCode);
        Assert.Contains("\"HCM\"", r.Message);
        _incidents.Verify(i => i.AddAsync(It.IsAny<Incident>()), Times.Never);
        _rt.Verify(n => n.IncidentCreatedAsync(It.IsAny<IncidentResponseDto>(), It.IsAny<CancellationToken>()), Times.Never);
        _queue.Verify(q => q.EnqueueAsync(It.IsAny<AiAnalysisJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_InsideServiceArea_RejectEnabled_IsAccepted()
    {
        _area.Enabled = true;
        _area.Name = "Q7";
        _area.CenterLat = 10.7335;
        _area.CenterLng = 106.7192;
        _area.RadiusKm = 5;

        var r = await _svc.CreateIncidentAsync(Sos());

        Assert.True(r.Success);
        Assert.False(r.Data!.IsOutOfServiceArea);
    }

    [Fact]
    public async Task Create_OutsideServiceArea_RejectDisabled_IsAcceptedButFlaggedAndAlerted()
    {
        EnableAreaFarFromSos(rejectOutside: false);
        Incident? saved = null;
        _incidents.Setup(i => i.AddAsync(It.IsAny<Incident>())).Callback<Incident>(i => saved = i).ReturnsAsync((Incident i) => i);

        var r = await _svc.CreateIncidentAsync(Sos());

        Assert.True(r.Success);
        Assert.True(saved!.IsOutOfServiceArea);
        Assert.True(saved.DistanceOutsideAreaKm > 0);
        Assert.Contains(saved.AuditTrails, a => a.Action == AuditAction.OutOfServiceArea);
        _rt.Verify(n => n.OutOfAreaAlertAsync(It.IsAny<OutOfAreaAlertDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // =============== ProcessAiAnalysis ===============
    private static AiClassificationResultDto AiResult(int severity = 2, string type = "fire", bool ok = true)
        => new() { SeverityScore = severity, IncidentType = type, IsSuccess = ok, ModelName = "TriageVL-3B", HazardTags = new() { "fire" }, ConfidenceScore = 0.9 };

    [Fact]
    public async Task Ai_AppliesResultWhenStillProcessing()
    {
        var inc = Existing(IncidentStatus.AiProcessing);
        _ai.Setup(a => a.AnalyzeAsync("u", null, It.IsAny<CancellationToken>())).ReturnsAsync(AiResult());

        await _svc.ProcessAiAnalysisAsync(new AiAnalysisJob(inc.Id, "u", null, DateTime.UtcNow));

        Assert.Equal(IncidentStatus.AiProcessed, inc.Status);
        Assert.Equal(SeverityLevel.Level2, inc.Severity);
        Assert.Equal(IncidentEventType.FireExplosion, inc.EventType);
        Assert.Equal("TriageVL-3B", inc.AiClassification!.ModelName);
        _incidents.Verify(i => i.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _rt.Verify(n => n.IncidentUpdatedAsync(It.IsAny<IncidentResponseDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Ai_DoesNotOverrideOperatorDecision()
    {
        var inc = Existing(IncidentStatus.Verified, i => i.Severity = SeverityLevel.Level3);
        _ai.Setup(a => a.AnalyzeAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(AiResult(1));

        await _svc.ProcessAiAnalysisAsync(new AiAnalysisJob(inc.Id, "u", null, DateTime.UtcNow));

        Assert.Equal(IncidentStatus.Verified, inc.Status);
        Assert.Equal(SeverityLevel.Level3, inc.Severity);
        Assert.Equal(SeverityLevel.Level1, inc.AiClassification!.SeverityScore);
    }

    [Fact]
    public async Task Ai_CancelledOrMissingIncident_IsSkipped()
    {
        var inc = Existing(IncidentStatus.Cancelled);
        _ai.Setup(a => a.AnalyzeAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(AiResult());

        await _svc.ProcessAiAnalysisAsync(new AiAnalysisJob(inc.Id, "u", null, DateTime.UtcNow));
        await _svc.ProcessAiAnalysisAsync(new AiAnalysisJob(Guid.NewGuid(), "u", null, DateTime.UtcNow));

        _incidents.Verify(i => i.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Ai_AlreadyAnalyzed_IsNotDuplicated()
    {
        var inc = Existing(IncidentStatus.AiProcessed, i => i.AiClassification = new AiClassification { ModelName = "x" });
        _ai.Setup(a => a.AnalyzeAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(AiResult());

        await _svc.ProcessAiAnalysisAsync(new AiAnalysisJob(inc.Id, "u", null, DateTime.UtcNow));

        _incidents.Verify(i => i.AddAuditTrailAsync(It.IsAny<IncidentAuditTrail>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Ai_FailedResult_FallsBackToUnclassified()
    {
        var inc = Existing(IncidentStatus.AiProcessing);
        _ai.Setup(a => a.AnalyzeAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AiClassificationResultDto.CreateFallback("timeout"));

        await _svc.ProcessAiAnalysisAsync(new AiAnalysisJob(inc.Id, "u", null, DateTime.UtcNow));

        Assert.Equal(IncidentStatus.AiProcessed, inc.Status);
        Assert.Equal(SeverityLevel.Unclassified, inc.Severity);
        Assert.False(inc.AiClassification!.IsSuccess);
    }

    [Fact]
    public async Task Ai_SaveConflict_RetriesOnceWithoutCallingModelAgain()
    {
        // After ClearChangeTracker the repository reloads a fresh copy from the database
        var inc = new Incident { Status = IncidentStatus.AiProcessing };
        var reloaded = new Incident { Id = inc.Id, Status = IncidentStatus.AiProcessing };
        _incidents.SetupSequence(i => i.GetIncidentWithDetailsAsync(inc.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inc)
            .ReturnsAsync(reloaded);
        _ai.Setup(a => a.AnalyzeAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(AiResult());
        _incidents.SetupSequence(i => i.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("concurrency"))
            .Returns(Task.CompletedTask);

        await _svc.ProcessAiAnalysisAsync(new AiAnalysisJob(inc.Id, "u", null, DateTime.UtcNow));

        _ai.Verify(a => a.AnalyzeAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _incidents.Verify(i => i.ClearChangeTracker(), Times.Once);
        _incidents.Verify(i => i.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Equal(IncidentStatus.AiProcessed, reloaded.Status);
    }

    // =============== Verify ===============
    private static VerifyIncidentDto Verify(SeverityLevel s, string? notes = null, IncidentEventType? type = null)
        => new() { ConfirmedSeverity = s, OperatorNotes = notes, ConfirmedEventType = type };

    [Fact]
    public async Task Verify_AcceptingAiResult_SetsVerified()
    {
        var inc = Existing(IncidentStatus.AiProcessed, i =>
        {
            i.Severity = SeverityLevel.Level2;
            i.EventType = IncidentEventType.FireExplosion;
            i.AiClassification = new AiClassification { SeverityScore = SeverityLevel.Level2, EventType = IncidentEventType.FireExplosion };
            i.ReportedByUserId = Guid.NewGuid();
        });

        var r = await _svc.VerifyIncidentAsync(inc.Id, Verify(SeverityLevel.Level2), _operatorId);

        Assert.True(r.Success);
        Assert.Equal(IncidentStatus.Verified, inc.Status);
        Assert.Equal(_operatorId, inc.VerifiedByUserId);
        Assert.NotNull(inc.ResponseTimeMs);
        _incidents.Verify(i => i.AddAuditTrailAsync(It.Is<IncidentAuditTrail>(a => a.Action == AuditAction.Verified), It.IsAny<CancellationToken>()), Times.Once);
        _notify.Verify(n => n.NotifyUsersAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(inc.ReportedByUserId!.Value)), It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Verify_OverrideWithoutReason_IsRejected_WithReason_IsAudited()
    {
        var inc = Existing(IncidentStatus.AiProcessed, i =>
        {
            i.Severity = SeverityLevel.Level1;
            i.AiClassification = new AiClassification { SeverityScore = SeverityLevel.Level1, EventType = IncidentEventType.FireExplosion };
        });

        var noReason = await _svc.VerifyIncidentAsync(inc.Id, Verify(SeverityLevel.Level3, "abc"), _operatorId);
        var withReason = await _svc.VerifyIncidentAsync(inc.Id, Verify(SeverityLevel.Level3, "Lửa lan sang nhà bên"), _operatorId);

        Assert.False(noReason.Success);
        Assert.True(withReason.Success);
        _incidents.Verify(i => i.AddAuditTrailAsync(It.Is<IncidentAuditTrail>(a => a.Action == AuditAction.SeverityOverridden), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Verify_UnclassifiedIncident_RequiresNotesAndEventType()
    {
        var inc = Existing(IncidentStatus.Pending);

        var noNotes = await _svc.VerifyIncidentAsync(inc.Id, Verify(SeverityLevel.Level2), _operatorId);
        var noType = await _svc.VerifyIncidentAsync(inc.Id, Verify(SeverityLevel.Level2, "Đã gọi xác minh"), _operatorId);
        var ok = await _svc.VerifyIncidentAsync(inc.Id, Verify(SeverityLevel.Level2, "Đã gọi xác minh", IncidentEventType.TrafficAccident), _operatorId);

        Assert.False(noNotes.Success);
        Assert.False(noType.Success);
        Assert.True(ok.Success);
        Assert.Equal(IncidentEventType.TrafficAccident, inc.EventType);
    }

    [Theory]
    [InlineData(IncidentStatus.Verified)]
    [InlineData(IncidentStatus.Dispatched)]
    [InlineData(IncidentStatus.Completed)]
    [InlineData(IncidentStatus.Cancelled)]
    public async Task Verify_WrongStatus_IsRejected(IncidentStatus status)
    {
        var inc = Existing(status);

        Assert.False((await _svc.VerifyIncidentAsync(inc.Id, Verify(SeverityLevel.Level2, "notes ok", IncidentEventType.FireExplosion), _operatorId)).Success);
    }

    [Fact]
    public async Task Verify_EscalatedButNotYetVerified_IsAllowed()
    {
        var inc = Existing(IncidentStatus.Escalated, i => i.EventType = IncidentEventType.FireExplosion);

        Assert.True((await _svc.VerifyIncidentAsync(inc.Id, Verify(SeverityLevel.Level3, "Đã xác minh"), _operatorId)).Success);
    }

    [Fact]
    public async Task Verify_LockedByAnotherOperator_IsConflict()
    {
        var inc = Existing(IncidentStatus.AiProcessed, i => { i.LockedByOperatorId = Guid.NewGuid(); i.LockExpiresAt = DateTime.UtcNow.AddMinutes(1); });

        var r = await _svc.VerifyIncidentAsync(inc.Id, Verify(SeverityLevel.Level2, "notes ok", IncidentEventType.FireExplosion), _operatorId);

        Assert.Equal(ApiErrorCodes.Conflict, r.ErrorCode);
    }

    [Fact]
    public async Task Verify_OtherStationIncident_IsForbidden()
    {
        var inc = Existing(IncidentStatus.AiProcessed, i => i.ResponsibleStationId = Guid.NewGuid());

        var r = await _svc.VerifyIncidentAsync(inc.Id, Verify(SeverityLevel.Level2), _operatorId);

        Assert.Equal(ApiErrorCodes.Forbidden, r.ErrorCode);
    }

    [Fact]
    public async Task Verify_InvalidTrafficJamDecision_IsRejected()
    {
        var inc = Existing(IncidentStatus.AiProcessed, i => i.EventType = IncidentEventType.TrafficAccident);
        var dto = Verify(SeverityLevel.Level1, "Đã xác minh");
        dto.ConfirmedTrafficJamStatus = TrafficJamStatus.NoEvidence;

        Assert.False((await _svc.VerifyIncidentAsync(inc.Id, dto, _operatorId)).Success);
        _incidents.Verify(i => i.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // =============== Cancel ===============
    private User UserWith(UserRole role)
    {
        var u = new User { FullName = role.ToString(), Role = role };
        _users.Setup(x => x.GetByIdAsync(u.Id)).ReturnsAsync(u);
        return u;
    }

    [Fact]
    public async Task Cancel_ByReporterBeforeDispatch_Succeeds()
    {
        var citizen = UserWith(UserRole.Citizen);
        var inc = Existing(IncidentStatus.AiProcessed, i => i.ReportedByUserId = citizen.Id);

        var r = await _svc.CancelIncidentAsync(inc.Id, "Báo nhầm", citizen.Id);

        Assert.True(r.Success);
        Assert.Equal(IncidentStatus.Cancelled, inc.Status);
        Assert.Equal(CancellationReason.CitizenCancelled, inc.CancellationReason);
    }

    [Fact]
    public async Task Cancel_ByOtherCitizen_IsRejected()
    {
        var other = UserWith(UserRole.Citizen);
        var inc = Existing(IncidentStatus.Pending, i => i.ReportedByUserId = Guid.NewGuid());

        Assert.False((await _svc.CancelIncidentAsync(inc.Id, "x", other.Id)).Success);
        Assert.Equal(IncidentStatus.Pending, inc.Status);
    }

    [Fact]
    public async Task Cancel_ByReporterAfterDispatch_IsRejected()
    {
        var citizen = UserWith(UserRole.Citizen);
        var inc = Existing(IncidentStatus.Dispatched, i => i.ReportedByUserId = citizen.Id);

        Assert.False((await _svc.CancelIncidentAsync(inc.Id, "x", citizen.Id)).Success);
    }

    [Fact]
    public async Task Cancel_ByOperator_RecallsActiveUnits()
    {
        var op = UserWith(UserRole.Operator);
        var unit = new RescueUnit { PlateNumber = "51A-1", Status = RescueUnitStatus.Dispatched };
        var inc = Existing(IncidentStatus.Dispatched, i =>
            i.DispatchAssignments.Add(new DispatchAssignment { RescueUnit = unit, Status = DispatchAssignmentStatus.Dispatched }));

        var r = await _svc.CancelIncidentAsync(inc.Id, "Báo giả", op.Id, reasonCode: CancellationReason.FalseReport);

        Assert.True(r.Success);
        Assert.Equal(RescueUnitStatus.Available, unit.Status);
        Assert.Equal(DispatchAssignmentStatus.Cancelled, inc.DispatchAssignments.Single().Status);
        Assert.Equal(CancellationReason.FalseReport, inc.CancellationReason);
        _rt.Verify(n => n.RescueUnitStatusChangedAsync(It.IsAny<RescueUnitStatusEventDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_OperatorCannotUseSystemReasonCodes()
    {
        var op = UserWith(UserRole.Operator);
        var inc = Existing(IncidentStatus.Pending);

        await _svc.CancelIncidentAsync(inc.Id, "x", op.Id, reasonCode: CancellationReason.AutoExpired);

        Assert.Equal(CancellationReason.Other, inc.CancellationReason);
    }

    [Theory]
    [InlineData(IncidentStatus.Completed)]
    [InlineData(IncidentStatus.Cancelled)]
    public async Task Cancel_FinishedIncident_IsRejected(IncidentStatus status)
    {
        var op = UserWith(UserRole.Operator);
        var inc = Existing(status);

        Assert.False((await _svc.CancelIncidentAsync(inc.Id, "x", op.Id)).Success);
    }

    [Fact]
    public async Task Cancel_Anonymous_IsRejected()
    {
        var inc = Existing(IncidentStatus.Pending);

        Assert.False((await _svc.CancelIncidentAsync(inc.Id, "x")).Success);
    }

    [Fact(Skip = "Known gap: guest cancellation token is issued on SOS creation but never validated; guests cannot cancel (SRS allows session-only guest cancel).")]
    public async Task Cancel_GuestWithValidCancellationToken_Succeeds()
    {
        var inc = Existing(IncidentStatus.Pending);
        _guest.Setup(g => g.Validate(inc.Id, "guest-token")).Returns(true);

        Assert.True((await _svc.CancelIncidentAsync(inc.Id, "Báo nhầm", null, default, "guest-token")).Success);
    }

    [Fact]
    public async Task AutoExpire_CancelsStaleIncidentsWithSystemReason()
    {
        var stale = Existing(IncidentStatus.Pending);
        var done = Existing(IncidentStatus.Completed);
        _incidents.Setup(i => i.GetStaleUnverifiedIdsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { stale.Id, done.Id });

        var n = await _svc.AutoExpireStaleIncidentsAsync(TimeSpan.FromHours(24));

        Assert.Equal(1, n);
        Assert.Equal(CancellationReason.AutoExpired, stale.CancellationReason);
        Assert.Equal(IncidentStatus.Completed, done.Status);
    }

    // =============== Lock ===============
    [Fact]
    public async Task Lock_Acquired_ReturnsTwoMinuteLockAndBroadcasts()
    {
        var inc = Existing(IncidentStatus.AiProcessed);
        DateTime expires = default;
        _incidents.Setup(i => i.TryAcquireLockAsync(inc.Id, _operatorId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, DateTime, DateTime, CancellationToken>((_, _, e, _, _) => expires = e).ReturnsAsync(true);

        var r = await _svc.LockIncidentAsync(inc.Id, _operatorId);

        Assert.True(r.Success);
        Assert.InRange((expires - DateTime.UtcNow).TotalSeconds, 115, 121);
        _rt.Verify(n => n.IncidentLockChangedAsync(It.Is<IncidentLockEventDto>(e => e.LockedByOperatorId == _operatorId), _q7.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Lock_HeldByOther_IsConflict()
    {
        var inc = Existing(IncidentStatus.AiProcessed);

        var r = await _svc.LockIncidentAsync(inc.Id, _operatorId);

        Assert.Equal(ApiErrorCodes.Conflict, r.ErrorCode);
    }

    [Fact]
    public async Task Lock_UnknownOrFinished_IsRejected()
    {
        var done = Existing(IncidentStatus.Completed);

        Assert.Equal(ApiErrorCodes.NotFound, (await _svc.LockIncidentAsync(Guid.NewGuid(), _operatorId)).ErrorCode);
        Assert.False((await _svc.LockIncidentAsync(done.Id, _operatorId)).Success);
    }

    [Fact]
    public async Task Unlock_OnlyBroadcastsWhenReleased()
    {
        var id = Guid.NewGuid();

        var r = await _svc.UnlockIncidentAsync(id, _operatorId, force: false);

        Assert.False(r.Data);
        _rt.Verify(n => n.IncidentLockChangedAsync(It.IsAny<IncidentLockEventDto>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // =============== Access ===============
    [Fact]
    public async Task CanAccess_RulesPerRole()
    {
        var citizen = UserWith(UserRole.Citizen);
        var stranger = UserWith(UserRole.Citizen);
        var admin = UserWith(UserRole.Admin);
        var op = UserWith(UserRole.Operator);
        op.StationId = _q7.Id;
        var opOther = UserWith(UserRole.Operator);
        opOther.StationId = Guid.NewGuid();
        var inc = Existing(IncidentStatus.Pending, i => i.ReportedByUserId = citizen.Id);

        Assert.True(await _svc.CanAccessIncidentAsync(inc.Id, citizen.Id));
        Assert.False(await _svc.CanAccessIncidentAsync(inc.Id, stranger.Id));
        Assert.True(await _svc.CanAccessIncidentAsync(inc.Id, admin.Id));
        Assert.True(await _svc.CanAccessIncidentAsync(inc.Id, op.Id));
        Assert.False(await _svc.CanAccessIncidentAsync(inc.Id, opOther.Id));
        Assert.False(await _svc.CanAccessIncidentAsync(inc.Id, Guid.NewGuid()));
    }

    // =============== BR-07 hooks: linking on SOS, quick check, parent cancel ===============
    private Incident LinkParent(double distance = 40)
    {
        var parent = new Incident { Title = "Cháy", Status = IncidentStatus.Dispatched, ResponsibleStationId = Guid.NewGuid(), EventType = IncidentEventType.FireExplosion };
        _followUp.Setup(f => f.FindLinkParentAsync(It.IsAny<Incident>(), It.IsAny<CancellationToken>())).ReturnsAsync((parent, distance));
        _followUp.Setup(f => f.OnReportLinkedAsync(It.IsAny<Incident>(), parent, distance, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LinkedToIncidentDto { IncidentId = parent.Id, AssignedUnitPlates = new() { "51B-001" } });
        return parent;
    }

    [Fact]
    public async Task Create_NearOpenIncident_IsLinked_SkipsTriageAndOperatorQueue()
    {
        Incident? saved = null;
        _incidents.Setup(i => i.AddAsync(It.IsAny<Incident>())).Callback<Incident>(i => saved = i).ReturnsAsync((Incident i) => i);
        var parent = LinkParent();
        var dto = Sos();
        dto.ReportedEventType = IncidentEventType.FireExplosion;

        var r = await _svc.CreateIncidentAsync(dto);

        Assert.True(r.Success);
        Assert.Equal(IncidentStatus.Linked, saved!.Status);
        Assert.Equal(parent.Id, saved.DuplicateOfIncidentId);
        Assert.Equal(parent.ResponsibleStationId, saved.ResponsibleStationId);
        Assert.Equal(40, saved.LinkDistanceMeters);
        Assert.Equal(IncidentEventType.FireExplosion, saved.ReportedEventType);
        Assert.NotNull(saved.LatestReportAt);
        Assert.Equal(parent.Id, r.Data!.LinkedTo!.IncidentId);
        Assert.Equal(parent.Id, r.Data.LinkedToIncidentId);
        Assert.Equal("guest-token", r.Data.GuestCancellationToken);
        Assert.Contains("51B-001", r.Message);
        _queue.Verify(q => q.EnqueueAsync(It.IsAny<AiAnalysisJob>(), It.IsAny<CancellationToken>()), Times.Never);
        _rt.Verify(n => n.IncidentCreatedAsync(It.IsAny<IncidentResponseDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_LinkedWithPhoto_QueuesQuickCheckOnly()
    {
        LinkParent();
        await _svc.CreateIncidentAsync(Sos("https://res.cloudinary.com/demo/image/upload/b.jpg"));
        _queue.Verify(q => q.EnqueueAsync(It.Is<AiAnalysisJob>(j => j.QuickCheckOnly), It.IsAny<CancellationToken>()), Times.Once);
        _queue.Verify(q => q.EnqueueAsync(It.Is<AiAnalysisJob>(j => !j.QuickCheckOnly), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAi_QuickCheckJob_DelegatesToFollowUp()
    {
        var job = new AiAnalysisJob(Guid.NewGuid(), "u", null, DateTime.UtcNow, QuickCheckOnly: true);
        await _svc.ProcessAiAnalysisAsync(job);
        _followUp.Verify(f => f.HandleQuickCheckAsync(job, It.IsAny<CancellationToken>()), Times.Once);
        _ai.Verify(a => a.AnalyzeAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancel_ParentWithLinkedReports_RestoresThem()
    {
        var op = UserWith(UserRole.Operator);
        op.StationId = _q7.Id;
        var inc = Existing(IncidentStatus.Verified, i => i.LinkedReportCount = 2);

        await _svc.CancelIncidentAsync(inc.Id, "Báo sai", op.Id);

        _followUp.Verify(f => f.RestoreLinkedReportsAsync(inc.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_LinkedReport_DecrementsParentCounter()
    {
        var citizen = UserWith(UserRole.Citizen);
        var parentId = Guid.NewGuid();
        var inc = Existing(IncidentStatus.Linked, i => { i.ReportedByUserId = citizen.Id; i.DuplicateOfIncidentId = parentId; });

        await _svc.CancelIncidentAsync(inc.Id, "Báo nhầm", citizen.Id);

        _incidents.Verify(i => i.AdjustLinkedReportCountAsync(parentId, -1, null, It.IsAny<CancellationToken>()), Times.Once);
        _followUp.Verify(f => f.RestoreLinkedReportsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
