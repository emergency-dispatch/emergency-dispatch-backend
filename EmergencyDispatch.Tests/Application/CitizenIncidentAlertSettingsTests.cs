using System.Text.Json;
using EmergencyDispatch.Application.DTOs.Settings;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EmergencyDispatch.Tests.Application;

public class CitizenIncidentAlertSettingsTests
{
    private readonly Dictionary<string, string> _store = new();
    private readonly SystemSettingsService _service;
    public CitizenIncidentAlertSettingsTests()
    {
        var repo = new Mock<ISystemSettingRepository>();
        repo.Setup(r => r.GetByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) => _store.TryGetValue(key, out var json) ? new SystemSetting { Key = key, ValueJson = json } : null);
        repo.Setup(r => r.UpsertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, string json, Guid? _, CancellationToken _) => { _store[key] = json; return new SystemSetting { Key = key, ValueJson = json }; });
        _service = new(repo.Object, NullLogger<SystemSettingsService>.Instance, new Mock<ISystemAuditService>().Object, new Mock<IRealtimeNotifier>().Object);
    }

    [Fact]
    public async Task DefaultIsDisabledAndDoesNotHaveAnActivationTime()
    {
        var settings = await _service.GetCitizenIncidentAlertSettingsAsync();
        Assert.False(settings.Enabled); Assert.Null(settings.EnabledFromUtc);
    }

    [Fact]
    public async Task EnablingUsesServerTimeAndIgnoresClientBackdating()
    {
        var before = DateTime.UtcNow;
        var response = await _service.UpdateCitizenIncidentAlertAsync(new() { Enabled = true, EnabledFromUtc = DateTime.UnixEpoch }, Guid.NewGuid());
        Assert.True(response.Success);
        Assert.InRange(response.Data!.Values.EnabledFromUtc!.Value, before, DateTime.UtcNow);
    }

    [Fact]
    public async Task UpdatingEnabledSettingsPreservesOriginalActivationTime()
    {
        var original = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        _store[SystemSettingsService.CitizenIncidentAlertsKey] = JsonSerializer.Serialize(new CitizenIncidentAlertSettingsDto { Enabled = true, EnabledFromUtc = original });
        var response = await _service.UpdateCitizenIncidentAlertAsync(new() { Enabled = true, EnabledFromUtc = DateTime.UnixEpoch }, Guid.NewGuid());
        Assert.Equal(original, response.Data!.Values.EnabledFromUtc);
    }

    [Fact]
    public async Task ReenablingStartsANewWindowWithoutReplayingOlderIncidents()
    {
        var original = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        _store[SystemSettingsService.CitizenIncidentAlertsKey] = JsonSerializer.Serialize(new CitizenIncidentAlertSettingsDto { Enabled = false, EnabledFromUtc = original });
        var before = DateTime.UtcNow;
        var response = await _service.UpdateCitizenIncidentAlertAsync(new() { Enabled = true }, Guid.NewGuid());
        Assert.InRange(response.Data!.Values.EnabledFromUtc!.Value, before, DateTime.UtcNow);
        Assert.NotEqual(original, response.Data.Values.EnabledFromUtc);
    }
}
