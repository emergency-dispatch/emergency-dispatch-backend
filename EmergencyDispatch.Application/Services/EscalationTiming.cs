using System.Globalization;
using EmergencyDispatch.Application.DTOs.Settings;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.Services;

/// <summary>
/// Các hàm tính toán thuần của Escalation Daemon (FR-SYS-02, BR-19)
/// </summary>
public static class EscalationTiming
{
    /// <summary>
    /// Việt Nam dùng UTC+7 cố định, không có giờ mùa hè
    /// </summary>
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /// <summary>
    /// Thời điểm hiện tại có nằm trong khung đêm hay không (khung có thể vắt qua nửa đêm, VD 22:00 → 06:00)
    /// </summary>
    public static bool IsNightShift(DateTime utcNow, string nightStart, string nightEnd)
    {
        var local = TimeOnly.FromDateTime(utcNow + VietnamOffset);
        var start = ParseTime(nightStart, new TimeOnly(22, 0));
        var end = ParseTime(nightEnd, new TimeOnly(6, 0));

        return start <= end
            ? local >= start && local < end
            : local >= start || local < end;
    }

    /// <summary>
    /// Ngưỡng chờ (phút) áp dụng tại thời điểm hiện tại: ban đêm dùng ngưỡng rút ngắn (BR-19)
    /// </summary>
    public static double GetThresholdMinutes(DateTime utcNow, EscalationSettingsDto settings)
        => IsNightShift(utcNow, settings.NightStart, settings.NightEnd)
            ? settings.NightThresholdMinutes
            : settings.DayThresholdMinutes;

    /// <summary>
    /// Các mức độ bị quét: mọi mức từ ngưỡng tối thiểu của quy tắc trở lên, luôn kèm Level 0
    /// (Reject / AI lỗi được ưu tiên ngang Level 3 để tránh bỏ sót sự cố thật — FR-SYS-02)
    /// </summary>
    public static IReadOnlyList<SeverityLevel> SeveritiesInScope(SeverityLevel minSeverity)
        => Enum.GetValues<SeverityLevel>()
            .Where(s => s == SeverityLevel.Unclassified || s >= minSeverity)
            .Distinct()
            .ToList();

    private static TimeOnly ParseTime(string value, TimeOnly fallback)
        => TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : fallback;
}
