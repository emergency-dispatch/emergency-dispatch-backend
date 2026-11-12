using System.Linq.Expressions;
using EmergencyDispatch.Application.DTOs.Catalog;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Dispatch;
using EmergencyDispatch.Application.DTOs.Incident;
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

/// <summary>
/// Danh mục loại xe / loại sự cố (Admin quản lý)
/// </summary>
public class TypeCatalogServiceTests
{
    private readonly Mock<ITypeCatalogRepository> _repo = new();
    private readonly Mock<ISystemAuditService> _audit = new();
    private readonly List<RescueUnitTypeDefinition> _unitTypes = new()
    {
        new() { Code = RescueUnitType.Ambulance, NameVi = "Xe cấp cứu", NameEn = "Ambulance", IsSystem = true },
        new() { Code = RescueUnitType.FireTruck, NameVi = "Xe chữa cháy", NameEn = "Fire truck", IsSystem = true }
    };
    private readonly TypeCatalogService _svc;

    public TypeCatalogServiceTests()
    {
        _repo.Setup(r => r.GetUnitTypesAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _unitTypes);
        _repo.Setup(r => r.GetUnitTypeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string code, CancellationToken _) => _unitTypes.FirstOrDefault(t => t.Code == code));
        _svc = new TypeCatalogService(_repo.Object, _audit.Object);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1Truck")]
    [InlineData("Xe cứu hộ")]
    [InlineData("Water-Tanker")]
    public async Task CreateUnitType_InvalidCode_IsRejected(string code)
    {
        var r = await _svc.CreateUnitTypeAsync(new UpsertRescueUnitTypeDto { Code = code, NameVi = "A", NameEn = "A" });

        Assert.False(r.Success);
        _repo.Verify(x => x.AddUnitTypeAsync(It.IsAny<RescueUnitTypeDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateUnitType_NewCode_IsAddedAsNonSystem()
    {
        RescueUnitTypeDefinition? added = null;
        _repo.Setup(x => x.AddUnitTypeAsync(It.IsAny<RescueUnitTypeDefinition>(), It.IsAny<CancellationToken>()))
            .Callback<RescueUnitTypeDefinition, CancellationToken>((t, _) => added = t);

        var r = await _svc.CreateUnitTypeAsync(new UpsertRescueUnitTypeDto { Code = "WaterTanker", NameVi = " Xe bồn nước ", NameEn = "Water tanker" });

        Assert.True(r.Success);
        Assert.Equal("WaterTanker", added!.Code);
        Assert.Equal("Xe bồn nước", added.NameVi);
        Assert.False(added.IsSystem);
    }

    [Fact]
    public async Task CreateUnitType_ExistingCode_IsConflict()
    {
        var r = await _svc.CreateUnitTypeAsync(new UpsertRescueUnitTypeDto { Code = RescueUnitType.Ambulance, NameVi = "A", NameEn = "A" });

        Assert.Equal(ApiErrorCodes.Conflict, r.ErrorCode);
    }

    [Fact]
    public async Task DeleteUnitType_SystemOrInUse_IsRejected_UnusedCustomIsDeleted()
    {
        var system = _unitTypes[0];
        var used = new RescueUnitTypeDefinition { Code = "Drone", NameVi = "Drone", NameEn = "Drone" };
        var unused = new RescueUnitTypeDefinition { Code = "Bike", NameVi = "Xe máy", NameEn = "Motorbike" };
        foreach (var t in new[] { system, used, unused })
        {
            _repo.Setup(x => x.GetUnitTypeByIdAsync(t.Id, It.IsAny<CancellationToken>())).ReturnsAsync(t);
        }
        _repo.Setup(x => x.IsUnitTypeInUseAsync("Drone", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Assert.False((await _svc.DeleteUnitTypeAsync(system.Id)).Success);
        Assert.Equal(ApiErrorCodes.Conflict, (await _svc.DeleteUnitTypeAsync(used.Id)).ErrorCode);
        Assert.True((await _svc.DeleteUnitTypeAsync(unused.Id)).Success);
        _repo.Verify(x => x.RemoveUnitType(unused), Times.Once);
        _repo.Verify(x => x.RemoveUnitType(It.Is<RescueUnitTypeDefinition>(t => t != unused)), Times.Never);
    }

    [Fact]
    public async Task UpdateIncidentType_SystemCategoryLocked()
    {
        var fire = new IncidentTypeDefinition { Code = "FireExplosion", NameVi = "Cháy nổ", NameEn = "Fire", Category = IncidentEventType.FireExplosion, IsSystem = true };
        _repo.Setup(x => x.GetIncidentTypeByIdAsync(fire.Id, It.IsAny<CancellationToken>())).ReturnsAsync(fire);

        var r = await _svc.UpdateIncidentTypeAsync(fire.Id, new UpsertIncidentTypeDto { NameVi = "Cháy", NameEn = "Fire", Category = IncidentEventType.Other });

        Assert.False(r.Success);
        Assert.Equal(IncidentEventType.FireExplosion, fire.Category);
    }

    private static DispatchPlanLevelDto Level(SeverityLevel severity, params (string Code, int Qty)[] units)
        => new() { Severity = severity, Units = units.Select(u => new DispatchPlanUnitDto { UnitTypeCode = u.Code, Quantity = u.Qty }).ToList() };

    [Fact]
    public async Task UpdateDispatchPlan_ValidatesUnitTypeQuantityDuplicatesAndLevelTotal()
    {
        var type = new IncidentTypeDefinition { Code = "FloodDrowning", NameVi = "Đuối nước", NameEn = "Drowning", Category = IncidentEventType.Other };
        _repo.Setup(x => x.GetIncidentTypeByIdAsync(type.Id, It.IsAny<CancellationToken>())).ReturnsAsync(type);
        Task<ApiResponseDto<IncidentTypeDto>> Save(params DispatchPlanLevelDto[] levels)
            => _svc.UpdateDispatchPlanAsync(type.Id, new UpdateDispatchPlanDto { Levels = levels.ToList() });

        Assert.False((await Save(Level(SeverityLevel.Level1, ("Boat2", 1)))).Success);                                     // loại xe không có
        Assert.False((await Save(Level(SeverityLevel.Level1, (RescueUnitType.Ambulance, 9)))).Success);                    // số lượng > 5
        Assert.False((await Save(Level(SeverityLevel.Level1, (RescueUnitType.Ambulance, 1), (RescueUnitType.Ambulance, 1)))).Success); // trùng loại trong mức
        Assert.False((await Save(Level(SeverityLevel.Level3, (RescueUnitType.Ambulance, 3), (RescueUnitType.FireTruck, 3)))).Success); // tổng 6 > 5
        Assert.False((await Save(Level(SeverityLevel.Level1, (RescueUnitType.Ambulance, 1)), Level(SeverityLevel.Level1, (RescueUnitType.FireTruck, 1)))).Success); // lặp mức
        Assert.False((await Save(Level(SeverityLevel.Unclassified, (RescueUnitType.Ambulance, 1)))).Success);              // Level 0

        // Level 1: 1 xe chữa cháy; Level 3: 2 xe chữa cháy + 1 xe cấp cứu; Level 2 bỏ trống
        var ok = await Save(
            Level(SeverityLevel.Level1, (RescueUnitType.FireTruck, 1)),
            Level(SeverityLevel.Level3, (RescueUnitType.FireTruck, 2), (RescueUnitType.Ambulance, 1)));

        Assert.True(ok.Success);
        var plan = ok.Data!.DispatchPlan;
        Assert.Equal(new[] { SeverityLevel.Level1, SeverityLevel.Level2, SeverityLevel.Level3 }, plan.Select(l => l.Severity));
        Assert.Equal(new[] { 1, 0, 3 }, plan.Select(l => l.TotalUnits));
        Assert.Equal(new[] { RescueUnitType.FireTruck, RescueUnitType.Ambulance }, plan[2].Units.Select(u => u.UnitTypeCode));
        Assert.Contains(plan[2].Units[1].UnitTypeName, new[] { "Xe cấp cứu", "Ambulance" });   // tên theo Accept-Language
        Assert.Equal(3, type.UnitRules.Count);
    }
}

/// <summary>
/// Quy tắc loại xe theo loại sự cố + mã loại sự cố
/// </summary>
public class UnitRuleScoringTests
{
    private static IncidentTypeUnitRule Rule(string unit, SeverityLevel level, int qty = 1, int priority = 0)
        => new() { UnitTypeCode = unit, Severity = level, Quantity = qty, Priority = priority };

    [Fact]
    public void MatchingUnitTypes_UsesExactLevelPlan_MissingLevelUsesNearestLower()
    {
        var rules = new List<IncidentTypeUnitRule>
        {
            Rule(RescueUnitType.FireTruck, SeverityLevel.Level1, priority: 1),
            Rule(RescueUnitType.FireTruck, SeverityLevel.Level3, qty: 2, priority: 1),
            Rule(RescueUnitType.Ambulance, SeverityLevel.Level3, priority: 2)
        };

        var l1 = DispatchScoring.MatchingUnitTypes(rules, IncidentEventType.FireExplosion, Array.Empty<string>(), "", SeverityLevel.Level1);
        var l2 = DispatchScoring.MatchingUnitTypes(rules, IncidentEventType.FireExplosion, Array.Empty<string>(), "", SeverityLevel.Level2);
        var l3 = DispatchScoring.MatchingUnitTypes(rules, IncidentEventType.FireExplosion, Array.Empty<string>(), "", SeverityLevel.Level3);

        Assert.Equal(new[] { RescueUnitType.FireTruck }, l1!.ToArray());
        Assert.Equal(new[] { RescueUnitType.FireTruck }, l2!.ToArray());                // Level 2 chưa cấu hình → dùng Level 1
        Assert.Equal(new[] { RescueUnitType.FireTruck, RescueUnitType.Ambulance }.OrderBy(x => x), l3!.OrderBy(x => x));
        Assert.Equal(1, DispatchScoring.PlannedUnitCount(rules, SeverityLevel.Level2));
        Assert.Equal(3, DispatchScoring.PlannedUnitCount(rules, SeverityLevel.Level3));
        Assert.Null(DispatchScoring.PlannedUnitCount(Array.Empty<IncidentTypeUnitRule>(), SeverityLevel.Level3));
    }

    [Fact]
    public void MatchingUnitTypes_NoRules_FallsBackToLegacyKeywords()
    {
        var result = DispatchScoring.MatchingUnitTypes(Array.Empty<IncidentTypeUnitRule>(), IncidentEventType.Other, Array.Empty<string>(), "Người bị đuối nước", SeverityLevel.Level2);

        Assert.Equal(new[] { RescueUnitType.RescueBoat }, result!.ToArray());
    }

    [Fact]
    public void ApplicableRules_NoneAtSeverity_UsesLowestTier()
    {
        var rules = new List<IncidentTypeUnitRule> { Rule(RescueUnitType.RescueBoat, SeverityLevel.Level2) };

        Assert.Single(DispatchScoring.ApplicableRules(rules, SeverityLevel.Level1));
    }

    [Fact]
    public void IncidentTypeCodeFor_KeepsReportedDetailedTypeOnlyWhenCategoryMatches()
    {
        var drowning = new Incident { ReportedEventType = IncidentEventType.Other, ReportedIncidentTypeCode = "FloodDrowning" };
        var reportedOtherButFire = new Incident
        {
            ReportedEventType = IncidentEventType.Other, ReportedIncidentTypeCode = "FloodDrowning", EventType = IncidentEventType.FireExplosion
        };
        var confirmed = new Incident { IncidentTypeCode = "ForestFire", EventType = IncidentEventType.FireExplosion };

        Assert.Equal("FloodDrowning", DispatchScoring.IncidentTypeCodeFor(drowning));
        Assert.Equal("FireExplosion", DispatchScoring.IncidentTypeCodeFor(reportedOtherButFire));
        Assert.Equal("ForestFire", DispatchScoring.IncidentTypeCodeFor(confirmed));
        Assert.Null(DispatchScoring.IncidentTypeCodeFor(new Incident()));
    }

    [Fact]
    public void Linking_OtherIncidentsWithDifferentDetailedTypes_AreNotMerged()
    {
        var drowning = new Incident { Status = IncidentStatus.AiProcessed, ReportedEventType = IncidentEventType.Other, ReportedIncidentTypeCode = "FloodDrowning", Latitude = 10.7, Longitude = 106.7, CreatedAt = DateTime.UtcNow };
        var window = DateTime.UtcNow.AddMinutes(-15);

        var collapse = IncidentLinking.PickParent(new[] { drowning }, 10.7, 106.7, IncidentEventType.Other, 200, window, "BuildingCollapse");
        var sameType = IncidentLinking.PickParent(new[] { drowning }, 10.7, 106.7, IncidentEventType.Other, 200, window, "FloodDrowning");
        var generic = IncidentLinking.PickParent(new[] { drowning }, 10.7, 106.7, IncidentEventType.Other, 200, window, "Other");

        Assert.Null(collapse);
        Assert.NotNull(sameType);
        Assert.NotNull(generic);
    }
}

/// <summary>
/// AI tự động điều phối: điều kiện, chống trùng, chọn xe theo quy tắc + BR-14
/// </summary>
public class AutoDispatchServiceTests
{
    private readonly Mock<IIncidentRepository> _incidents = new();
    private readonly Mock<IDispatchService> _dispatch = new();
    private readonly Mock<ISystemSettingsService> _settings = new();
    private readonly Mock<INotificationService> _notify = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IRealtimeNotifier> _rt = new();
    private readonly DispatchSettingsDto _dispatchSettings = new() { AutoDispatchEnabled = true, AutoDispatchMinConfidence = 0.9 };
    private readonly List<Incident> _neighbours = new();
    private readonly AutoDispatchService _svc;

    public AutoDispatchServiceTests()
    {
        _settings.Setup(s => s.GetDispatchSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _dispatchSettings);
        _incidents.Setup(i => i.FindLinkCandidatesAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _neighbours);
        _users.Setup(u => u.GetActiveByRolesAsync(It.IsAny<IReadOnlyCollection<UserRole>>())).ReturnsAsync(new List<User>());
        _dispatch.Setup(d => d.GetUnitRulesAsync(It.IsAny<Incident>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IncidentTypeUnitRule>());
        _dispatch.Setup(d => d.AutoAssignAsync(It.IsAny<AssignDispatchDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponseDto<List<DispatchAssignmentResponseDto>>.SuccessResult(new List<DispatchAssignmentResponseDto>()));
        _svc = new AutoDispatchService(_incidents.Object, _dispatch.Object, _settings.Object, _notify.Object, _users.Object, _rt.Object,
            NullLogger<AutoDispatchService>.Instance);
    }

    private Incident Fire(double confidence = 0.95, string source = "logprob", SeverityLevel severity = SeverityLevel.Level2, Action<Incident>? tweak = null)
    {
        var inc = new Incident
        {
            Title = "Cháy", Latitude = 10.7335, Longitude = 106.7192, Status = IncidentStatus.AiProcessed, Severity = severity,
            EventType = IncidentEventType.FireExplosion, CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            AiClassification = new AiClassification
            {
                IsSuccess = true, EventType = IncidentEventType.FireExplosion, SeverityScore = severity,
                ConfidenceScore = confidence, ConfidenceSource = source, ModelName = "Qwen2.5-VL-3B-LoRA (Local GPU)"
            }
        };
        tweak?.Invoke(inc);
        _incidents.Setup(i => i.GetIncidentWithDetailsAsync(inc.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inc);
        return inc;
    }

    private void Recommend(string? unitType, params Guid[] unitIds)
        => _dispatch.Setup(d => d.GetRecommendationsAsync(It.IsAny<Guid>(), DispatchAlgorithm.NearestAvailable, unitType, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponseDto<List<RecommendedUnitResponseDto>>.SuccessResult(
                unitIds.Select(id => new RecommendedUnitResponseDto { RescueUnitId = id, UnitType = unitType ?? RescueUnitType.FireTruck }).ToList()));

    [Theory]
    [InlineData(0.89, "logprob", "below-threshold")]
    [InlineData(0.99, "heuristic", "confidence-not-measured")]
    [InlineData(0.99, "self-reported", "confidence-not-measured")]
    [InlineData(0.99, "logprob", null)]
    [InlineData(0.99, "logprob-consensus", null)]
    public void IneligibleReason_RequiresMeasuredConfidenceAboveThreshold(double confidence, string source, string? expected)
    {
        Assert.Equal(expected, AutoDispatchService.IneligibleReason(Fire(confidence, source), _dispatchSettings, DateTime.UtcNow));
    }

    [Fact]
    public void IneligibleReason_OtherTypesLinkedOrOperatorHandled_AreSkipped()
    {
        var now = DateTime.UtcNow;
        Assert.Equal("reporter-chose-other", AutoDispatchService.IneligibleReason(Fire(tweak: i => i.ReportedEventType = IncidentEventType.Other), _dispatchSettings, now));
        Assert.Equal("type-not-ai-recognizable", AutoDispatchService.IneligibleReason(Fire(tweak: i => i.EventType = IncidentEventType.Other), _dispatchSettings, now));
        Assert.Equal("unclassified", AutoDispatchService.IneligibleReason(Fire(severity: SeverityLevel.Unclassified), _dispatchSettings, now));
        Assert.Equal("linked-report", AutoDispatchService.IneligibleReason(Fire(tweak: i => i.DuplicateOfIncidentId = Guid.NewGuid()), _dispatchSettings, now));
        Assert.Equal("out-of-service-area", AutoDispatchService.IneligibleReason(Fire(tweak: i => i.IsOutOfServiceArea = true), _dispatchSettings, now));
        Assert.Equal("operator-handling", AutoDispatchService.IneligibleReason(Fire(tweak: i => { i.LockedByOperatorId = Guid.NewGuid(); i.LockExpiresAt = now.AddMinutes(1); }), _dispatchSettings, now));
        Assert.Equal("not-awaiting-verification", AutoDispatchService.IneligibleReason(Fire(tweak: i => i.Status = IncidentStatus.Verified), _dispatchSettings, now));
    }

    [Fact]
    public async Task Disabled_DoesNothing()
    {
        _dispatchSettings.AutoDispatchEnabled = false;
        var inc = Fire();

        Assert.Equal(AutoDispatchOutcome.Disabled, await _svc.TryAutoDispatchAsync(inc.Id));
        Assert.Equal(IncidentStatus.AiProcessed, inc.Status);
        _dispatch.Verify(d => d.AutoAssignAsync(It.IsAny<AssignDispatchDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Eligible_VerifiesAsSystem_DispatchesNearestUnit_AndAnnounces()
    {
        var inc = Fire();
        var nearest = Guid.NewGuid();
        Recommend(null, nearest, Guid.NewGuid());
        AssignDispatchDto? sent = null;
        _dispatch.Setup(d => d.AutoAssignAsync(It.IsAny<AssignDispatchDto>(), It.IsAny<CancellationToken>()))
            .Callback<AssignDispatchDto, CancellationToken>((dto, _) => sent = dto)
            .ReturnsAsync(ApiResponseDto<List<DispatchAssignmentResponseDto>>.SuccessResult(new List<DispatchAssignmentResponseDto>()));

        var outcome = await _svc.TryAutoDispatchAsync(inc.Id);

        Assert.Equal(AutoDispatchOutcome.Dispatched, outcome);
        Assert.Equal(new[] { nearest }, sent!.GetUnitIds());
        Assert.Equal(IncidentStatus.Verified, inc.Status);
        Assert.NotNull(inc.VerifiedAt);
        Assert.Null(inc.VerifiedByUserId);
        Assert.Equal(AutoDispatchState.Dispatched, inc.AutoDispatchState);
        _incidents.Verify(i => i.BeginAreaLockAsync(inc.Latitude, inc.Longitude, _dispatchSettings.AutoDispatchDuplicateRadiusMeters, It.IsAny<CancellationToken>()), Times.Once);
        _incidents.Verify(i => i.AddAuditTrailAsync(It.Is<IncidentAuditTrail>(a => a.Action == AuditAction.AutoDispatched), It.IsAny<CancellationToken>()), Times.Once);
        _rt.Verify(r => r.AutoDispatchResultAsync(It.Is<AutoDispatchEventDto>(e => e.State == AutoDispatchState.Dispatched && e.IncidentId == inc.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NeighbourAlreadyDispatched_SkipsAsSuspectedDuplicate_IgnoringType()
    {
        var inc = Fire();
        Recommend(null, Guid.NewGuid());
        // Sự cố gần đó (~60 m) đã điều xe, khác loại (tai nạn) → vẫn nghi trùng
        var neighbour = new Incident { Status = IncidentStatus.Dispatched, EventType = IncidentEventType.TrafficAccident, Latitude = inc.Latitude + 0.0005, Longitude = inc.Longitude, CreatedAt = DateTime.UtcNow };
        _neighbours.Add(neighbour);

        var outcome = await _svc.TryAutoDispatchAsync(inc.Id);

        Assert.Equal(AutoDispatchOutcome.SkippedDuplicate, outcome);
        Assert.Equal(AutoDispatchState.Skipped, inc.AutoDispatchState);
        Assert.Equal(neighbour.Id, inc.SuspectedDuplicateOfIncidentId);
        Assert.Equal(IncidentStatus.AiProcessed, inc.Status);
        _dispatch.Verify(d => d.AutoAssignAsync(It.IsAny<AssignDispatchDto>(), It.IsAny<CancellationToken>()), Times.Never);
        _incidents.Verify(i => i.AddAuditTrailAsync(It.Is<IncidentAuditTrail>(a => a.Action == AuditAction.AutoDispatchSkipped), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoUnitsAvailable_SkipsAndHandsToOperator()
    {
        var inc = Fire();
        Recommend(null);

        Assert.Equal(AutoDispatchOutcome.SkippedNoUnits, await _svc.TryAutoDispatchAsync(inc.Id));
        Assert.Equal(AutoDispatchState.Skipped, inc.AutoDispatchState);
        Assert.Equal(IncidentStatus.AiProcessed, inc.Status);
    }

    [Fact]
    public async Task Level2_FollowsAdminPlan_MarksPartialWhenShortOfUnits()
    {
        // Kế hoạch Level 2: 1 xe thang + 2 xe chữa cháy (3 xe). Hết xe thang → điều 2 xe chữa cháy, ghi chú thiếu xe
        var inc = Fire(severity: SeverityLevel.Level2);
        _dispatch.Setup(d => d.GetUnitRulesAsync(inc, It.IsAny<CancellationToken>())).ReturnsAsync(new List<IncidentTypeUnitRule>
        {
            new() { UnitTypeCode = RescueUnitType.FireTruck, Severity = SeverityLevel.Level1, Quantity = 1, Priority = 1 },
            new() { UnitTypeCode = RescueUnitType.LadderTruck, Severity = SeverityLevel.Level2, Quantity = 1, Priority = 1 },
            new() { UnitTypeCode = RescueUnitType.FireTruck, Severity = SeverityLevel.Level2, Quantity = 2, Priority = 2 }
        });
        Recommend(RescueUnitType.LadderTruck);
        var trucks = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        Recommend(RescueUnitType.FireTruck, trucks);
        AssignDispatchDto? sent = null;
        _dispatch.Setup(d => d.AutoAssignAsync(It.IsAny<AssignDispatchDto>(), It.IsAny<CancellationToken>()))
            .Callback<AssignDispatchDto, CancellationToken>((dto, _) => sent = dto)
            .ReturnsAsync(ApiResponseDto<List<DispatchAssignmentResponseDto>>.SuccessResult(new List<DispatchAssignmentResponseDto>()));

        Assert.Equal(AutoDispatchOutcome.Dispatched, await _svc.TryAutoDispatchAsync(inc.Id));

        Assert.Equal(trucks.Take(2), sent!.GetUnitIds());
        Assert.Equal(AutoDispatchService.PartialNote, inc.AutoDispatchNote);
    }

    [Fact]
    public async Task Level1_PlanOfOneUnit_DispatchesExactlyOne()
    {
        var inc = Fire(severity: SeverityLevel.Level1);
        _dispatch.Setup(d => d.GetUnitRulesAsync(inc, It.IsAny<CancellationToken>())).ReturnsAsync(new List<IncidentTypeUnitRule>
        {
            new() { UnitTypeCode = RescueUnitType.FireTruck, Severity = SeverityLevel.Level1, Quantity = 1, Priority = 1 },
            new() { UnitTypeCode = RescueUnitType.Ambulance, Severity = SeverityLevel.Level3, Quantity = 1, Priority = 2 }
        });
        var nearest = Guid.NewGuid();
        Recommend(RescueUnitType.FireTruck, nearest, Guid.NewGuid());
        AssignDispatchDto? sent = null;
        _dispatch.Setup(d => d.AutoAssignAsync(It.IsAny<AssignDispatchDto>(), It.IsAny<CancellationToken>()))
            .Callback<AssignDispatchDto, CancellationToken>((dto, _) => sent = dto)
            .ReturnsAsync(ApiResponseDto<List<DispatchAssignmentResponseDto>>.SuccessResult(new List<DispatchAssignmentResponseDto>()));

        await _svc.TryAutoDispatchAsync(inc.Id);

        Assert.Equal(new[] { nearest }, sent!.GetUnitIds());
        Assert.Equal(AutoDispatchService.DispatchedNote, inc.AutoDispatchNote);
    }

    [Fact]
    public async Task Level3_PicksQuantityPerRule_CappedAtFive()
    {
        var inc = Fire(severity: SeverityLevel.Level3);
        _dispatch.Setup(d => d.GetUnitRulesAsync(inc, It.IsAny<CancellationToken>())).ReturnsAsync(new List<IncidentTypeUnitRule>
        {
            new() { UnitTypeCode = RescueUnitType.FireTruck, Severity = SeverityLevel.Level3, Quantity = 4, Priority = 1 },
            new() { UnitTypeCode = RescueUnitType.Ambulance, Severity = SeverityLevel.Level3, Quantity = 3, Priority = 2 }
        });
        var trucks = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var ambulances = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        Recommend(RescueUnitType.FireTruck, trucks);
        Recommend(RescueUnitType.Ambulance, ambulances);
        AssignDispatchDto? sent = null;
        _dispatch.Setup(d => d.AutoAssignAsync(It.IsAny<AssignDispatchDto>(), It.IsAny<CancellationToken>()))
            .Callback<AssignDispatchDto, CancellationToken>((dto, _) => sent = dto)
            .ReturnsAsync(ApiResponseDto<List<DispatchAssignmentResponseDto>>.SuccessResult(new List<DispatchAssignmentResponseDto>()));

        await _svc.TryAutoDispatchAsync(inc.Id);

        Assert.Equal(trucks.Take(4).Append(ambulances[0]), sent!.GetUnitIds());
    }

    [Fact]
    public async Task AssignFails_RollsBackAndMarksSkipped()
    {
        var inc = Fire();
        Recommend(null, Guid.NewGuid());
        _dispatch.Setup(d => d.AutoAssignAsync(It.IsAny<AssignDispatchDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponseDto<List<DispatchAssignmentResponseDto>>.ConflictResult("Xe vừa được điều đi"));
        var fresh = new Incident { Status = IncidentStatus.AiProcessed, Title = "Cháy" };
        _incidents.SetupSequence(i => i.GetIncidentWithDetailsAsync(inc.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inc)
            .ReturnsAsync(fresh);

        var outcome = await _svc.TryAutoDispatchAsync(inc.Id);

        Assert.Equal(AutoDispatchOutcome.Failed, outcome);
        _incidents.Verify(i => i.ClearChangeTracker(), Times.Once);
        Assert.Equal(AutoDispatchState.Skipped, fresh.AutoDispatchState);
    }
}

/// <summary>
/// Tạo SOS với loại sự cố chi tiết trong danh mục
/// </summary>
public class IncidentTypeOnCreateTests
{
    [Fact]
    public async Task Create_WithCatalogType_StoresDetailedCodeAndCategory_UnknownIsRejected()
    {
        var incidents = new Mock<IIncidentRepository>();
        var stations = new Mock<IStationRepository>();
        var settings = new Mock<ISystemSettingsService>();
        var catalog = new Mock<ITypeCatalogRepository>();
        var station = new Station { Name = "Q7", Latitude = 10.73, Longitude = 106.72, CoverageRadiusKm = 10, IsActive = true };
        stations.Setup(s => s.GetAllAsync(It.IsAny<Expression<Func<Station, bool>>?>(), It.IsAny<Func<IQueryable<Station>, IQueryable<Station>>?>()))
            .ReturnsAsync(new[] { station });
        settings.Setup(s => s.GetServiceAreaAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new ServiceAreaSettingsDto { Enabled = false });
        Incident? saved = null;
        incidents.Setup(i => i.AddAsync(It.IsAny<Incident>())).Callback<Incident>(i => saved = i).ReturnsAsync((Incident i) => i);
        catalog.Setup(c => c.GetIncidentTypeAsync("FloodDrowning", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IncidentTypeDefinition { Code = "FloodDrowning", Category = IncidentEventType.Other, IsActive = true });
        var cfg = TestHelpers.Config(("Cloudinary:CloudName", "demo"));
        var svc = new IncidentService(incidents.Object, new Mock<IUserRepository>().Object, new Mock<IBlockedPhoneRepository>().Object,
            new Mock<IAiClassificationService>().Object, new Mock<IAiAnalysisQueue>().Object, new Mock<IRealtimeNotifier>().Object,
            new Mock<INotificationService>().Object, new CreateIncidentDtoValidator(cfg), new CreateOperatorIncidentDtoValidator(cfg),
            NullLogger<IncidentService>.Instance, new Mock<IGuestCancellationTokenService>().Object, stations.Object, settings.Object,
            new Mock<ICurrentUserContext>().Object, new Mock<IIncidentFollowUpService>().Object, catalog.Object);
        CreateIncidentDto Dto(string code) => new()
        {
            Description = "Có người đuối nước", Latitude = 10.7335, Longitude = 106.7192, LocationAddress = "Kênh Tẻ", IncidentTypeCode = code
        };

        var ok = await svc.CreateIncidentAsync(Dto("FloodDrowning"));
        var bad = await svc.CreateIncidentAsync(Dto("NoSuchType"));

        Assert.True(ok.Success);
        Assert.Equal("FloodDrowning", saved!.ReportedIncidentTypeCode);
        Assert.Equal(IncidentEventType.Other, saved.ReportedEventType);
        Assert.Equal("FloodDrowning", ok.Data!.IncidentTypeCode);
        Assert.False(bad.Success);
    }
}
