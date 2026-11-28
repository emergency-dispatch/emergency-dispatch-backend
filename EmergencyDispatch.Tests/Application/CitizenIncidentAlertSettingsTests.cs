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
    private readonly Mock<ISystemSettingRepository> _repository = new();
    private readonly Mock<ISystemAuditService> _audit = new();
    private readonly Mock<IRealtimeNotifier> _realtime = new();
    public CitizenIncidentAlertSettingsTests()
    {
        var repo = _repository;
        repo.Setup(r => r.GetByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) => _store.TryGetValue(key, out var json) ? new SystemSetting { Key = key, ValueJson = json } : null);
        repo.Setup(r => r.UpsertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, string json, Guid? _, CancellationToken _) => { _store[key] = json; return new SystemSetting { Key = key, ValueJson = json }; });
        repo.Setup(r => r.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, string json, CancellationToken _) =>
            {
                _store.TryAdd(key, json);
                return new SystemSetting { Key = key, ValueJson = _store[key] };
            });
        _service = new(repo.Object, NullLogger<SystemSettingsService>.Instance, _audit.Object, _realtime.Object);
    }

    [Fact]
    public async Task DefaultIsEnabledAndActivationTimeIsPersistedOnce()
    {
        var before = DateTime.UtcNow;
        var settings = await _service.GetCitizenIncidentAlertSettingsAsync();
        Assert.True(settings.Enabled);
        Assert.InRange(settings.EnabledFromUtc!.Value, before, DateTime.UtcNow);
        Assert.True(_store.ContainsKey(SystemSettingsService.CitizenIncidentAlertsKey));
        var again = await _service.GetCitizenIncidentAlertSettingsAsync();
        Assert.Equal(settings.EnabledFromUtc, again.EnabledFromUtc);
    }

    [Fact]
    public async Task StoredAdminOptOutRemainsDisabled()
    {
        _store[SystemSettingsService.CitizenIncidentAlertsKey] = "{\"enabled\":false}";
        var settings = await _service.GetCitizenIncidentAlertSettingsAsync();
        Assert.False(settings.Enabled);
        Assert.Null(settings.EnabledFromUtc);
    }

    [Fact]
    public async Task InvalidStoredConfigurationCannotPublishWithoutACutoff()
    {
        _store[SystemSettingsService.CitizenIncidentAlertsKey] = "invalid json";
        var settings = await _service.GetCitizenIncidentAlertSettingsAsync();
        Assert.False(settings.Enabled);
        Assert.Null(settings.EnabledFromUtc);
    }

    [Fact]
    public async Task EnablingUsesServerTimeAndIgnoresClientBackdating()
    {
        _store[SystemSettingsService.CitizenIncidentAlertsKey] = "{\"enabled\":false}";
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

    [Fact]
    public async Task LegacyJsonWithoutCooldown_UsesThreeMinutesWithoutRewritingActivationWindow()
    {
        const string json = "{\"enabled\":true,\"enabledFromUtc\":\"2026-11-01T00:00:00Z\"}";
        _store[SystemSettingsService.CitizenIncidentAlertsKey] = json;

        var settings = await _service.GetCitizenIncidentAlertSettingsAsync();

        Assert.Equal(3, settings.ReportCooldownMinutes);
        Assert.True(settings.Enabled);
        Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), settings.EnabledFromUtc);
        Assert.Equal(json, _store[SystemSettingsService.CitizenIncidentAlertsKey]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(60)]
    public async Task CooldownValidBoundaries_ArePersistedAndPublished_WithoutResettingCutoff(int minutes)
    {
        var cutoff = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        _store[SystemSettingsService.CitizenIncidentAlertsKey] = JsonSerializer.Serialize(new CitizenIncidentAlertSettingsDto { Enabled = true, EnabledFromUtc = cutoff });
        using var cancellation = new CancellationTokenSource();

        var result = await _service.UpdateCitizenIncidentAlertAsync(new() { Enabled = true, ReportCooldownMinutes = minutes }, Guid.NewGuid(), cancellation.Token);

        Assert.True(result.Success);
        Assert.Equal(minutes, result.Data!.Values.ReportCooldownMinutes);
        Assert.Equal(cutoff, result.Data.Values.EnabledFromUtc);
        using var saved = JsonDocument.Parse(_store[SystemSettingsService.CitizenIncidentAlertsKey]);
        Assert.Equal(minutes, saved.RootElement.GetProperty("reportCooldownMinutes").GetInt32());
        _realtime.Verify(r => r.SettingsChangedAsync(It.Is<EmergencyDispatch.Application.DTOs.Incident.SettingsChangedEventDto>(e => e.Key == SystemSettingsService.CitizenIncidentAlertsKey), cancellation.Token), Times.Once);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(61)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public async Task CooldownOutsideRange_IsRejectedWithoutWritesAuditOrRealtime(int minutes)
    {
        const string original = "{\"enabled\":false,\"reportCooldownMinutes\":3}";
        _store[SystemSettingsService.CitizenIncidentAlertsKey] = original;

        var result = await _service.UpdateCitizenIncidentAlertAsync(new() { Enabled = true, ReportCooldownMinutes = minutes }, Guid.NewGuid());

        Assert.False(result.Success);
        Assert.Equal(original, _store[SystemSettingsService.CitizenIncidentAlertsKey]);
        _repository.Verify(r => r.UpsertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        _audit.VerifyNoOtherCalls();
        _realtime.VerifyNoOtherCalls();
    }

    [Fact]
    public void AuthenticatedGetContract_UsesDataValuesAndCamelCaseCooldown_WhilePutRemainsAdminOnly()
    {
        var controller = typeof(EmergencyDispatch.API.Controllers.SettingsController);
        var get = controller.GetMethod(nameof(EmergencyDispatch.API.Controllers.SettingsController.GetCitizenIncidentAlerts))!;
        var put = controller.GetMethod(nameof(EmergencyDispatch.API.Controllers.SettingsController.UpdateCitizenIncidentAlerts))!;
        Assert.Empty(get.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), true));
        var controllerAuthorization = Assert.Single(controller.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
        Assert.Null(controllerAuthorization.Roles);
        Assert.Empty(put.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), true));
        var authorization = Assert.Single(put.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
        Assert.Equal("Admin", authorization.Roles);
        var response = EmergencyDispatch.Application.DTOs.Common.ApiResponseDto<SettingsRecordDto<CitizenIncidentAlertSettingsDto>>.Ok(
            new() { Values = new() { ReportCooldownMinutes = 10 } });
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(10, json.RootElement.GetProperty("data").GetProperty("values").GetProperty("reportCooldownMinutes").GetInt32());
    }

}
