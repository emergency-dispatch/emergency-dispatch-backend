using System.Text.Json;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.DTOs.Settings;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EmergencyDispatch.Tests.Application;

public class SystemSettingsServiceTests
{
    private readonly Mock<ISystemSettingRepository> _repo = new();
    private readonly Mock<ISystemAuditService> _audit = new();
    private readonly Mock<IRealtimeNotifier> _rt = new();
    private readonly Dictionary<string, string> _store = new();
    private readonly SystemSettingsService _svc;

    public SystemSettingsServiceTests()
    {
        _repo.Setup(r => r.GetByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string k, CancellationToken _) => _store.TryGetValue(k, out var v) ? new SystemSetting { Key = k, ValueJson = v } : null);
        _repo.Setup(r => r.UpsertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string k, string v, Guid? _, CancellationToken _) => { _store[k] = v; return new SystemSetting { Key = k, ValueJson = v }; });
        _svc = new SystemSettingsService(_repo.Object, NullLogger<SystemSettingsService>.Instance, _audit.Object, _rt.Object);
    }

    // ---------- defaults & loading ----------
    [Fact]
    public async Task Get_NoStoredValue_ReturnsDefaults()
    {
        var d = await _svc.GetDispatchSettingsAsync();
        var e = await _svc.GetEscalationSettingsAsync();

        Assert.Equal(new double[] { 10, 15, 22.5, 30 }, d.RadiusStepsKm);
        Assert.Equal(5, d.RecoveryMinutes);
        Assert.Equal(5, e.DayThresholdMinutes);
        Assert.Equal(2.5, e.NightThresholdMinutes);
    }

    [Fact]
    public async Task Get_CorruptJson_FallsBackToDefaults()
    {
        _store[SystemSettingsService.EscalationKey] = "{not json";

        Assert.Equal(5, (await _svc.GetEscalationSettingsAsync()).DayThresholdMinutes);
    }

    [Fact]
    public async Task Get_StoredJson_IsDeserialized()
    {
        _store[SystemSettingsService.EscalationKey] = JsonSerializer.Serialize(new EscalationSettingsDto { DayThresholdMinutes = 7 }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(7, (await _svc.GetEscalationSettingsAsync()).DayThresholdMinutes);
    }

    // ---------- ValidateDispatch ----------
    [Fact]
    public void ValidateDispatch_Defaults_AreValid()
    {
        Assert.Empty(SystemSettingsService.ValidateDispatch(new DispatchSettingsDto(), 30));
    }

    [Fact]
    public void ValidateDispatch_WeightsMustBePositiveAndSumToOne()
    {
        var zero = new DispatchSettingsDto { Weights = new LoadAwareWeightsDto { Distance = 0, StationLoad = 0.5, ActiveMissions = 0.5 } };
        var sum = new DispatchSettingsDto { Weights = new LoadAwareWeightsDto { Distance = 0.5, StationLoad = 0.5, ActiveMissions = 0.5 } };

        Assert.Contains("Mỗi trọng số Load-Aware phải lớn hơn 0.", SystemSettingsService.ValidateDispatch(zero, 30));
        Assert.Contains("Tổng ba trọng số Load-Aware phải bằng 1.", SystemSettingsService.ValidateDispatch(sum, 30));
    }

    [Theory]
    [InlineData(new double[0], "Cần ít nhất 1 nấc bán kính.")]
    [InlineData(new double[] { 10, 0 }, "Mỗi nấc bán kính phải lớn hơn 0.")]
    [InlineData(new double[] { 10, 10 }, "Các nấc bán kính phải tăng dần.")]
    [InlineData(new double[] { 15, 10 }, "Các nấc bán kính phải tăng dần.")]
    public void ValidateDispatch_RadiusSteps(double[] steps, string expected)
    {
        var s = new DispatchSettingsDto { RadiusStepsKm = steps.ToList() };

        Assert.Contains(expected, SystemSettingsService.ValidateDispatch(s, 30));
    }

    [Fact]
    public void ValidateDispatch_LastStepAboveEscalationMax_IsRejected()
    {
        var s = new DispatchSettingsDto { RadiusStepsKm = new() { 10, 40 } };

        Assert.Single(SystemSettingsService.ValidateDispatch(s, 30));
    }

    [Theory]
    [InlineData(0, 24, 1)]
    [InlineData(5, 0.5, 1)]
    [InlineData(5, 169, 1)]
    [InlineData(5, 168, 0)]
    public void ValidateDispatch_RecoveryAndAutoExpire(double recovery, double autoExpire, int errorCount)
    {
        var s = new DispatchSettingsDto { RecoveryMinutes = recovery, AutoExpireHours = autoExpire };

        Assert.Equal(errorCount, SystemSettingsService.ValidateDispatch(s, 30).Count);
    }

    // ---------- ValidateEscalation ----------
    private static readonly double[] Steps = { 10, 15, 22.5, 30 };

    [Fact]
    public void ValidateEscalation_Defaults_AreValid()
    {
        Assert.Empty(SystemSettingsService.ValidateEscalation(new EscalationSettingsDto(), Steps));
    }

    [Theory]
    [InlineData(0, 2.5)]
    [InlineData(1.2, 2.5)]
    [InlineData(5, 0)]
    [InlineData(5, 2.25)]
    public void ValidateEscalation_ThresholdsMustBePositiveHalfSteps(double day, double night)
    {
        var s = new EscalationSettingsDto { DayThresholdMinutes = day, NightThresholdMinutes = night };

        Assert.Single(SystemSettingsService.ValidateEscalation(s, Steps));
    }

    [Theory]
    [InlineData("25:00", "06:00")]
    [InlineData("22:00", "6h")]
    [InlineData("22:00", "22:00")]
    public void ValidateEscalation_NightWindow(string start, string end)
    {
        var s = new EscalationSettingsDto { NightStart = start, NightEnd = end };

        Assert.NotEmpty(SystemSettingsService.ValidateEscalation(s, Steps));
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(2, 0)]
    [InlineData(2, 20)]
    public void ValidateEscalation_MultiplierAndMaxRadius(double multiplier, double maxRadius)
    {
        var s = new EscalationSettingsDto { RadiusMultiplier = multiplier, MaxRadiusKm = maxRadius };

        Assert.NotEmpty(SystemSettingsService.ValidateEscalation(s, Steps));
    }

    // ---------- Update flows ----------
    [Fact]
    public async Task UpdateDispatch_Invalid_DoesNotPersist()
    {
        var r = await _svc.UpdateDispatchAsync(new DispatchSettingsDto { RecoveryMinutes = 0 }, Guid.NewGuid());

        Assert.False(r.Success);
        _repo.Verify(x => x.UpsertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        _rt.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateDispatch_Valid_PersistsAuditsAndBroadcasts()
    {
        var actor = Guid.NewGuid();

        var r = await _svc.UpdateDispatchAsync(new DispatchSettingsDto { RecoveryMinutes = 8 }, actor);

        Assert.True(r.Success);
        Assert.Equal(8, r.Data!.Values.RecoveryMinutes);
        _repo.Verify(x => x.UpsertAsync(SystemSettingsService.DispatchKey, It.IsAny<string>(), actor, It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.LogAsync(SystemAuditAction.SettingsUpdated, It.IsAny<string>(), null, SystemSettingsService.DispatchKey,
            It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        _rt.Verify(n => n.SettingsChangedAsync(It.Is<SettingsChangedEventDto>(e => e.Key == SystemSettingsService.DispatchKey), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateEscalation_Valid_Persists()
    {
        var r = await _svc.UpdateEscalationAsync(new EscalationSettingsDto { DayThresholdMinutes = 3 }, Guid.NewGuid());

        Assert.True(r.Success);
        Assert.Equal(3, (await _svc.GetEscalationSettingsAsync()).DayThresholdMinutes);
    }

    [Fact]
    public async Task UpdateEscalation_MaxRadiusBelowDispatchSteps_IsRejected()
    {
        var r = await _svc.UpdateEscalationAsync(new EscalationSettingsDto { MaxRadiusKm = 20 }, Guid.NewGuid());

        Assert.False(r.Success);
    }

    [Fact]
    public async Task UpdateServiceArea_TrimsNameAndValidates()
    {
        var bad = await _svc.UpdateServiceAreaAsync(new ServiceAreaSettingsDto { Name = "  ", RadiusKm = 10 }, Guid.NewGuid());
        var ok = await _svc.UpdateServiceAreaAsync(new ServiceAreaSettingsDto { Name = "  HCM  ", Enabled = true, CenterLat = 10.77, CenterLng = 106.7, RadiusKm = 30 }, Guid.NewGuid());

        Assert.False(bad.Success);
        Assert.True(ok.Success);
        Assert.Equal("HCM", (await _svc.GetServiceAreaAsync()).Name);
    }
}
