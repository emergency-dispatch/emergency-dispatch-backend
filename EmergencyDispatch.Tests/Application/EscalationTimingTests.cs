using EmergencyDispatch.Application.DTOs.Settings;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Tests.Application;

public class EscalationTimingTests
{
    // Vietnam = UTC+7
    private static DateTime VnToUtc(int hour, int minute = 0) => new DateTime(2026, 11, 3, hour, minute, 0, DateTimeKind.Utc).AddHours(-7);

    [Theory]
    [InlineData(22, 0, true)]
    [InlineData(23, 30, true)]
    [InlineData(2, 0, true)]
    [InlineData(5, 59, true)]
    [InlineData(6, 0, false)]
    [InlineData(12, 0, false)]
    [InlineData(21, 59, false)]
    public void IsNightShift_OvernightWindow(int h, int m, bool expected)
    {
        Assert.Equal(expected, EscalationTiming.IsNightShift(VnToUtc(h, m), "22:00", "06:00"));
    }

    [Theory]
    [InlineData(13, 0, true)]
    [InlineData(12, 59, false)]
    [InlineData(15, 0, false)]
    public void IsNightShift_SameDayWindow(int h, int m, bool expected)
    {
        Assert.Equal(expected, EscalationTiming.IsNightShift(VnToUtc(h, m), "13:00", "15:00"));
    }

    [Fact]
    public void IsNightShift_InvalidTimes_FallBackTo22To6()
    {
        Assert.True(EscalationTiming.IsNightShift(VnToUtc(23), "bad", "also-bad"));
        Assert.False(EscalationTiming.IsNightShift(VnToUtc(10), "bad", "also-bad"));
    }

    [Fact]
    public void GetThresholdMinutes_PicksDayOrNightValue()
    {
        var s = new EscalationSettingsDto { DayThresholdMinutes = 5, NightThresholdMinutes = 2.5, NightStart = "22:00", NightEnd = "06:00" };

        Assert.Equal(5, EscalationTiming.GetThresholdMinutes(VnToUtc(10), s));
        Assert.Equal(2.5, EscalationTiming.GetThresholdMinutes(VnToUtc(23), s));
    }

    [Fact]
    public void SeveritiesInScope_AlwaysIncludesUnclassified()
    {
        var scope = EscalationTiming.SeveritiesInScope(SeverityLevel.Level3);

        Assert.Equal(new[] { SeverityLevel.Unclassified, SeverityLevel.Level3 }, scope);
    }

    [Fact]
    public void SeveritiesInScope_Level2_IncludesLevel2And3()
    {
        var scope = EscalationTiming.SeveritiesInScope(SeverityLevel.Level2);

        Assert.Equal(new[] { SeverityLevel.Unclassified, SeverityLevel.Level2, SeverityLevel.Level3 }, scope);
    }
}
