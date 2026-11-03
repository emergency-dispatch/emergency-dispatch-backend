using System.Linq.Expressions;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.DTOs.Security;
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

/// <summary>Operator tools of IncidentService: manual incident creation, phone reveal and reporter blocking.</summary>
public class IncidentServiceOperatorToolsTests
{
    private readonly Mock<IIncidentRepository> _incidents = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IBlockedPhoneRepository> _blocked = new();
    private readonly Mock<IRealtimeNotifier> _rt = new();
    private readonly Mock<IStationRepository> _stations = new();
    private readonly Mock<ISystemSettingsService> _settings = new();
    private readonly Mock<ICurrentUserContext> _me = new();
    private readonly Station _q7 = new() { Name = "Q7", Latitude = 10.732842, Longitude = 106.719688, IsActive = true };
    private readonly IncidentService _svc;

    public IncidentServiceOperatorToolsTests()
    {
        var cfg = TestHelpers.Config(("Cloudinary:CloudName", "demo"));
        _stations.Setup(s => s.GetByIdAsync(_q7.Id)).ReturnsAsync(_q7);
        _stations.Setup(s => s.GetAllAsync(It.IsAny<Expression<Func<Station, bool>>?>(), It.IsAny<Func<IQueryable<Station>, IQueryable<Station>>?>()))
            .ReturnsAsync(new[] { _q7 });
        _settings.Setup(s => s.GetServiceAreaAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new ServiceAreaSettingsDto());
        _incidents.Setup(i => i.AddAsync(It.IsAny<Incident>())).ReturnsAsync((Incident i) => i);
        _blocked.Setup(b => b.AddAsync(It.IsAny<BlockedPhoneNumber>())).ReturnsAsync((BlockedPhoneNumber b) => b);
        _me.SetupGet(m => m.Role).Returns(UserRole.Operator);
        _me.SetupGet(m => m.StationId).Returns(_q7.Id);
        _svc = new IncidentService(_incidents.Object, _users.Object, _blocked.Object, Mock.Of<IAiClassificationService>(), Mock.Of<IAiAnalysisQueue>(),
            _rt.Object, Mock.Of<INotificationService>(), new CreateIncidentDtoValidator(cfg), new CreateOperatorIncidentDtoValidator(cfg),
            NullLogger<IncidentService>.Instance, Mock.Of<IGuestCancellationTokenService>(), _stations.Object, _settings.Object, _me.Object);
    }

    private User Actor(UserRole role, Guid? station = null)
    {
        var u = new User { FullName = role.ToString(), Role = role, StationId = station };
        _users.Setup(x => x.GetByIdAsync(u.Id)).ReturnsAsync(u);
        return u;
    }

    private Incident Existing(Action<Incident>? tweak = null)
    {
        var inc = new Incident { Title = "Cháy", Status = IncidentStatus.AiProcessed, ResponsibleStationId = _q7.Id, ReporterPhone = "0901234567" };
        tweak?.Invoke(inc);
        _incidents.Setup(i => i.GetIncidentWithDetailsAsync(inc.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inc);
        return inc;
    }

    // -------- CreateByOperator (phone call, radio, CCTV) --------
    private static CreateOperatorIncidentDto PhoneCall() => new()
    {
        Latitude = 10.7335, Longitude = 106.7192, LocationAddress = " Kho A ", Source = IncidentSource.PhoneCall,
        EventType = IncidentEventType.FireExplosion, Severity = SeverityLevel.Level2, OperatorNotes = "  Bảo vệ gọi báo cháy kho  ",
        ReporterPhone = "+84987654321"
    };

    [Fact]
    public async Task CreateByOperator_IsVerifiedImmediately_ForOwnStation()
    {
        Incident? saved = null;
        _incidents.Setup(i => i.AddAsync(It.IsAny<Incident>())).Callback<Incident>(i => saved = i).ReturnsAsync((Incident i) => i);
        var op = Guid.NewGuid();

        var r = await _svc.CreateByOperatorAsync(PhoneCall(), op);

        Assert.True(r.Success);
        Assert.Equal(IncidentStatus.Verified, saved!.Status);
        Assert.Equal(op, saved.VerifiedByUserId);
        Assert.Equal(IncidentSource.PhoneCall, saved.Source);
        Assert.Equal(_q7.Id, saved.ResponsibleStationId);
        Assert.Equal("0987654321", saved.ReporterPhone);
        Assert.Contains(saved.AuditTrails, a => a.Action == AuditAction.CreatedByOperator);
        _rt.Verify(n => n.IncidentCreatedAsync(It.IsAny<IncidentResponseDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateByOperator_Invalid_ReturnsErrors()
    {
        var dto = PhoneCall();
        dto.OperatorNotes = "abc";

        Assert.False((await _svc.CreateByOperatorAsync(dto, Guid.NewGuid())).Success);
        _incidents.Verify(i => i.AddAsync(It.IsAny<Incident>()), Times.Never);
    }

    [Fact]
    public async Task CreateByOperator_OperatorWithoutStation_IsForbidden()
    {
        _me.SetupGet(m => m.StationId).Returns((Guid?)null);

        Assert.Equal(ApiErrorCodes.Forbidden, (await _svc.CreateByOperatorAsync(PhoneCall(), Guid.NewGuid())).ErrorCode);
    }

    // -------- Reveal phone --------
    [Fact]
    public async Task RevealPhone_OperatorOfStation_GetsFullNumberAndAuditIsWritten()
    {
        var op = Actor(UserRole.Operator, _q7.Id);
        var inc = Existing();

        var r = await _svc.RevealReporterPhoneAsync(inc.Id, op.Id);

        Assert.True(r.Success);
        Assert.Equal("0901234567", r.Data!.PhoneNumber);
        _incidents.Verify(i => i.AddAuditTrailAsync(It.Is<IncidentAuditTrail>(a => a.Action == AuditAction.PhoneRevealed && a.NewStateJson!.Contains("090***567")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevealPhone_StaffNotAssigned_IsForbidden_AssignedStaffAllowed()
    {
        _me.SetupGet(m => m.Role).Returns(UserRole.RescueStaff);
        var staff = Actor(UserRole.RescueStaff, _q7.Id);
        var inc = Existing();

        Assert.Equal(ApiErrorCodes.Forbidden, (await _svc.RevealReporterPhoneAsync(inc.Id, staff.Id)).ErrorCode);

        inc.DispatchAssignments.Add(new DispatchAssignment { Status = DispatchAssignmentStatus.EnRoute, RescueUnit = new RescueUnit { StationId = _q7.Id } });
        Assert.True((await _svc.RevealReporterPhoneAsync(inc.Id, staff.Id)).Success);
    }

    [Fact]
    public async Task RevealPhone_UnknownIncident_IsNotFound()
    {
        Assert.Equal(ApiErrorCodes.NotFound, (await _svc.RevealReporterPhoneAsync(Guid.NewGuid(), Guid.NewGuid())).ErrorCode);
    }

    // -------- Block reporter --------
    [Fact]
    public async Task BlockReporter_CreatesBlockWithExpiryAndAudit()
    {
        var op = Actor(UserRole.Operator, _q7.Id);
        var inc = Existing();
        BlockedPhoneNumber? saved = null;
        _blocked.Setup(b => b.AddAsync(It.IsAny<BlockedPhoneNumber>())).Callback<BlockedPhoneNumber>(b => saved = b).ReturnsAsync((BlockedPhoneNumber b) => b);

        var r = await _svc.BlockReporterAsync(inc.Id, new BlockReporterDto { Duration = BlockDuration.ThirtyDays, Reason = " Báo giả " }, op.Id);

        Assert.True(r.Success);
        Assert.Equal("0901234567", saved!.PhoneNumber);
        Assert.Equal("Báo giả", saved.Reason);
        Assert.InRange((saved.ExpiresAt!.Value - DateTime.UtcNow).TotalDays, 29.9, 30.1);
        Assert.Equal("090***567", r.Data!.MaskedPhone);
        _incidents.Verify(i => i.AddAuditTrailAsync(It.Is<IncidentAuditTrail>(a => a.Action == AuditAction.ReporterBlocked), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BlockReporter_AlreadyBlocked_IsConflict()
    {
        var inc = Existing();
        _blocked.Setup(b => b.GetActiveBlockAsync("0901234567", It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(new BlockedPhoneNumber());

        Assert.Equal(ApiErrorCodes.Conflict, (await _svc.BlockReporterAsync(inc.Id, new BlockReporterDto { Reason = "x" }, Guid.NewGuid())).ErrorCode);
    }

    [Fact]
    public async Task BlockReporter_NoPhone_Fails()
    {
        var inc = Existing(i => i.ReporterPhone = null);

        Assert.False((await _svc.BlockReporterAsync(inc.Id, new BlockReporterDto { Reason = "x" }, Guid.NewGuid())).Success);
    }

    [Fact]
    public async Task BlockReporter_OtherStation_IsForbidden()
    {
        var inc = Existing(i => i.ResponsibleStationId = Guid.NewGuid());

        Assert.Equal(ApiErrorCodes.Forbidden, (await _svc.BlockReporterAsync(inc.Id, new BlockReporterDto { Reason = "x" }, Guid.NewGuid())).ErrorCode);
    }
}
