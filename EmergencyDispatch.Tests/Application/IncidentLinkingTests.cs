using EmergencyDispatch.Application.DTOs.Settings;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Tests.Application;

/// <summary>BR-07 duplicate linking rules (pure functions) and the related settings validation.</summary>
public class IncidentLinkingTests
{
    private const double Lat = 10.7335, Lng = 106.7192;
    private static readonly DateTime Now = DateTime.UtcNow;

    private static Incident Candidate(double dLatMeters, IncidentEventType type = IncidentEventType.None, int minutesAgo = 2,
        IncidentStatus status = IncidentStatus.Verified, string description = "")
        => new()
        {
            Latitude = Lat + dLatMeters / 111_320.0,
            Longitude = Lng,
            EventType = type,
            Status = status,
            Description = description,
            CreatedAt = Now.AddMinutes(-minutesAgo)
        };

    // ---------- KeywordType / KnownType ----------
    [Theory]
    [InlineData("Cháy nhà dân", IncidentEventType.FireExplosion)]
    [InlineData("thấy KHÓI bốc lên", IncidentEventType.FireExplosion)]
    [InlineData("Tai nạn giao thông", IncidentEventType.TrafficAccident)]
    [InlineData("xe máy va chạm", IncidentEventType.TrafficAccident)]
    public void KeywordType_DetectsSingleCategory(string text, IncidentEventType expected)
        => Assert.Equal(expected, IncidentLinking.KeywordType(text));

    [Theory]
    [InlineData("")]
    [InlineData("Cần giúp đỡ")]
    [InlineData("Tai nạn khiến xe bốc cháy")]
    public void KeywordType_UnknownOrBoth_IsNull(string text)
        => Assert.Null(IncidentLinking.KeywordType(text));

    [Fact]
    public void KnownType_PrefersOperatorThenAiThenReporterThenKeywords()
    {
        var inc = new Incident { Description = "va chạm", ReportedEventType = IncidentEventType.FireExplosion };
        Assert.Equal(IncidentEventType.FireExplosion, IncidentLinking.KnownType(inc));

        inc.AiClassification = new AiClassification { IsSuccess = true, EventType = IncidentEventType.TrafficAccident };
        Assert.Equal(IncidentEventType.TrafficAccident, IncidentLinking.KnownType(inc));

        inc.EventType = IncidentEventType.FireExplosion;
        Assert.Equal(IncidentEventType.FireExplosion, IncidentLinking.KnownType(inc));
    }

    [Fact]
    public void KnownType_IgnoresFailedAi()
    {
        var inc = new Incident { Description = "cháy", AiClassification = new AiClassification { IsSuccess = false, EventType = IncidentEventType.TrafficAccident } };
        Assert.Equal(IncidentEventType.FireExplosion, IncidentLinking.KnownType(inc));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(IncidentEventType.FireExplosion, null, true)]
    [InlineData(IncidentEventType.FireExplosion, IncidentEventType.FireExplosion, true)]
    [InlineData(IncidentEventType.FireExplosion, IncidentEventType.TrafficAccident, false)]
    public void Compatible_UnknownMatchesAnything(IncidentEventType? a, IncidentEventType? b, bool expected)
        => Assert.Equal(expected, IncidentLinking.Compatible(a, b));

    // ---------- Geometry ----------
    [Fact]
    public void BoundingBox_ContainsPointsWithinRadius()
    {
        var box = IncidentLinking.BoundingBox(Lat, Lng, 200);
        var north = Lat + 199 / 111_320.0;
        Assert.InRange(north, box.MinLat, box.MaxLat);
        Assert.True(box.MaxLng > Lng && box.MinLng < Lng);
    }

    [Fact]
    public void DistanceMeters_IsApproximatelyMeters()
        => Assert.InRange(IncidentLinking.DistanceMeters(Lat, Lng, Lat + 100 / 111_320.0, Lng), 98, 102);

    // ---------- PickParent ----------
    [Fact]
    public void PickParent_ChoosesNearestCompatibleCandidate()
    {
        var far = Candidate(150, IncidentEventType.FireExplosion);
        var near = Candidate(40, IncidentEventType.FireExplosion);
        var r = IncidentLinking.PickParent(new[] { far, near }, Lat, Lng, IncidentEventType.FireExplosion, 200, Now.AddMinutes(-15));
        Assert.Same(near, r!.Value.Parent);
        Assert.InRange(r.Value.DistanceMeters, 35, 45);
    }

    [Fact]
    public void PickParent_SkipsIncompatibleType_EvenIfNearer()
    {
        var accident = Candidate(20, IncidentEventType.TrafficAccident);
        var fire = Candidate(120, IncidentEventType.FireExplosion);
        var r = IncidentLinking.PickParent(new[] { accident, fire }, Lat, Lng, IncidentEventType.FireExplosion, 200, Now.AddMinutes(-15));
        Assert.Same(fire, r!.Value.Parent);
    }

    [Fact]
    public void PickParent_OutsideRadius_IsNull()
        => Assert.Null(IncidentLinking.PickParent(new[] { Candidate(250) }, Lat, Lng, null, 200, Now.AddMinutes(-15)));

    [Fact]
    public void PickParent_OutsideWindow_IsNull()
        => Assert.Null(IncidentLinking.PickParent(new[] { Candidate(10, minutesAgo: 30) }, Lat, Lng, null, 200, Now.AddMinutes(-15)));

    [Fact]
    public void PickParent_RecentLinkedReportKeepsOldParentInWindow()
    {
        var parent = Candidate(10, minutesAgo: 30);
        parent.LatestReportAt = Now.AddMinutes(-1);
        Assert.NotNull(IncidentLinking.PickParent(new[] { parent }, Lat, Lng, null, 200, Now.AddMinutes(-15)));
    }

    [Fact]
    public void PickParent_NeverChainsToALinkedReport()
        => Assert.Null(IncidentLinking.PickParent(new[] { Candidate(10, status: IncidentStatus.Linked) }, Lat, Lng, null, 200, Now.AddMinutes(-15)));

    [Fact]
    public void PickParent_UsesKeywordTypeOfCandidate()
    {
        var accidentByText = Candidate(10, description: "tai nạn liên hoàn");
        Assert.Null(IncidentLinking.PickParent(new[] { accidentByText }, Lat, Lng, IncidentEventType.FireExplosion, 200, Now.AddMinutes(-15)));
    }

    // ---------- IsOutlier ----------
    [Fact]
    public void IsOutlier_FarEdgeOfRadius()
        => Assert.True(IncidentLinking.IsOutlier(new Incident { LinkDistanceMeters = 150 }, IncidentEventType.FireExplosion, 200));

    [Fact]
    public void IsOutlier_ReportedOrYoloTypeConflicts()
    {
        Assert.True(IncidentLinking.IsOutlier(new Incident { LinkDistanceMeters = 20, ReportedEventType = IncidentEventType.TrafficAccident }, IncidentEventType.FireExplosion, 200));
        Assert.True(IncidentLinking.IsOutlier(new Incident { LinkDistanceMeters = 20, QuickCheckEventType = IncidentEventType.TrafficAccident }, IncidentEventType.FireExplosion, 200));
    }

    [Fact]
    public void IsOutlier_CloseAndConsistent_IsFalse()
        => Assert.False(IncidentLinking.IsOutlier(new Incident { LinkDistanceMeters = 50, QuickCheckEventType = IncidentEventType.FireExplosion }, IncidentEventType.FireExplosion, 200));

    // ---------- Settings validation ----------
    [Fact]
    public void ValidateDuplicateDetection_Defaults_AreValid()
        => Assert.Empty(SystemSettingsService.ValidateDuplicateDetection(new DuplicateDetectionSettingsDto()));

    [Theory]
    [InlineData(19, 15)]
    [InlineData(2001, 15)]
    [InlineData(200, 0)]
    [InlineData(200, 121)]
    public void ValidateDuplicateDetection_OutOfRange(double radius, double window)
        => Assert.NotEmpty(SystemSettingsService.ValidateDuplicateDetection(new DuplicateDetectionSettingsDto { RadiusMeters = radius, WindowMinutes = window }));

    [Theory]
    [InlineData(0)]
    [InlineData(1.2)]
    public void ValidateEscalation_FieldRequestThresholdMustBePositiveHalfStep(double minutes)
        => Assert.Contains("Ngưỡng chờ xử lý yêu cầu từ hiện trường phải > 0 và theo bước 0.5 phút.",
            SystemSettingsService.ValidateEscalation(new EscalationSettingsDto { FieldRequestThresholdMinutes = minutes }, new[] { 3.0, 5.0, 10.0 }));
}
