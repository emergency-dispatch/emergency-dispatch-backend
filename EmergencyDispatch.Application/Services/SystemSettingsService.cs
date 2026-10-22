using EmergencyDispatch.Application.Localization;
using System.Globalization;
using System.Text.Json;
using EmergencyDispatch.Application.DTOs.Audit;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Settings;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Application.Services;

/// <summary>
/// Tham số vận hành lưu trong bảng SystemSettings; chưa có bản ghi thì dùng mặc định theo SRS (BR-11, BR-12, BR-19, BR-27)
/// </summary>
public class SystemSettingsService : ISystemSettingsService
{
    public const string DispatchKey = "dispatch";
    public const string EscalationKey = "escalation";
    public const string ServiceAreaKey = "serviceArea";
    private const double WeightSumTolerance = 0.001;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISystemSettingRepository _repository;
    private readonly ILogger<SystemSettingsService> _logger;
    private readonly ISystemAuditService _auditService;
    private readonly IRealtimeNotifier _realtimeNotifier;

    public SystemSettingsService(ISystemSettingRepository repository, ILogger<SystemSettingsService> logger, ISystemAuditService auditService, IRealtimeNotifier realtimeNotifier)
    {
        _realtimeNotifier = realtimeNotifier;
        _repository = repository;
        _logger = logger;
        _auditService = auditService;
    }

    public async Task<DispatchSettingsDto> GetDispatchSettingsAsync(CancellationToken cancellationToken = default)
        => (await GetDispatchRecordAsync(cancellationToken)).Values;

    public async Task<EscalationSettingsDto> GetEscalationSettingsAsync(CancellationToken cancellationToken = default)
        => (await GetEscalationRecordAsync(cancellationToken)).Values;

    public Task<SettingsRecordDto<DispatchSettingsDto>> GetDispatchRecordAsync(CancellationToken cancellationToken = default)
        => LoadAsync<DispatchSettingsDto>(DispatchKey, cancellationToken);

    public Task<SettingsRecordDto<EscalationSettingsDto>> GetEscalationRecordAsync(CancellationToken cancellationToken = default)
        => LoadAsync<EscalationSettingsDto>(EscalationKey, cancellationToken);

    public async Task<ApiResponseDto<SettingsRecordDto<DispatchSettingsDto>>> UpdateDispatchAsync(
        DispatchSettingsDto values,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var escalation = await GetEscalationSettingsAsync(cancellationToken);
        var errors = ValidateDispatch(values, escalation.MaxRadiusKm);
        if (errors.Count > 0)
        {
            return ApiResponseDto<SettingsRecordDto<DispatchSettingsDto>>.FailureResult(errors, "Tham số điều phối không hợp lệ.");
        }

        values.RadiusStepsKm = values.RadiusStepsKm.OrderBy(r => r).ToList();
        var before = await GetDispatchSettingsAsync(cancellationToken);
        await _repository.UpsertAsync(DispatchKey, JsonSerializer.Serialize(values, JsonOptions), actorUserId, cancellationToken);
        _logger.LogInformation("Admin {Actor} cập nhật tham số điều phối: {Json}", actorUserId, JsonSerializer.Serialize(values, JsonOptions));
        await _auditService.LogAsync(SystemAuditAction.SettingsUpdated, AuditEntityTypes.Settings, null, DispatchKey,
            "Cập nhật tham số điều phối (trọng số Load-Aware, bán kính tìm xe, thời gian CoolDown)", before, values, cancellationToken);

        var dispatchRecord = await GetDispatchRecordAsync(cancellationToken);
        await PublishSettingsAsync(DispatchKey, dispatchRecord.UpdatedAt, dispatchRecord.UpdatedByName, cancellationToken);
        return ApiResponseDto<SettingsRecordDto<DispatchSettingsDto>>.SuccessResult(dispatchRecord, "Đã lưu tham số điều phối.");
    }

    public async Task<ApiResponseDto<SettingsRecordDto<EscalationSettingsDto>>> UpdateEscalationAsync(
        EscalationSettingsDto values,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var dispatch = await GetDispatchSettingsAsync(cancellationToken);
        var escalationBefore = await GetEscalationSettingsAsync(cancellationToken);
        var errors = ValidateEscalation(values, dispatch.RadiusStepsKm);
        if (errors.Count > 0)
        {
            return ApiResponseDto<SettingsRecordDto<EscalationSettingsDto>>.FailureResult(errors, "Cấu hình leo thang không hợp lệ.");
        }

        var before = escalationBefore;
        await _repository.UpsertAsync(EscalationKey, JsonSerializer.Serialize(values, JsonOptions), actorUserId, cancellationToken);
        _logger.LogInformation("Admin {Actor} cập nhật cấu hình leo thang: {Json}", actorUserId, JsonSerializer.Serialize(values, JsonOptions));
        await _auditService.LogAsync(SystemAuditAction.SettingsUpdated, AuditEntityTypes.Settings, null, EscalationKey,
            "Cập nhật cấu hình leo thang (ngưỡng ngày/đêm, khung giờ đêm, mở rộng bán kính)", before, values, cancellationToken);

        var escalationRecord = await GetEscalationRecordAsync(cancellationToken);
        await PublishSettingsAsync(EscalationKey, escalationRecord.UpdatedAt, escalationRecord.UpdatedByName, cancellationToken);
        return ApiResponseDto<SettingsRecordDto<EscalationSettingsDto>>.SuccessResult(escalationRecord, "Đã lưu cấu hình leo thang.");
    }

    public async Task<ServiceAreaSettingsDto> GetServiceAreaAsync(CancellationToken cancellationToken = default)
        => (await GetServiceAreaRecordAsync(cancellationToken)).Values;

    public Task<SettingsRecordDto<ServiceAreaSettingsDto>> GetServiceAreaRecordAsync(CancellationToken cancellationToken = default)
        => LoadAsync<ServiceAreaSettingsDto>(ServiceAreaKey, cancellationToken);

    public async Task<ApiResponseDto<SettingsRecordDto<ServiceAreaSettingsDto>>> UpdateServiceAreaAsync(
        ServiceAreaSettingsDto values,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        values.Name = values.Name?.Trim() ?? string.Empty;
        var errors = StationAssignment.ValidateServiceArea(values);
        if (errors.Count > 0)
        {
            return ApiResponseDto<SettingsRecordDto<ServiceAreaSettingsDto>>.FailureResult(errors, "Cấu hình khu vực phục vụ không hợp lệ.");
        }

        var before = await GetServiceAreaAsync(cancellationToken);
        await _repository.UpsertAsync(ServiceAreaKey, JsonSerializer.Serialize(values, JsonOptions), actorUserId, cancellationToken);
        await _auditService.LogAsync(SystemAuditAction.SettingsUpdated, AuditEntityTypes.Settings, null, ServiceAreaKey,
            $"Cập nhật khu vực phục vụ \"{values.Name}\" (bán kính {values.RadiusKm} km, {(values.Enabled ? "bật" : "tắt")} kiểm tra)", before, values, cancellationToken);

        var areaRecord = await GetServiceAreaRecordAsync(cancellationToken);
        await PublishSettingsAsync(ServiceAreaKey, areaRecord.UpdatedAt, areaRecord.UpdatedByName, cancellationToken);
        return ApiResponseDto<SettingsRecordDto<ServiceAreaSettingsDto>>.SuccessResult(areaRecord, "Đã lưu khu vực phục vụ.");
    }

    private Task PublishSettingsAsync(string key, DateTime? updatedAt, string? updatedByName, CancellationToken cancellationToken)
        => _realtimeNotifier.SettingsChangedAsync(new EmergencyDispatch.Application.DTOs.Incident.SettingsChangedEventDto
        {
            Key = key,
            UpdatedAt = updatedAt,
            UpdatedByName = updatedByName
        }, cancellationToken);

    /// <summary>
    /// Quy tắc kiểm tra dùng chung với màn hình Admin (FE adminSettingsService.ts)
    /// </summary>
    public static List<string> ValidateDispatch(DispatchSettingsDto s, double maxRadiusKm)
    {
        var errors = new List<string>();
        var w = s.Weights;
        if (w == null || new[] { w.Distance, w.StationLoad, w.ActiveMissions }.Any(x => !(x > 0)))
        {
            errors.Add("Mỗi trọng số Load-Aware phải lớn hơn 0.");
        }
        else if (Math.Abs(w.Distance + w.StationLoad + w.ActiveMissions - 1) > WeightSumTolerance)
        {
            errors.Add("Tổng ba trọng số Load-Aware phải bằng 1.");
        }

        var steps = s.RadiusStepsKm ?? new List<double>();
        if (steps.Count == 0) errors.Add("Cần ít nhất 1 nấc bán kính.");
        else if (steps.Any(r => !(r > 0))) errors.Add("Mỗi nấc bán kính phải lớn hơn 0.");
        else if (steps.Where((r, i) => i > 0 && r <= steps[i - 1]).Any()) errors.Add("Các nấc bán kính phải tăng dần.");
        else if (steps[^1] > maxRadiusKm) errors.Add(LocalizationHelper.Localize("Nấc cuối không được vượt bán kính tối đa của cấu hình leo thang ({0} km).", maxRadiusKm));

        if (!(s.RecoveryMinutes > 0)) errors.Add("Thời gian hồi sức phải lớn hơn 0.");
        if (!(s.AutoExpireHours >= 1 && s.AutoExpireHours <= 168)) errors.Add("Thời gian tự hủy sự cố chưa xác minh phải từ 1 đến 168 giờ.");
        return errors;
    }

    public static List<string> ValidateEscalation(EscalationSettingsDto s, IReadOnlyList<double> dispatchStepsKm)
    {
        var errors = new List<string>();
        static bool IsHalfStep(double n) => Math.Abs(n * 2 - Math.Round(n * 2)) < 1e-9;

        if (!(s.DayThresholdMinutes > 0) || !IsHalfStep(s.DayThresholdMinutes)) errors.Add("Ngưỡng ban ngày phải > 0 và theo bước 0.5 phút.");
        if (!(s.NightThresholdMinutes > 0) || !IsHalfStep(s.NightThresholdMinutes)) errors.Add("Ngưỡng ban đêm phải > 0 và theo bước 0.5 phút.");

        var startOk = TimeOnly.TryParseExact(s.NightStart, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        var endOk = TimeOnly.TryParseExact(s.NightEnd, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        if (!startOk) errors.Add("Giờ bắt đầu khung đêm không hợp lệ (HH:mm).");
        if (!endOk) errors.Add("Giờ kết thúc khung đêm không hợp lệ (HH:mm).");
        else if (startOk && s.NightStart == s.NightEnd) errors.Add("Giờ kết thúc khung đêm phải khác giờ bắt đầu.");

        if (!(s.RadiusMultiplier > 0)) errors.Add("Hệ số mở rộng bán kính phải lớn hơn 0.");

        var lastStep = dispatchStepsKm.Count > 0 ? dispatchStepsKm.Max() : 0;
        if (!(s.MaxRadiusKm > 0)) errors.Add("Bán kính tối đa phải lớn hơn 0.");
        else if (s.MaxRadiusKm < lastStep) errors.Add(LocalizationHelper.Localize("Bán kính tối đa không được nhỏ hơn nấc bán kính cuối của tham số điều phối ({0} km).", lastStep));

        return errors;
    }

    private async Task<SettingsRecordDto<T>> LoadAsync<T>(string key, CancellationToken cancellationToken) where T : new()
    {
        var setting = await _repository.GetByKeyAsync(key, cancellationToken);
        if (setting == null)
        {
            return new SettingsRecordDto<T> { Values = new T() };
        }

        T values;
        try
        {
            values = JsonSerializer.Deserialize<T>(setting.ValueJson, JsonOptions) ?? new T();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Cấu hình '{Key}' trong DB không đọc được, dùng giá trị mặc định.", key);
            values = new T();
        }

        return new SettingsRecordDto<T>
        {
            Values = values,
            UpdatedAt = setting.UpdatedAt ?? setting.CreatedAt,
            UpdatedByName = setting.UpdatedByUser?.FullName
        };
    }
}
