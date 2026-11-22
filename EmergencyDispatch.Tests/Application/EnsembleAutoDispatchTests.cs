using EmergencyDispatch.Application.DTOs.Ai;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Dispatch;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.DTOs.Settings;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using EmergencyDispatch.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EmergencyDispatch.Tests.Application;

/// <summary>
/// AI tự điều phối, chế độ "3 AI bỏ phiếu" (TriageVL-3B + ResNet-50 + YOLO11n-cls): TriageVL đạt ngưỡng và ít nhất một
/// CNN cùng loại sự cố, đạt ngưỡng; mức điều phối = mức cao nhất trong nhóm đa số
/// </summary>
public class EnsembleAutoDispatchTests
{
    private const string Photo = "https://res.cloudinary.com/demo/fire.jpg";

    private readonly Mock<IIncidentRepository> _incidents = new();
    private readonly Mock<IDispatchService> _dispatch = new();
    private readonly Mock<ISystemSettingsService> _settings = new();
    private readonly Mock<INotificationService> _notify = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IRealtimeNotifier> _rt = new();
    private readonly Mock<ICnnTriageService> _cnn = new();
    private readonly DispatchSettingsDto _dispatchSettings = new() { AutoDispatchEnabled = true, AutoDispatchMinConfidence = 0.9 };
    private readonly List<Incident> _neighbours = new();
    private readonly AutoDispatchService _svc;

    public EnsembleAutoDispatchTests()
    {
        _settings.Setup(s => s.GetDispatchSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _dispatchSettings);
        _incidents.Setup(i => i.FindLinkCandidatesAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _neighbours);
        _users.Setup(u => u.GetActiveByRolesAsync(It.IsAny<IReadOnlyCollection<UserRole>>())).ReturnsAsync(new List<User>());
        _dispatch.Setup(d => d.GetUnitRulesAsync(It.IsAny<Incident>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IncidentTypeUnitRule>());
        _dispatch.Setup(d => d.GetRecommendationsAsync(It.IsAny<Guid>(), DispatchAlgorithm.NearestAvailable, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponseDto<List<RecommendedUnitResponseDto>>.SuccessResult(new List<RecommendedUnitResponseDto>
            {
                new() { RescueUnitId = Guid.NewGuid(), StationTeamId = Guid.NewGuid(), UnitType = RescueUnitType.FireTruck }
            }));
        _dispatch.Setup(d => d.AutoAssignAsync(It.IsAny<AssignDispatchDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponseDto<List<DispatchAssignmentResponseDto>>.SuccessResult(new List<DispatchAssignmentResponseDto>()));
        _svc = new AutoDispatchService(_incidents.Object, _dispatch.Object, _settings.Object, _notify.Object, _users.Object, _rt.Object,
            _cnn.Object, NullLogger<AutoDispatchService>.Instance);
    }

    private Incident Fire(SeverityLevel severity = SeverityLevel.Level2, bool withPhoto = true)
    {
        var inc = new Incident
        {
            Title = "Cháy", Latitude = 10.7335, Longitude = 106.7192, Status = IncidentStatus.AiProcessed, Severity = severity,
            EventType = IncidentEventType.FireExplosion, CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            AiClassification = new AiClassification
            {
                IsSuccess = true, EventType = IncidentEventType.FireExplosion, SeverityScore = severity,
                ConfidenceScore = 0.96, ConfidenceSource = "logprob", ModelName = "Qwen2.5-VL-3B-LoRA (Local GPU)"
            }
        };
        if (withPhoto) inc.MediaItems.Add(new IncidentMedia { MediaUrl = Photo, MediaType = MediaType.Photo });
        _incidents.Setup(i => i.GetIncidentWithDetailsAsync(inc.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inc);
        return inc;
    }

    private static AiModelVoteDto Vote(string model, string? type, int severity, double? confidence, bool available = true)
        => new() { Model = model, IncidentType = type, Severity = severity, Confidence = confidence, ConfidenceSource = "softmax", IsAvailable = available };

    private void CnnReturns(params AiModelVoteDto[] votes)
        => _cnn.Setup(c => c.ClassifyAsync(Photo, It.IsAny<CancellationToken>())).ReturnsAsync(votes);

    // ---------- Luật bỏ phiếu (hàm thuần) ----------

    [Fact]
    public void Verdict_OneCnnAgreesAboveThreshold_PassesWithHighestLevelOfMajority()
    {
        var inc = Fire(SeverityLevel.Level2);
        var (reason, severity, votes) = AutoDispatchService.EnsembleVerdict(inc, new[]
        {
            Vote("ResNet-50", "fire", 3, 0.97),
            Vote("YOLO11n-cls", "accident", 1, 0.99) // khác loại → không thuộc đa số, mức của nó không được tính
        }, 0.9);

        Assert.Null(reason);
        Assert.Equal(SeverityLevel.Level3, severity);
        Assert.Equal(new[] { true, true, false }, votes.Select(v => v.InMajority));
        Assert.Equal("TriageVL-3B", votes[0].Model);
    }

    [Fact]
    public void Verdict_CnnSameTypeButBelowThreshold_IsNoMajority()
    {
        var (reason, severity, _) = AutoDispatchService.EnsembleVerdict(Fire(), new[]
        {
            Vote("ResNet-50", "fire", 2, 0.89),
            Vote("YOLO11n-cls", "none", 0, 0.99)
        }, 0.9);

        Assert.Equal("ensemble-no-majority", reason);
        Assert.Equal(SeverityLevel.Level2, severity);
    }

    [Fact]
    public void Verdict_BothCnnUnavailable_IsUnavailable()
    {
        var (reason, _, votes) = AutoDispatchService.EnsembleVerdict(Fire(), new[]
        {
            Vote("ResNet-50", null, 0, null, available: false),
            Vote("YOLO11n-cls", null, 0, null, available: false)
        }, 0.9);

        Assert.Equal("ensemble-unavailable", reason);
        Assert.Equal(3, votes.Count);
    }

    // ---------- Luồng tự điều phối ----------

    [Fact]
    public async Task Ensemble_Majority_DispatchesAtHighestLevel_AndStoresVotes()
    {
        var inc = Fire(SeverityLevel.Level2);
        CnnReturns(Vote("ResNet-50", "fire", 3, 0.95), Vote("YOLO11n-cls", "fire", 2, 0.93));

        Assert.Equal(AutoDispatchOutcome.Dispatched, await _svc.TryAutoDispatchAsync(inc.Id));

        Assert.Equal(SeverityLevel.Level3, inc.Severity);
        Assert.Equal(IncidentStatus.Verified, inc.Status);
        var stored = AutoDispatchService.ParseVotes(inc.AiClassification!.EnsembleVotesJson)!;
        Assert.Equal(3, stored.Count);
        Assert.All(stored, v => Assert.True(v.InMajority));
        _incidents.Verify(i => i.AddStatusHistoryAsync(It.Is<IncidentStatusHistory>(h => h.Notes!.Contains("3 AI bỏ phiếu") && h.Notes.Contains("nâng từ Level2")),
            It.IsAny<CancellationToken>()), Times.Once);
        _rt.Verify(r => r.AutoDispatchResultAsync(It.Is<AutoDispatchEventDto>(e => e.State == AutoDispatchState.Dispatched && e.Votes!.Count == 3),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Ensemble_NoCnnAgrees_IsNotEligible_KeepsLevelAndStoresVotes()
    {
        var inc = Fire(SeverityLevel.Level2);
        CnnReturns(Vote("ResNet-50", "accident", 2, 0.97), Vote("YOLO11n-cls", "fire", 2, 0.71));

        Assert.Equal(AutoDispatchOutcome.NotEligible, await _svc.TryAutoDispatchAsync(inc.Id));

        Assert.Equal("ensemble-no-majority", inc.AutoDispatchReasonCode);
        Assert.Equal(AutoDispatchState.NotEligible, inc.AutoDispatchState);
        Assert.Equal(SeverityLevel.Level2, inc.Severity);
        Assert.Equal(IncidentStatus.AiProcessed, inc.Status);
        Assert.NotNull(inc.AiClassification!.EnsembleVotesJson);
        _dispatch.Verify(d => d.AutoAssignAsync(It.IsAny<AssignDispatchDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Ensemble_CnnServiceThrows_IsUnavailable_NotDispatched()
    {
        var inc = Fire();
        _cnn.Setup(c => c.ClassifyAsync(Photo, It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("down"));

        Assert.Equal(AutoDispatchOutcome.NotEligible, await _svc.TryAutoDispatchAsync(inc.Id));
        Assert.Equal("ensemble-unavailable", inc.AutoDispatchReasonCode);
        _dispatch.Verify(d => d.AutoAssignAsync(It.IsAny<AssignDispatchDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Ensemble_NoPhoto_IsNotEligible_WithoutCallingCnn()
    {
        var inc = Fire(withPhoto: false);

        Assert.Equal(AutoDispatchOutcome.NotEligible, await _svc.TryAutoDispatchAsync(inc.Id));
        Assert.Equal("ensemble-no-photo", inc.AutoDispatchReasonCode);
        _cnn.Verify(c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Ensemble_TriageVlBelowThreshold_DoesNotCallCnn()
    {
        var inc = Fire();
        inc.AiClassification!.ConfidenceScore = 0.80;

        Assert.Equal(AutoDispatchOutcome.NotEligible, await _svc.TryAutoDispatchAsync(inc.Id));
        Assert.Equal("below-threshold", inc.AutoDispatchReasonCode);
        _cnn.Verify(c => c.ClassifyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Ensemble_UpgradedButSuspectedDuplicate_RevertsLevel()
    {
        var inc = Fire(SeverityLevel.Level1);
        CnnReturns(Vote("ResNet-50", "fire", 3, 0.99), Vote("YOLO11n-cls", "fire", 1, 0.95));
        _neighbours.Add(new Incident { Status = IncidentStatus.Dispatched, Latitude = inc.Latitude + 0.0005, Longitude = inc.Longitude, CreatedAt = DateTime.UtcNow });

        Assert.Equal(AutoDispatchOutcome.SkippedDuplicate, await _svc.TryAutoDispatchAsync(inc.Id));
        Assert.Equal(SeverityLevel.Level1, inc.Severity);
    }

    // ---------- Đọc phản hồi YoloService /api/yolo/triage7 ----------

    [Fact]
    public void CnnTriage_Parse_MapsLabels_AndMarksBrokenModelsUnavailable()
    {
        const string body = """
            {"models":[
              {"model_name":"ResNet-50 (7 levels)","predicted_class":"accident_L3","confidence":0.97,"is_available":true},
              {"model_name":"YOLO11n-cls (7 levels)","predicted_class":null,"confidence":null,"is_available":false,"error":"Model not loaded"}
            ]}
            """;

        var votes = CnnTriageService.Parse(body);

        Assert.Equal(2, votes.Count);
        Assert.True(votes[0].IsAvailable);
        Assert.Equal("accident", votes[0].IncidentType);
        Assert.Equal(3, votes[0].Severity);
        Assert.False(votes[1].IsAvailable);
        Assert.Equal("Model not loaded", votes[1].Error);
    }

    [Theory]
    [InlineData("fire_L1", true, "fire", 1)]
    [InlineData("accident_L3", true, "accident", 3)]
    [InlineData("none", true, "none", 0)]
    [InlineData("fire_L4", false, "none", 0)]
    [InlineData("flood_L2", false, "none", 0)]
    [InlineData(null, false, "none", 0)]
    public void CnnTriage_TryParseLabel(string? label, bool ok, string type, int severity)
    {
        Assert.Equal(ok, CnnTriageService.TryParseLabel(label, out var t, out var s));
        Assert.Equal(type, t);
        Assert.Equal(severity, s);
    }

    [Fact]
    public void Settings_DefaultModeIsEnsemble_UnknownModeIsRejected()
    {
        Assert.Equal(AutoDispatchModes.Ensemble, new DispatchSettingsDto().AutoDispatchMode);
        Assert.Contains(SystemSettingsService.ValidateDispatch(new DispatchSettingsDto { AutoDispatchMode = "Majority" }, 30),
            e => e.Contains("Chế độ AI tự điều phối"));
        Assert.Empty(SystemSettingsService.ValidateDispatch(new DispatchSettingsDto { AutoDispatchMode = AutoDispatchModes.Single }, 30));
    }
}
