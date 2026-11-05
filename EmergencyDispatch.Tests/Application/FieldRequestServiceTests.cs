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

/// <summary>Field requests from rescue teams (Backup / Other): create, respond, cancel, dispatch hand-off, overdue escalation.</summary>
public class FieldRequestServiceTests
{
    private readonly Mock<IFieldRequestRepository> _repo = new();
    private readonly Mock<IDispatchAssignmentRepository> _assign = new();
    private readonly Mock<IIncidentRepository> _incidents = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IRealtimeNotifier> _rt = new();
    private readonly Mock<INotificationService> _notify = new();
    private readonly Mock<ICurrentUserContext> _me = new();
    private readonly Mock<ISystemSettingsService> _settings = new();
    private readonly List<IncidentAuditTrail> _audits = new();
    private readonly List<FieldRequest> _store = new();
    private readonly Dictionary<Guid, DispatchAssignment> _assignments = new();
    private readonly Guid _stationA = Guid.NewGuid();
    private readonly Guid _stationB = Guid.NewGuid();
    private readonly FieldRequestService _svc;

    public FieldRequestServiceTests()
    {
        _incidents.Setup(i => i.AddAuditTrailAsync(It.IsAny<IncidentAuditTrail>(), It.IsAny<CancellationToken>()))
            .Callback<IncidentAuditTrail, CancellationToken>((a, _) => _audits.Add(a)).Returns(Task.CompletedTask);
        // Như EF Include: bản ghi mới đọc lại có đủ navigation của lượt điều phối
        _repo.Setup(r => r.AddAsync(It.IsAny<FieldRequest>())).Callback<FieldRequest>(r =>
        {
            if (_assignments.TryGetValue(r.AssignmentId, out var a))
            {
                r.Assignment = a;
                r.Incident = a.Incident;
                r.RescueUnit = a.RescueUnit;
            }
            _store.Add(r);
        }).ReturnsAsync((FieldRequest r) => r);
        _repo.Setup(r => r.GetWithDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _store.FirstOrDefault(r => r.Id == id));
        _repo.Setup(r => r.GetByIncidentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _store.Where(r => r.IncidentId == id).ToList());
        _repo.Setup(r => r.GetOpenAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _store.Where(r => r.IsOpen).ToList());
        _users.Setup(u => u.GetActiveByRolesAsync(It.IsAny<IReadOnlyCollection<UserRole>>())).ReturnsAsync(new List<User>());
        _settings.Setup(s => s.GetEscalationSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new EscalationSettingsDto { FieldRequestThresholdMinutes = 3 });
        AsOperator(_stationA);
        _svc = new FieldRequestService(_repo.Object, _assign.Object, _incidents.Object, _users.Object, _rt.Object, _notify.Object,
            _me.Object, _settings.Object, NullLogger<FieldRequestService>.Instance);
    }

    private void AsOperator(Guid? station, UserRole role = UserRole.Operator)
    {
        _me.SetupGet(m => m.Role).Returns(role);
        _me.SetupGet(m => m.StationId).Returns(station);
    }

    private User Staff(Guid station, UserRole role = UserRole.RescueStaff)
    {
        var u = new User { Role = role, StationId = station, FullName = "Staff" };
        _users.Setup(x => x.GetByIdAsync(u.Id)).ReturnsAsync(u);
        return u;
    }

    private DispatchAssignment Assignment(DispatchAssignmentStatus status = DispatchAssignmentStatus.OnScene, Guid? unitStation = null, Guid? incidentStation = null)
    {
        var incident = new Incident { Title = "Cháy", ResponsibleStationId = incidentStation ?? _stationA, Status = IncidentStatus.InProgress };
        var unit = new RescueUnit { PlateNumber = "51B-001", StationId = unitStation ?? _stationA };
        var a = new DispatchAssignment { Incident = incident, IncidentId = incident.Id, RescueUnit = unit, RescueUnitId = unit.Id, Status = status };
        _assign.Setup(x => x.GetByIdWithDetailsAsync(a.Id)).ReturnsAsync(a);
        _assignments[a.Id] = a;
        return a;
    }

    private FieldRequest Existing(FieldRequestStatus status = FieldRequestStatus.Open, FieldRequestType type = FieldRequestType.Backup,
        Guid? unitStation = null, Guid? incidentStation = null, IncidentStatus incidentStatus = IncidentStatus.InProgress)
    {
        var a = Assignment(unitStation: unitStation, incidentStation: incidentStation);
        a.Incident!.Status = incidentStatus;
        var r = new FieldRequest
        {
            Incident = a.Incident, IncidentId = a.IncidentId, Assignment = a, AssignmentId = a.Id, RescueUnit = a.RescueUnit,
            RescueUnitId = a.RescueUnitId, RequestedByUserId = Guid.NewGuid(), Type = type, Status = status, Quantity = 1,
            Description = "Cần thêm xe", CreatedAt = DateTime.UtcNow.AddMinutes(-10)
        };
        _store.Add(r);
        return r;
    }

    private static CreateFieldRequestDto Backup(int qty = 1) => new() { Type = FieldRequestType.Backup, Quantity = qty };

    // =============== Create ===============
    [Fact]
    public async Task Create_Backup_ByStaffOfUnitStation_IsOpenAudited()
    {
        var a = Assignment();
        var staff = Staff(_stationA);

        var r = await _svc.CreateAsync(a.Id, new CreateFieldRequestDto { Type = FieldRequestType.Backup, Quantity = 2, RequestedUnitType = RescueUnitType.Ambulance, Description = " 2 người bỏng " }, staff.Id);

        Assert.True(r.Success);
        var saved = Assert.Single(_store);
        Assert.Equal(FieldRequestStatus.Open, saved.Status);
        Assert.Equal(2, saved.Quantity);
        Assert.Equal("2 người bỏng", saved.Description);
        Assert.Equal(a.IncidentId, saved.IncidentId);
        Assert.Contains(_audits, x => x.Action == AuditAction.FieldRequestCreated && x.IncidentId == a.IncidentId);
        _rt.Verify(x => x.FieldRequestChangedAsync(It.Is<FieldRequestEventDto>(e => e.Action == "Created"), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_NotifiesOperatorsOfResponsibleAndUnitStations()
    {
        var opA = new User { Role = UserRole.Operator, StationId = _stationA };
        var opB = new User { Role = UserRole.Operator, StationId = _stationB };
        var opC = new User { Role = UserRole.Operator, StationId = Guid.NewGuid() };
        _users.Setup(u => u.GetActiveByRolesAsync(It.IsAny<IReadOnlyCollection<UserRole>>())).ReturnsAsync(new List<User> { opA, opB, opC });
        var a = Assignment(unitStation: _stationB);
        var staff = Staff(_stationB);

        await _svc.CreateAsync(a.Id, Backup(), staff.Id);

        _notify.Verify(n => n.NotifyUsersAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(opA.Id) && ids.Contains(opB.Id)),
            It.Is<NotificationRequest>(q => q.HighPriority), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_Other_WithoutDescription_Fails()
    {
        var a = Assignment();
        var r = await _svc.CreateAsync(a.Id, new CreateFieldRequestDto { Type = FieldRequestType.Other, Description = "abc" }, Staff(_stationA).Id);
        Assert.False(r.Success);
        Assert.Empty(_store);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task Create_BackupQuantityOutOfRange_Fails(int qty)
    {
        var a = Assignment();
        Assert.False((await _svc.CreateAsync(a.Id, Backup(qty), Staff(_stationA).Id)).Success);
    }

    [Fact]
    public async Task Create_InvalidType_Fails()
        => Assert.False((await _svc.CreateAsync(Guid.NewGuid(), new CreateFieldRequestDto { Type = (FieldRequestType)9 }, Guid.NewGuid())).Success);

    [Fact]
    public async Task Create_UnknownAssignment_IsNotFound()
        => Assert.Equal(ApiErrorCodes.NotFound, (await _svc.CreateAsync(Guid.NewGuid(), Backup(), Staff(_stationA).Id)).ErrorCode);

    [Fact]
    public async Task Create_StaffOfOtherStation_IsForbidden()
    {
        var a = Assignment();
        Assert.Equal(ApiErrorCodes.Forbidden, (await _svc.CreateAsync(a.Id, Backup(), Staff(_stationB).Id)).ErrorCode);
    }

    [Fact]
    public async Task Create_OperatorUser_IsForbidden()
    {
        var a = Assignment();
        Assert.Equal(ApiErrorCodes.Forbidden, (await _svc.CreateAsync(a.Id, Backup(), Staff(_stationA, UserRole.Operator).Id)).ErrorCode);
    }

    [Theory]
    [InlineData(DispatchAssignmentStatus.Dispatched)]
    [InlineData(DispatchAssignmentStatus.Completed)]
    [InlineData(DispatchAssignmentStatus.Cancelled)]
    public async Task Create_AssignmentNotRunning_Fails(DispatchAssignmentStatus status)
    {
        var a = Assignment(status);
        Assert.False((await _svc.CreateAsync(a.Id, Backup(), Staff(_stationA).Id)).Success);
    }

    [Fact]
    public async Task Create_SecondOpenRequestOfSameType_IsConflict_OtherTypeAllowed()
    {
        var a = Assignment();
        var staff = Staff(_stationA);
        await _svc.CreateAsync(a.Id, Backup(), staff.Id);

        Assert.Equal(ApiErrorCodes.Conflict, (await _svc.CreateAsync(a.Id, Backup(), staff.Id)).ErrorCode);
        Assert.True((await _svc.CreateAsync(a.Id, new CreateFieldRequestDto { Type = FieldRequestType.Other, Description = "Cần cảnh sát" }, staff.Id)).Success);
    }

    // =============== Respond ===============
    [Fact]
    public async Task Acknowledge_Open_SetsAcknowledgedAndNotifiesRequester()
    {
        var req = Existing();
        var opId = Guid.NewGuid();

        var r = await _svc.AcknowledgeAsync(req.Id, opId);

        Assert.True(r.Success);
        Assert.Equal(FieldRequestStatus.Acknowledged, req.Status);
        Assert.Equal(opId, req.AcknowledgedByUserId);
        Assert.Contains(_audits, x => x.Action == AuditAction.FieldRequestAcknowledged);
        _notify.Verify(n => n.NotifyUsersAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(req.RequestedByUserId)), It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Acknowledge_Twice_IsConflict()
    {
        var req = Existing(FieldRequestStatus.Acknowledged);
        Assert.Equal(ApiErrorCodes.Conflict, (await _svc.AcknowledgeAsync(req.Id, Guid.NewGuid())).ErrorCode);
    }

    [Fact]
    public async Task Respond_OperatorOfUnrelatedStation_IsForbidden()
    {
        var req = Existing(unitStation: _stationB, incidentStation: _stationB);
        Assert.Equal(ApiErrorCodes.Forbidden, (await _svc.AcknowledgeAsync(req.Id, Guid.NewGuid())).ErrorCode);
    }

    [Fact]
    public async Task Respond_OperatorOfSupportUnitStation_IsAllowed()
    {
        var req = Existing(unitStation: _stationA, incidentStation: _stationB);
        Assert.True((await _svc.AcknowledgeAsync(req.Id, Guid.NewGuid())).Success);
    }

    [Fact]
    public async Task Respond_UnknownRequest_IsNotFound()
        => Assert.Equal(ApiErrorCodes.NotFound, (await _svc.FulfillAsync(Guid.NewGuid(), null, Guid.NewGuid())).ErrorCode);

    [Fact]
    public async Task Fulfill_Acknowledged_IsResolvedWithNote()
    {
        var req = Existing(FieldRequestStatus.Acknowledged);
        var r = await _svc.FulfillAsync(req.Id, " Đã báo cảnh sát ", Guid.NewGuid());
        Assert.True(r.Success);
        Assert.Equal(FieldRequestStatus.Fulfilled, req.Status);
        Assert.Equal("Đã báo cảnh sát", req.ResponseNote);
        Assert.NotNull(req.ResolvedAt);
        Assert.Contains(_audits, x => x.Action == AuditAction.FieldRequestFulfilled);
    }

    [Fact]
    public async Task Fulfill_Closed_IsConflict()
    {
        var req = Existing(FieldRequestStatus.Declined);
        Assert.Equal(ApiErrorCodes.Conflict, (await _svc.FulfillAsync(req.Id, null, Guid.NewGuid())).ErrorCode);
    }

    [Fact]
    public async Task Fulfill_NoteTooLong_Fails()
    {
        var req = Existing();
        Assert.False((await _svc.FulfillAsync(req.Id, new string('x', FieldRequestService.MaxNoteLength + 1), Guid.NewGuid())).Success);
        Assert.Equal(FieldRequestStatus.Open, req.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public async Task Decline_RequiresReason(string? reason)
    {
        var req = Existing();
        Assert.False((await _svc.DeclineAsync(req.Id, reason, Guid.NewGuid())).Success);
        Assert.Equal(FieldRequestStatus.Open, req.Status);
    }

    [Fact]
    public async Task Decline_WithReason_IsDeclinedAndAudited()
    {
        var req = Existing();
        Assert.True((await _svc.DeclineAsync(req.Id, "Không còn xe trống", Guid.NewGuid())).Success);
        Assert.Equal(FieldRequestStatus.Declined, req.Status);
        Assert.Contains(_audits, x => x.Action == AuditAction.FieldRequestDeclined && x.Description.Contains("Không còn xe trống"));
    }

    // =============== Cancel (staff) ===============
    [Fact]
    public async Task Cancel_ByStaffOfUnitStation_IsCancelled()
    {
        var req = Existing();
        var r = await _svc.CancelAsync(req.Id, "Đã ổn", Staff(_stationA).Id);
        Assert.True(r.Success);
        Assert.Equal(FieldRequestStatus.Cancelled, req.Status);
        Assert.Contains(_audits, x => x.Action == AuditAction.FieldRequestCancelled && x.ActorRole == nameof(UserRole.RescueStaff));
    }

    [Fact]
    public async Task Cancel_ByStaffOfOtherStation_IsForbidden()
    {
        var req = Existing();
        Assert.Equal(ApiErrorCodes.Forbidden, (await _svc.CancelAsync(req.Id, null, Staff(_stationB).Id)).ErrorCode);
    }

    [Fact]
    public async Task Cancel_AlreadyFulfilled_IsConflict()
    {
        var req = Existing(FieldRequestStatus.Fulfilled);
        Assert.Equal(ApiErrorCodes.Conflict, (await _svc.CancelAsync(req.Id, null, Staff(_stationA).Id)).ErrorCode);
    }

    // =============== Lists ===============
    [Fact]
    public async Task GetOpen_OperatorSeesOwnIncidentsAndOwnUnitsOnly()
    {
        var own = Existing(incidentStation: _stationA, unitStation: _stationA);
        var support = Existing(incidentStation: _stationB, unitStation: _stationA);
        var other = Existing(incidentStation: _stationB, unitStation: _stationB);
        Existing(FieldRequestStatus.Fulfilled);

        var ids = (await _svc.GetOpenAsync()).Data!.Select(d => d.Id).ToList();

        Assert.Contains(own.Id, ids);
        Assert.Contains(support.Id, ids);
        Assert.DoesNotContain(other.Id, ids);
        Assert.Equal(2, ids.Count);
    }

    [Fact]
    public async Task GetOpen_AdminSeesAll()
    {
        AsOperator(null, UserRole.Admin);
        Existing(incidentStation: _stationB, unitStation: _stationB);
        Existing();
        Assert.Equal(2, (await _svc.GetOpenAsync()).Data!.Count);
    }

    [Fact]
    public async Task GetByIncident_StaffSeesOnlyOwnStationUnits()
    {
        var mine = Existing(unitStation: _stationA);
        var incident = mine.Incident!;
        var otherUnit = new RescueUnit { StationId = _stationB };
        _store.Add(new FieldRequest { IncidentId = incident.Id, Incident = incident, RescueUnit = otherUnit, Type = FieldRequestType.Other, Description = "x" });
        _incidents.Setup(i => i.GetIncidentWithDetailsAsync(incident.Id, It.IsAny<CancellationToken>())).ReturnsAsync(incident);
        AsOperator(_stationA, UserRole.RescueStaff);

        var r = await _svc.GetByIncidentAsync(incident.Id, Guid.NewGuid());

        Assert.Equal(mine.Id, Assert.Single(r.Data!).Id);
    }

    [Fact]
    public async Task GetByIncident_UnknownIncident_IsNotFound()
        => Assert.Equal(ApiErrorCodes.NotFound, (await _svc.GetByIncidentAsync(Guid.NewGuid(), Guid.NewGuid())).ErrorCode);

    // =============== Dispatch hand-off ===============
    [Fact]
    public async Task ValidateForDispatch_OpenBackupOfSameIncident_IsValid()
    {
        var req = Existing();
        Assert.Null(await _svc.ValidateForDispatchAsync(req.Id, req.IncidentId));
    }

    [Fact]
    public async Task ValidateForDispatch_WrongIncidentOtherTypeOrClosed_ReturnsError()
    {
        var backup = Existing();
        var other = Existing(type: FieldRequestType.Other);
        var closed = Existing(FieldRequestStatus.Declined);

        Assert.NotNull(await _svc.ValidateForDispatchAsync(backup.Id, Guid.NewGuid()));
        Assert.NotNull(await _svc.ValidateForDispatchAsync(other.Id, other.IncidentId));
        Assert.NotNull(await _svc.ValidateForDispatchAsync(closed.Id, closed.IncidentId));
    }

    [Fact]
    public async Task MarkFulfilledByDispatch_ResolvesWithPlates()
    {
        var req = Existing(FieldRequestStatus.Acknowledged);
        await _svc.MarkFulfilledByDispatchAsync(req.Id, new[] { "51B-002", "51B-003" }, Guid.NewGuid());
        Assert.Equal(FieldRequestStatus.Fulfilled, req.Status);
        Assert.Contains("51B-002, 51B-003", req.ResponseNote);
    }

    [Fact]
    public async Task MarkFulfilledByDispatch_RepositoryFailure_IsSwallowed()
    {
        _repo.Setup(r => r.GetWithDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db"));
        await _svc.MarkFulfilledByDispatchAsync(Guid.NewGuid(), new[] { "x" }, Guid.NewGuid());
    }

    // =============== Overdue escalation ===============
    [Fact]
    public async Task ProcessOverdue_UsesThresholdFromSettings()
    {
        _repo.Setup(r => r.GetOverdueAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<FieldRequest>());
        await _svc.ProcessOverdueAsync();
        _repo.Verify(r => r.GetOverdueAsync(It.Is<DateTime>(d => Math.Abs((DateTime.UtcNow.AddMinutes(-3) - d).TotalSeconds) < 5), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task ProcessOverdue_EscalatesAndNotifiesAdminsAndStationOperators()
    {
        var req = Existing();
        var admin = new User { Role = UserRole.Admin };
        var op = new User { Role = UserRole.Operator, StationId = _stationA };
        var otherOp = new User { Role = UserRole.Operator, StationId = _stationB };
        _users.Setup(u => u.GetActiveByRolesAsync(It.Is<IReadOnlyCollection<UserRole>>(r => r.Contains(UserRole.Admin)))).ReturnsAsync(new List<User> { admin });
        _users.Setup(u => u.GetActiveByRolesAsync(It.Is<IReadOnlyCollection<UserRole>>(r => r.Contains(UserRole.Operator)))).ReturnsAsync(new List<User> { op, otherOp });
        _repo.Setup(r => r.GetOverdueAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<FieldRequest> { req });

        Assert.Equal(1, await _svc.ProcessOverdueAsync());

        Assert.NotNull(req.EscalatedAt);
        Assert.Equal(FieldRequestStatus.Open, req.Status);
        Assert.Contains(_audits, x => x.Action == AuditAction.FieldRequestEscalated && x.ActorRole == "System");
        _notify.Verify(n => n.NotifyUsersAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(admin.Id) && ids.Contains(op.Id)),
            It.Is<NotificationRequest>(q => q.Type == NotificationType.Escalation), It.IsAny<CancellationToken>()), Times.Once);
        _rt.Verify(x => x.FieldRequestChangedAsync(It.Is<FieldRequestEventDto>(e => e.Action == "Escalated"), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessOverdue_ClosedIncident_AutoCancelsInsteadOfEscalating()
    {
        var req = Existing(incidentStatus: IncidentStatus.Completed);
        _repo.Setup(r => r.GetOverdueAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<FieldRequest> { req });

        Assert.Equal(0, await _svc.ProcessOverdueAsync());

        Assert.Equal(FieldRequestStatus.Cancelled, req.Status);
        Assert.Null(req.EscalatedAt);
        Assert.Contains(_audits, x => x.Action == AuditAction.FieldRequestCancelled);
    }

    [Fact]
    public async Task ProcessOverdue_EndedAssignment_AutoCancels()
    {
        var req = Existing();
        req.Assignment!.Status = DispatchAssignmentStatus.Completed;
        _repo.Setup(r => r.GetOverdueAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<FieldRequest> { req });

        await _svc.ProcessOverdueAsync();

        Assert.Equal(FieldRequestStatus.Cancelled, req.Status);
    }
}
