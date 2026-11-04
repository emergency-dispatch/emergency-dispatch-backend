using System.Linq.Expressions;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Equipment;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Moq;

namespace EmergencyDispatch.Tests.Application;

public class EquipmentServiceTests
{
    private readonly Mock<IEquipmentRepository> _equipment = new();
    private readonly Mock<IStationRepository> _stations = new();
    private readonly Mock<IRescueUnitRepository> _units = new();
    private readonly Mock<ICurrentUserContext> _me = new();
    private readonly Mock<ISystemAuditService> _audit = new();
    private readonly Station _q7 = new() { Name = "Q7" };
    private readonly Station _q1 = new() { Name = "Q1" };
    private readonly EquipmentService _svc;

    public EquipmentServiceTests()
    {
        var stations = new[] { _q7, _q1 };
        _stations.Setup(s => s.ExistsAsync(It.IsAny<Expression<Func<Station, bool>>>()))
            .ReturnsAsync((Expression<Func<Station, bool>> p) => stations.Any(p.Compile()));
        _equipment.Setup(e => e.AddAsync(It.IsAny<Equipment>())).ReturnsAsync((Equipment e) => e);
        _equipment.Setup(e => e.GetListAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<EquipmentStatus?>(), It.IsAny<EquipmentCategory?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Equipment>());
        ActAs(UserRole.Operator, _q7.Id);
        _svc = new EquipmentService(_equipment.Object, _stations.Object, _units.Object, _me.Object, _audit.Object);
    }

    private void ActAs(UserRole role, Guid? station)
    {
        _me.SetupGet(m => m.Role).Returns(role);
        _me.SetupGet(m => m.StationId).Returns(station);
    }

    private Equipment Existing(Guid stationId, EquipmentStatus status = EquipmentStatus.Available)
    {
        var e = new Equipment { Name = "Bình CO2", StationId = stationId, Status = status, Quantity = 2 };
        _equipment.Setup(x => x.GetWithDetailsAsync(e.Id, It.IsAny<CancellationToken>())).ReturnsAsync(e);
        return e;
    }

    private RescueUnit Unit(Guid stationId)
    {
        var u = new RescueUnit { PlateNumber = "51A-115", StationId = stationId };
        _units.Setup(x => x.GetByIdAsync(u.Id)).ReturnsAsync(u);
        return u;
    }

    private CreateEquipmentDto NewDto(Guid? station = null, Guid? unit = null) => new()
    {
        Name = "  Máy AED  ", Category = EquipmentCategory.Medical, Quantity = 1, StationId = station ?? _q7.Id, RescueUnitId = unit, Notes = "  "
    };

    // ---------- Create ----------
    [Fact]
    public async Task Create_OperatorOwnStation_SavesAvailableTrimmedAndAudits()
    {
        Equipment? saved = null;
        _equipment.Setup(e => e.AddAsync(It.IsAny<Equipment>())).Callback<Equipment>(e => saved = e).ReturnsAsync((Equipment e) => e);
        var unit = Unit(_q7.Id);

        var r = await _svc.CreateAsync(NewDto(unit: unit.Id));

        Assert.True(r.Success);
        Assert.Equal("Máy AED", saved!.Name);
        Assert.Null(saved.Notes);
        Assert.Equal(EquipmentStatus.Available, saved.Status);
        Assert.Equal(unit.Id, saved.RescueUnitId);
        _audit.Verify(a => a.LogAsync(SystemAuditAction.Created, "Equipment", saved.Id, "Máy AED", It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_OperatorOtherStation_IsForbidden()
    {
        var r = await _svc.CreateAsync(NewDto(_q1.Id));

        Assert.Equal(ApiErrorCodes.Forbidden, r.ErrorCode);
        _equipment.Verify(e => e.AddAsync(It.IsAny<Equipment>()), Times.Never);
    }

    [Fact]
    public async Task Create_OperatorWithoutStation_IsForbidden()
    {
        ActAs(UserRole.Operator, null);

        var r = await _svc.CreateAsync(NewDto());

        Assert.Equal(ApiErrorCodes.Forbidden, r.ErrorCode);
    }

    [Fact]
    public async Task Create_AdminAnyStation_Succeeds()
    {
        ActAs(UserRole.Admin, null);

        Assert.True((await _svc.CreateAsync(NewDto(_q1.Id))).Success);
    }

    [Fact]
    public async Task Create_UnknownStation_IsNotFound()
    {
        ActAs(UserRole.Admin, null);

        var r = await _svc.CreateAsync(NewDto(Guid.NewGuid()));

        Assert.Equal(ApiErrorCodes.NotFound, r.ErrorCode);
    }

    [Fact]
    public async Task Create_UnitOfAnotherStation_IsRejected()
    {
        var r = await _svc.CreateAsync(NewDto(unit: Unit(_q1.Id).Id));

        Assert.False(r.Success);
        Assert.Null(r.ErrorCode);
        _equipment.Verify(e => e.AddAsync(It.IsAny<Equipment>()), Times.Never);
    }

    [Fact]
    public async Task Create_UnknownOrDeletedUnit_IsNotFound()
    {
        var deleted = Unit(_q7.Id);
        deleted.IsDeleted = true;

        Assert.Equal(ApiErrorCodes.NotFound, (await _svc.CreateAsync(NewDto(unit: Guid.NewGuid()))).ErrorCode);
        Assert.Equal(ApiErrorCodes.NotFound, (await _svc.CreateAsync(NewDto(unit: deleted.Id))).ErrorCode);
    }

    // ---------- List / Get ----------
    [Fact]
    public async Task List_OperatorWithoutStationFilter_IsScopedToOwnStation()
    {
        var r = await _svc.GetListAsync(new EquipmentFilterDto());

        Assert.True(r.Success);
        _equipment.Verify(e => e.GetListAsync(_q7.Id, null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task List_StaffAskingOtherStation_IsForbidden()
    {
        ActAs(UserRole.RescueStaff, _q7.Id);

        var r = await _svc.GetListAsync(new EquipmentFilterDto { StationId = _q1.Id });

        Assert.Equal(ApiErrorCodes.Forbidden, r.ErrorCode);
        _equipment.Verify(e => e.GetListAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<EquipmentStatus?>(), It.IsAny<EquipmentCategory?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task List_AdminWithoutFilter_IsNotScoped()
    {
        ActAs(UserRole.Admin, null);

        await _svc.GetListAsync(new EquipmentFilterDto { Status = EquipmentStatus.Maintenance });

        _equipment.Verify(e => e.GetListAsync(null, null, EquipmentStatus.Maintenance, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get_OtherStation_IsForbidden_Unknown_IsNotFound()
    {
        var other = Existing(_q1.Id);

        Assert.Equal(ApiErrorCodes.Forbidden, (await _svc.GetByIdAsync(other.Id)).ErrorCode);
        Assert.Equal(ApiErrorCodes.NotFound, (await _svc.GetByIdAsync(Guid.NewGuid())).ErrorCode);
    }

    // ---------- Update ----------
    [Fact]
    public async Task Update_OperatorMovingToAnotherStation_IsForbidden()
    {
        var e = Existing(_q7.Id);

        var r = await _svc.UpdateAsync(e.Id, new UpdateEquipmentDto { Name = "X", StationId = _q1.Id, Quantity = 1 });

        Assert.Equal(ApiErrorCodes.Forbidden, r.ErrorCode);
        Assert.Equal(_q7.Id, e.StationId);
    }

    [Fact]
    public async Task Update_AdminMovesStation_UnitMustBelongToNewStation()
    {
        ActAs(UserRole.Admin, null);
        var e = Existing(_q7.Id);
        var q7Unit = Unit(_q7.Id);

        var bad = await _svc.UpdateAsync(e.Id, new UpdateEquipmentDto { Name = "X", StationId = _q1.Id, RescueUnitId = q7Unit.Id, Quantity = 1 });
        var ok = await _svc.UpdateAsync(e.Id, new UpdateEquipmentDto { Name = " X ", StationId = _q1.Id, Quantity = 3 });

        Assert.False(bad.Success);
        Assert.True(ok.Success);
        Assert.Equal(_q1.Id, e.StationId);
        Assert.Equal("X", e.Name);
        Assert.Equal(3, e.Quantity);
        _audit.Verify(a => a.LogAsync(SystemAuditAction.Updated, "Equipment", e.Id, It.IsAny<string?>(), It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------- Status ----------
    [Fact]
    public async Task Status_StaffOwnStation_ToMaintenanceWithReason_IsSavedAndAudited()
    {
        ActAs(UserRole.RescueStaff, _q7.Id);
        var e = Existing(_q7.Id);

        var r = await _svc.UpdateStatusAsync(e.Id, new UpdateEquipmentStatusDto { Status = EquipmentStatus.Maintenance, Note = "  Van bình bị rò  " });

        Assert.True(r.Success);
        Assert.Equal(EquipmentStatus.Maintenance, e.Status);
        Assert.Equal("Van bình bị rò", e.StatusNote);
        Assert.NotNull(e.StatusChangedAt);
        _audit.Verify(a => a.LogAsync(SystemAuditAction.StatusChanged, "Equipment", e.Id, It.IsAny<string?>(), It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("hỏng")]
    public async Task Status_ToMaintenanceWithoutValidReason_IsRejected(string? note)
    {
        var e = Existing(_q7.Id);

        var r = await _svc.UpdateStatusAsync(e.Id, new UpdateEquipmentStatusDto { Status = EquipmentStatus.Maintenance, Note = note });

        Assert.False(r.Success);
        Assert.Equal(EquipmentStatus.Available, e.Status);
        _equipment.Verify(x => x.UpdateAsync(It.IsAny<Equipment>()), Times.Never);
    }

    [Fact]
    public async Task Status_BackToAvailable_NeedsNoReason_ClearsNote()
    {
        var e = Existing(_q7.Id, EquipmentStatus.Maintenance);
        e.StatusNote = "Van bình bị rò";

        var r = await _svc.UpdateStatusAsync(e.Id, new UpdateEquipmentStatusDto { Status = EquipmentStatus.Available });

        Assert.True(r.Success);
        Assert.Equal(EquipmentStatus.Available, e.Status);
        Assert.Null(e.StatusNote);
    }

    [Fact]
    public async Task Status_Unchanged_DoesNotSaveOrAudit()
    {
        var e = Existing(_q7.Id);

        var r = await _svc.UpdateStatusAsync(e.Id, new UpdateEquipmentStatusDto { Status = EquipmentStatus.Available });

        Assert.True(r.Success);
        _equipment.Verify(x => x.UpdateAsync(It.IsAny<Equipment>()), Times.Never);
        _audit.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Status_StaffOfOtherStation_IsForbidden()
    {
        ActAs(UserRole.RescueStaff, _q1.Id);
        var e = Existing(_q7.Id);

        var r = await _svc.UpdateStatusAsync(e.Id, new UpdateEquipmentStatusDto { Status = EquipmentStatus.Maintenance, Note = "Van bình bị rò" });

        Assert.Equal(ApiErrorCodes.Forbidden, r.ErrorCode);
        Assert.Equal(EquipmentStatus.Available, e.Status);
    }

    // ---------- Delete ----------
    [Fact]
    public async Task Delete_OwnStation_SoftDeletesAndAudits_OtherStationForbidden()
    {
        var mine = Existing(_q7.Id);
        var other = Existing(_q1.Id);

        Assert.True((await _svc.DeleteAsync(mine.Id)).Success);
        Assert.Equal(ApiErrorCodes.Forbidden, (await _svc.DeleteAsync(other.Id)).ErrorCode);

        _equipment.Verify(x => x.DeleteAsync(mine), Times.Once);
        _equipment.Verify(x => x.DeleteAsync(other), Times.Never);
        _audit.Verify(a => a.LogAsync(SystemAuditAction.Deleted, "Equipment", mine.Id, It.IsAny<string?>(), It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
