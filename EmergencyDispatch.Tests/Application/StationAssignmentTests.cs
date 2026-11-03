using EmergencyDispatch.Application.DTOs.Settings;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Entities;

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
    public void PickResponsibleStation_PicksNearestCoveringStation()
    {
        var q1 = St("Q1", 10.776889, 106.700806);
        var q7 = St("Q7", 10.732842, 106.719688);

        Assert.Same(q7, StationAssignment.PickResponsibleStation(new[] { q1, q7 }, 10.7335, 106.7192));
        Assert.Same(q1, StationAssignment.PickResponsibleStation(new[] { q1, q7 }, 10.7770, 106.7010));
    }

    [Fact]
    public void PickResponsibleStation_NoneCovering_FallsBackToNearest()
    {
        var far = St("Far", 11.5, 107.5, radius: 1);
        var farther = St("Farther", 12.5, 108.5, radius: 1);

        Assert.Same(far, StationAssignment.PickResponsibleStation(new[] { farther, far }, 10.77, 106.70));
    }

    [Fact]
    public void PickResponsibleStation_IgnoresInactiveAndDeleted()
    {
        var inactive = St("Inactive", 10.7335, 106.7192, active: false);
        var deleted = St("Deleted", 10.7335, 106.7192, deleted: true);
        var ok = St("Ok", 10.80, 106.70);

        Assert.Same(ok, StationAssignment.PickResponsibleStation(new[] { inactive, deleted, ok }, 10.7335, 106.7192));
    }

    [Fact]
    public void PickResponsibleStation_NoStations_ReturnsNull()
    {
        Assert.Null(StationAssignment.PickResponsibleStation(Array.Empty<Station>(), 10, 106));
    }

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
