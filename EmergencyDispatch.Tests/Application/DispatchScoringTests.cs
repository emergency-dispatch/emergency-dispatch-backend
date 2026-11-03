using EmergencyDispatch.Application.DTOs.Settings;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Tests.Application;

public class DispatchScoringTests
{
    private static readonly double[] DefaultSteps = { 10, 15, 22.5, 30 };

    [Fact]
    public void BuildSearchRadii_NotEscalated_ReturnsPositiveStepsSorted()
    {
        var radii = DispatchScoring.BuildSearchRadii(new double[] { 22.5, 0, 10, -5, 15 }, escalated: false, 2, 30);

        Assert.Equal(new[] { 10, 15, 22.5 }, radii);
    }

    [Fact]
    public void BuildSearchRadii_NoValidSteps_FallsBackToMaxRadius()
    {
        var radii = DispatchScoring.BuildSearchRadii(Array.Empty<double>(), escalated: false, 2, 30);

        Assert.Equal(new double[] { 30 }, radii);
    }

    [Fact]
    public void BuildSearchRadii_Escalated_MultipliesAndCapsAtMaxWithoutDuplicates()
    {
        var radii = DispatchScoring.BuildSearchRadii(DefaultSteps, escalated: true, radiusMultiplier: 2, maxRadiusKm: 30);

        // 10*2=20, 15*2=30, 22.5*2=45->30, 30*2=60->30
        Assert.Equal(new double[] { 20, 30 }, radii);
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(1, 1.0 / 3)]
    [InlineData(3, 1.0)]
    [InlineData(10, 1.0)]
    public void NormalizeActiveMissions_SaturatesAtThree(int missions, double expected)
    {
        Assert.Equal(expected, DispatchScoring.NormalizeActiveMissions(missions), 6);
    }

    [Fact]
    public void LoadAwareCost_UsesDefaultWeights()
    {
        var w = new LoadAwareWeightsDto(); // 0.5 / 0.3 / 0.2

        var cost = DispatchScoring.LoadAwareCost(distanceKm: 5, radiusKm: 10, stationLoad: 0.5, stationActiveMissions: 3, w);

        Assert.Equal(0.5 * 0.5 + 0.3 * 0.5 + 0.2 * 1.0, cost, 6);
    }

    [Fact]
    public void LoadAwareCost_ClampsDistanceAndLoadToOne()
    {
        var w = new LoadAwareWeightsDto();

        var cost = DispatchScoring.LoadAwareCost(distanceKm: 50, radiusKm: 10, stationLoad: 7, stationActiveMissions: 0, w);

        Assert.Equal(0.5 + 0.3, cost, 6);
    }

    [Fact]
    public void LoadAwareCost_ZeroRadius_TreatsDistanceAsWorst()
    {
        var cost = DispatchScoring.LoadAwareCost(1, 0, 0, 0, new LoadAwareWeightsDto());

        Assert.Equal(0.5, cost, 6);
    }

    [Fact]
    public void LoadAwareCost_CloserUnitIsCheaperAtEqualLoad()
    {
        var w = new LoadAwareWeightsDto();

        Assert.True(DispatchScoring.LoadAwareCost(2, 10, 0.2, 1, w) < DispatchScoring.LoadAwareCost(8, 10, 0.2, 1, w));
    }

    [Theory]
    [InlineData(SeverityLevel.Level1, false)]
    [InlineData(SeverityLevel.Level3, true)]
    public void MatchingUnitTypes_Fire_ReturnsFireUnits_AndAmbulanceOnlyAtLevel3(SeverityLevel severity, bool withAmbulance)
    {
        var types = DispatchScoring.MatchingUnitTypes(IncidentEventType.FireExplosion, Array.Empty<string>(), "", severity)!;

        Assert.Contains(RescueUnitType.FireTruck, types);
        Assert.Contains(RescueUnitType.LadderTruck, types);
        Assert.Contains(RescueUnitType.HeavyRescueVehicle, types);
        Assert.Equal(withAmbulance, types.Contains(RescueUnitType.Ambulance));
    }

    [Theory]
    [InlineData(SeverityLevel.Level2, false)]
    [InlineData(SeverityLevel.Level3, true)]
    public void MatchingUnitTypes_Accident_ReturnsAmbulance_AndFireTruckOnlyAtLevel3(SeverityLevel severity, bool withFireTruck)
    {
        var types = DispatchScoring.MatchingUnitTypes(IncidentEventType.TrafficAccident, Array.Empty<string>(), "", severity)!;

        Assert.Contains(RescueUnitType.Ambulance, types);
        Assert.Contains(RescueUnitType.HeavyRescueVehicle, types);
        Assert.Equal(withFireTruck, types.Contains(RescueUnitType.FireTruck));
    }

    [Theory]
    [InlineData("smoke", "")]
    [InlineData("", "Cháy kho hàng")]
    [InlineData("", "tiếng nổ lớn")]
    public void MatchingUnitTypes_DetectsFireFromTagsOrVietnameseText(string tag, string text)
    {
        var types = DispatchScoring.MatchingUnitTypes(IncidentEventType.None, new[] { tag }, text, SeverityLevel.Level1);

        Assert.NotNull(types);
        Assert.Contains(RescueUnitType.FireTruck, types!);
    }

    [Fact]
    public void MatchingUnitTypes_DetectsAccidentFromText()
    {
        var types = DispatchScoring.MatchingUnitTypes(IncidentEventType.None, Array.Empty<string>(), "Va chạm xe máy", SeverityLevel.Level1);

        Assert.Contains(RescueUnitType.Ambulance, types!);
    }

    [Fact]
    public void MatchingUnitTypes_WaterOnly_ReturnsRescueBoat()
    {
        var types = DispatchScoring.MatchingUnitTypes(IncidentEventType.None, new[] { "flood" }, "", SeverityLevel.Level2);

        Assert.Equal(new[] { RescueUnitType.RescueBoat }, types!.ToArray());
    }

    [Fact]
    public void MatchingUnitTypes_FireWinsOverWater()
    {
        var types = DispatchScoring.MatchingUnitTypes(IncidentEventType.None, new[] { "flood", "fire" }, "", SeverityLevel.Level1);

        Assert.DoesNotContain(RescueUnitType.RescueBoat, types!);
        Assert.Contains(RescueUnitType.FireTruck, types!);
    }

    [Fact]
    public void MatchingUnitTypes_Unknown_ReturnsNull()
    {
        Assert.Null(DispatchScoring.MatchingUnitTypes(IncidentEventType.None, new[] { "other" }, "nothing", SeverityLevel.Level1));
    }
}
