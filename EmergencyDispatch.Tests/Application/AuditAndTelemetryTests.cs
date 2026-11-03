using EmergencyDispatch.Application.DTOs.Audit;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EmergencyDispatch.Tests.Application;

public class SystemAuditServiceTests
{
    private readonly Mock<ISystemAuditLogRepository> _repo = new();
    private readonly Mock<ICurrentUserContext> _me = new();
    private readonly SystemAuditService _svc;

    public SystemAuditServiceTests()
    {
        _svc = new SystemAuditService(_repo.Object, _me.Object, NullLogger<SystemAuditService>.Instance);
    }

    [Fact]
    public async Task Log_RecordsActorAndSerializesValuesAsCamelCaseWithEnumNames()
    {
        var actor = Guid.NewGuid();
        _me.SetupGet(m => m.UserId).Returns(actor);
        _me.SetupGet(m => m.FullName).Returns("Admin A");
        _me.SetupGet(m => m.Role).Returns(UserRole.Admin);
        _me.SetupGet(m => m.IpAddress).Returns("1.2.3.4");
        SystemAuditLog? saved = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<SystemAuditLog>(), It.IsAny<CancellationToken>())).Callback<SystemAuditLog, CancellationToken>((l, _) => saved = l);

        await _svc.LogAsync(SystemAuditAction.Updated, AuditEntityTypes.Station, Guid.NewGuid(), "Q7", "desc",
            new { Status = RescueUnitStatus.Available }, new { Status = RescueUnitStatus.Maintenance });

        Assert.Equal(actor, saved!.ActorId);
        Assert.Equal("Admin A", saved.ActorName);
        Assert.Equal("Admin", saved.ActorRole);
        Assert.Equal("1.2.3.4", saved.IpAddress);
        Assert.Equal("{\"status\":\"Available\"}", saved.OldValueJson);
        Assert.Equal("{\"status\":\"Maintenance\"}", saved.NewValueJson);
    }

    [Fact]
    public async Task Log_WithoutUser_UsesSystem_AndTruncatesDescription()
    {
        SystemAuditLog? saved = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<SystemAuditLog>(), It.IsAny<CancellationToken>())).Callback<SystemAuditLog, CancellationToken>((l, _) => saved = l);

        await _svc.LogAsync(SystemAuditAction.Created, "X", null, null, new string('d', 1500));

        Assert.Equal("System", saved!.ActorName);
        Assert.Equal("System", saved.ActorRole);
        Assert.Equal(1000, saved.Description.Length);
        Assert.Null(saved.OldValueJson);
    }

    [Fact]
    public async Task Log_RepositoryFailure_NeverBreaksTheBusinessOperation()
    {
        _repo.Setup(r => r.AddAsync(It.IsAny<SystemAuditLog>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db"));

        Assert.Null(await Record.ExceptionAsync(() => _svc.LogAsync(SystemAuditAction.Created, "X", null, null, "d")));
    }

    [Fact]
    public async Task GetPaged_FromAfterTo_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.GetPagedAsync(new SystemAuditLogFilterDto { From = DateTime.UtcNow, To = DateTime.UtcNow.AddDays(-1) }));
    }

    [Fact]
    public async Task GetPaged_NormalizesPaging()
    {
        _repo.Setup(r => r.GetPagedAsync(null, null, null, null, null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<SystemAuditLog>(), 0));

        var page = await _svc.GetPagedAsync(new SystemAuditLogFilterDto { PageIndex = -3, PageSize = 999 });

        Assert.Equal(1, page.PageIndex);
        Assert.Equal(100, page.PageSize);
    }
}

public class TelemetryServiceTests
{
    [Fact]
    public void CohensKappa_PerfectAgreement_IsOne()
    {
        var m = new[] { new[] { 5, 0 }, new[] { 0, 5 } };

        Assert.Equal(1.0, TelemetryService.CohensKappa(m));
    }

    [Fact]
    public void CohensKappa_KnownExample()
    {
        // po = 0.7, pe = 0.5 -> kappa = 0.4
        var m = new[] { new[] { 20, 5 }, new[] { 10, 15 } };

        Assert.Equal(0.4, TelemetryService.CohensKappa(m));
    }

    [Fact]
    public void CohensKappa_EmptyMatrix_IsNull()
    {
        Assert.Null(TelemetryService.CohensKappa(new[] { new[] { 0, 0 }, new[] { 0, 0 } }));
    }

    [Fact]
    public void CohensKappa_SingleCategoryOnly_IsOne()
    {
        Assert.Equal(1.0, TelemetryService.CohensKappa(new[] { new[] { 4, 0 }, new[] { 0, 0 } }));
    }

    [Fact]
    public void Stats_ComputesPercentilesAndIgnoresNegativeValues()
    {
        var s = TelemetryService.Stats(new List<double> { 40, 10, -5, 30, 20, 50 });

        Assert.Equal(5, s.Count);
        Assert.Equal(30, s.AvgMs);
        Assert.Equal(30, s.MedianMs);
        Assert.Equal(48, s.P95Ms);
        Assert.Equal(10, s.MinMs);
        Assert.Equal(50, s.MaxMs);
    }

    [Fact]
    public void Stats_Empty_HasNoValues()
    {
        var s = TelemetryService.Stats(new List<double>());

        Assert.Equal(0, s.Count);
        Assert.Null(s.AvgMs);
    }

    private static Incident Inc(SeverityLevel aiSev, SeverityLevel opSev, int aiSec = 10, int verifySec = 40, bool aiOk = true, IncidentSource src = IncidentSource.CitizenApp)
    {
        var t0 = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);
        var inc = new Incident { CreatedAt = t0, Severity = opSev, VerifiedAt = t0.AddSeconds(verifySec), Source = src };
        inc.AiClassification = new AiClassification { CreatedAt = t0.AddSeconds(aiSec), SeverityScore = aiSev, IsSuccess = aiOk, ProcessingDurationMs = 4000 };
        return inc;
    }

    [Fact]
    public void BuildSummary_AgreementAccuracyAndOverrideRate()
    {
        var list = new List<Incident>
        {
            Inc(SeverityLevel.Level2, SeverityLevel.Level2),
            Inc(SeverityLevel.Level3, SeverityLevel.Level3),
            Inc(SeverityLevel.Level1, SeverityLevel.Level2),
            Inc(SeverityLevel.Level2, SeverityLevel.Level2, aiOk: false),   // failed AI: not counted in agreement
        };

        var s = TelemetryService.BuildSummary(list, null, null);

        Assert.Equal(4, s.IncidentCount);
        Assert.Equal(3, s.AiAgreement.SampleCount);
        Assert.Equal(0.6667, s.AiAgreement.Accuracy);
        Assert.Equal(0.3333, s.AiAgreement.OverrideRate);
        Assert.Equal(1, s.AiAgreement.ConfusionMatrix[1][2]);
        Assert.Equal(3, s.TOperatorWithAi.Count);
        Assert.Equal(1, s.TOperatorManual.Count);
        Assert.Equal(30_000, s.TOperatorWithAi.MedianMs);   // verify(40s) - AI result(10s)
        Assert.Equal(10_000, s.TAi.MedianMs);
    }

    [Fact]
    public void BuildSummary_OperatorCreatedIncidents_AreExcludedFromOperatorTime()
    {
        var s = TelemetryService.BuildSummary(new List<Incident> { Inc(SeverityLevel.Level2, SeverityLevel.Level2, src: IncidentSource.PhoneCall) }, null, null);

        Assert.Equal(0, s.TOperator.Count);
        Assert.Equal(0, s.AiAgreement.SampleCount);
    }

    [Fact]
    public void BuildSummary_ResponseTimeGroupedByAlgorithm_IgnoresRejectedAndCancelled()
    {
        var inc = Inc(SeverityLevel.Level2, SeverityLevel.Level2);
        var t = inc.VerifiedAt!.Value;
        inc.DispatchAssignments.Add(new DispatchAssignment { DispatchedAt = t.AddSeconds(20), ArrivedAt = t.AddMinutes(6), DispatchAlgorithm = "LoadAware", Status = DispatchAssignmentStatus.Completed });
        inc.DispatchAssignments.Add(new DispatchAssignment { DispatchedAt = t.AddSeconds(5), ArrivedAt = t.AddMinutes(1), Status = DispatchAssignmentStatus.Rejected });

        var s = TelemetryService.BuildSummary(new List<Incident> { inc }, null, null);

        Assert.Equal(20_000, s.TDispatch.MedianMs);
        Assert.Equal(1, s.TResponse.Count);
        Assert.True(s.TResponseByAlgorithm.ContainsKey("LoadAware"));
    }

    [Fact]
    public void ToRow_DetectsOverride_AndComputesTimings()
    {
        var inc = Inc(SeverityLevel.Level1, SeverityLevel.Level3);
        inc.ClosedAt = inc.CreatedAt.AddMinutes(30);

        var row = TelemetryService.ToRow(inc);

        Assert.True(row.WasAiOverridden);
        Assert.Equal(10_000, row.TAiMs);
        Assert.Equal(30_000, row.TOperatorMs);
        Assert.Equal(30 * 60_000, row.TResolutionMs);
        Assert.Equal(0, row.UnitsDispatched);
    }
}
