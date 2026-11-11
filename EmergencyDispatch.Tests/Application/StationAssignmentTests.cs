using EmergencyDispatch.Application.DTOs.Settings;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EmergencyDispatch.Tests.Application;

public class StationAssignmentTests
{
    private static Station St(string name, double lat, double lng, double radius = 10, bool active = true, bool deleted = false)
        => new() { Name = name, Latitude = lat, Longitude = lng, CoverageRadiusKm = radius, IsActive = active, IsDeleted = deleted };

    [Fact]
    public void DistanceKm_SamePoint_IsZero()
    {
        Assert.Equal(0, StationAssignment.DistanceKm(10.77, 106.70, 10.77, 106.70), 6);
    }

    [Fact]
    public void DistanceKm_OneDegreeLatitude_IsAbout111Km()
    {
        Assert.InRange(StationAssignment.DistanceKm(10, 106, 11, 106), 111.0, 111.4);
    }

    [Fact]
    public void DistanceKm_District1ToDistrict7_IsAbout5Km()
    {
        var km = StationAssignment.DistanceKm(10.776889, 106.700806, 10.732842, 106.719688);

        Assert.InRange(km, 5.0, 5.6);
    }

    [Fact]
    public async Task PickResponsibleStation_UsesRoadDistance_NotStraightLineOrTravelTime()
    {
        var near = St("Near by air", 10.0001, 106);
        var farther = St("Shorter road", 10.01, 106);
        var routing = Routing(new RoadLeg(8, 5), new RoadLeg(4, 10));

        var selected = await Pick(new[] { near, farther }, routing.Object);

        Assert.Same(farther, selected);
        routing.Verify(r => r.GetLegsToAsync(
            It.Is<IReadOnlyList<(double Lat, double Lng)>>(p => p.Count == 2
                && p[0].Lat == near.Latitude && p[0].Lng == near.Longitude
                && p[1].Lat == farther.Latitude && p[1].Lng == farther.Longitude),
            10, 106, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PickResponsibleStation_DoesNotUseCoverageRadius()
    {
        var outsideOwnRadius = St("Small radius", 10.01, 106, radius: 0.001);
        var covering = St("Large radius", 10.02, 106, radius: 100);

        Assert.Same(outsideOwnRadius, await Pick(new[] { outsideOwnRadius, covering },
            Routing(new RoadLeg(2, 4), new RoadLeg(4, 8)).Object));
    }

    [Fact]
    public async Task PickResponsibleStation_IgnoresInactiveAndDeleted()
    {
        var inactive = St("Inactive", 10, 106, active: false);
        var deleted = St("Deleted", 10, 106, deleted: true);
        var ok = St("Ok", 10.01, 106);
        var other = St("Other", 10.02, 106);
        var routing = Routing(new RoadLeg(1, 2), new RoadLeg(2, 4));

        Assert.Same(ok, await Pick(new[] { inactive, deleted, ok, other }, routing.Object));
        routing.Verify(r => r.GetLegsToAsync(It.Is<IReadOnlyList<(double Lat, double Lng)>>(p => p.Count == 2
            && p[0].Lat == ok.Latitude && p[1].Lat == other.Latitude), 10, 106, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PickResponsibleStation_NoStationsOrSingleStation_NeedsNoRouting()
    {
        var routing = new Mock<IRoutingService>(MockBehavior.Strict);
        Assert.Null(await Pick(Array.Empty<Station>(), routing.Object));
        var only = St("Only", 10.01, 106, radius: 0);
        Assert.Same(only, await Pick(new[] { only }, routing.Object));
        routing.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PickResponsibleStation_MissingRoadForNearerStation_UsesKnownRoadOnly()
    {
        var near = St("No road result", 10.0001, 106);
        var routed = St("Has road", 10.01, 106);

        Assert.Same(routed, await Pick(new[] { near, routed }, Routing(null, new RoadLeg(10, 20)).Object));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task PickResponsibleStation_InvalidRoadDistance_IsNotSelected(double distance)
    {
        var invalid = St("Invalid road", 10.0001, 106);
        var valid = St("Valid road", 10.01, 106);

        Assert.Same(valid, await Pick(new[] { invalid, valid },
            Routing(new RoadLeg(distance, 1), new RoadLeg(4, 8)).Object));
    }

    [Fact]
    public async Task PickResponsibleStation_EqualRoadDistance_UsesDurationThenStableId()
    {
        var slow = St("Slow", 10.01, 106);
        var fast = St("Fast", 10.02, 106);
        Assert.Same(fast, await Pick(new[] { slow, fast }, Routing(new RoadLeg(4, 10), new RoadLeg(4, 5)).Object));

        var expected = new[] { slow, fast }.OrderBy(s => s.Id).First();
        Assert.Same(expected, await Pick(new[] { slow, fast }, Routing(new RoadLeg(4, 5), new RoadLeg(4, 5)).Object));
        Assert.Same(expected, await Pick(new[] { fast, slow }, Routing(new RoadLeg(4, 5), new RoadLeg(4, 5)).Object));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PickResponsibleStation_NoRoads_FallsBackAndLogsApproximation(bool throwError)
    {
        var near = St("Near", 10.0001, 106, radius: 0);
        var far = St("Far", 10.02, 106, radius: 100);
        var routing = Routing(null, null);
        if (throwError)
            routing.Setup(r => r.GetLegsToAsync(It.IsAny<IReadOnlyList<(double Lat, double Lng)>>(),
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("OSRM unavailable"));
        var logger = new Mock<ILogger>();

        Assert.Same(near, await StationAssignment.PickResponsibleStationAsync(new[] { near, far },
            10, 106, routing.Object, logger.Object));
        logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((value, _) => value != null && value.ToString()!.Contains("APPROXIMATE")),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task PickResponsibleStation_NullServiceResponse_FallsBack()
    {
        var near = St("Near", 10.0001, 106);
        var far = St("Far", 10.02, 106);
        Assert.Same(near, await Pick(new[] { far, near }, Mock.Of<IRoutingService>()));
    }

    [Fact]
    public async Task PickResponsibleStation_RoutingTimeout_DoesNotBlockSos()
    {
        var routing = new Mock<IRoutingService>();
        routing.Setup(r => r.GetLegsToAsync(It.IsAny<IReadOnlyList<(double Lat, double Lng)>>(),
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .Returns(async (IReadOnlyList<(double Lat, double Lng)> _, double _, double _, CancellationToken ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return (IReadOnlyList<RoadLeg?>?)null;
            });
        var near = St("Near", 10.0001, 106);
        var selected = await Pick(new[] { near, St("Far", 10.02, 106) }, routing.Object).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(near, selected);
    }

    [Fact]
    public async Task PickResponsibleStation_CallerCancellation_IsPropagated()
    {
        using var cts = new CancellationTokenSource();
        var routing = new Mock<IRoutingService>();
        routing.Setup(r => r.GetLegsToAsync(It.IsAny<IReadOnlyList<(double Lat, double Lng)>>(),
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<(double Lat, double Lng)> _, double _, double _, CancellationToken ct) =>
            {
                cts.Cancel();
                return Task.FromCanceled<IReadOnlyList<RoadLeg?>?>(ct);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => StationAssignment.PickResponsibleStationAsync(
            new[] { St("A", 10.01, 106), St("B", 10.02, 106) }, 10, 106,
            routing.Object, NullLogger.Instance, cts.Token));
    }

    private static Mock<IRoutingService> Routing(params RoadLeg?[] legs)
    {
        var routing = new Mock<IRoutingService>();
        routing.Setup(r => r.GetLegsToAsync(It.IsAny<IReadOnlyList<(double Lat, double Lng)>>(),
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(legs);
        return routing;
    }

    private static Task<Station?> Pick(IEnumerable<Station> stations, IRoutingService routing)
        => StationAssignment.PickResponsibleStationAsync(stations, 10, 106, routing, NullLogger.Instance);

    [Fact]
    public void DistanceOutsideArea_DisabledOrInside_IsZero()
    {
        var area = new ServiceAreaSettingsDto { Enabled = false, CenterLat = 10.77, CenterLng = 106.70, RadiusKm = 1 };
        Assert.Equal(0, StationAssignment.DistanceOutsideArea(area, 11.5, 107));

        area.Enabled = true;
        area.RadiusKm = 10;
        Assert.Equal(0, StationAssignment.DistanceOutsideArea(area, 10.78, 106.70));
    }

    [Fact]
    public void DistanceOutsideArea_Outside_ReturnsKmBeyondRadius()
    {
        var area = new ServiceAreaSettingsDto { Enabled = true, CenterLat = 10, CenterLng = 106, RadiusKm = 100 };

        Assert.InRange(StationAssignment.DistanceOutsideArea(area, 11, 106), 11.0, 11.4);
    }

    [Fact]
    public void ValidateServiceArea_ValidSettings_HasNoErrors()
    {
        var s = new ServiceAreaSettingsDto { Enabled = true, Name = "HCM", CenterLat = 10.77, CenterLng = 106.7, RadiusKm = 20 };

        Assert.Empty(StationAssignment.ValidateServiceArea(s));
    }

    [Fact]
    public void ValidateServiceArea_ReportsEveryInvalidField()
    {
        var s = new ServiceAreaSettingsDto { Enabled = true, Name = " ", CenterLat = 0, CenterLng = 0, RadiusKm = 0 };

        var errors = StationAssignment.ValidateServiceArea(s);

        Assert.Contains("Vui lòng nhập tên khu vực.", errors);
        Assert.Contains("Vui lòng chọn tâm khu vực trên bản đồ.", errors);
        Assert.Contains("Bán kính khu vực phải từ 1 đến 100 km.", errors);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(0, 181)]
    public void ValidateServiceArea_OutOfRangeCoordinates(double lat, double lng)
    {
        var s = new ServiceAreaSettingsDto { Enabled = false, Name = "x", CenterLat = lat, CenterLng = lng, RadiusKm = 10 };

        Assert.NotEmpty(StationAssignment.ValidateServiceArea(s));
    }
}
