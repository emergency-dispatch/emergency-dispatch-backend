using EmergencyDispatch.Application.DTOs.Settings;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Application.Services;

/// <summary>
/// Các hàm hỗ trợ cho mô hình "mỗi Operator phụ trách 1 trạm":
/// chọn trạm phụ trách theo vị trí sự cố và kiểm tra khu vực phục vụ.
/// </summary>
public static class StationAssignment
{
    /// <summary>
    /// Chọn trạm theo quãng đường bộ từ trạm tới sự cố, không xét CoverageRadiusKm.
    /// Select by station-to-incident road distance; station coverage radii are not used.
    /// Khi không có tuyến hợp lệ trong 2 giây, chọn gần nhất theo tọa độ và ghi log dự phòng.
    /// If no valid road route is available within 2 seconds, log and use straight-line proximity.
    /// </summary>
    public static async Task<Station?> PickResponsibleStationAsync(
        IEnumerable<Station> stations, double lat, double lng,
        IRoutingService routingService, ILogger logger,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var candidates = stations
            .Where(s => s.IsActive && !s.IsDeleted)
            .ToList();

        // Một trạm duy nhất thì không cần gọi định tuyến để so sánh.
        // A single station needs no routing comparison.
        if (candidates.Count <= 1)
        {
            return candidates.FirstOrDefault();
        }

        IReadOnlyList<RoadLeg?>? legs = null;
        using var routingTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        routingTimeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            legs = await routingService.GetLegsToAsync(
                candidates.Select(s => (s.Latitude, s.Longitude)).ToList(),
                lat, lng, routingTimeout.Token);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Station road-distance lookup failed or timed out.");
        }
        cancellationToken.ThrowIfCancellationRequested();

        // Không trộn km đường bộ với km chim bay khi chỉ một số trạm có tuyến.
        // Compare only valid road routes; never mix road and straight-line distances.
        var routed = candidates
            .Select((station, i) => new { Station = station, Leg = legs != null && i < legs.Count ? legs[i] : null })
            .Where(x => x.Leg != null && double.IsFinite(x.Leg.DistanceKm) && x.Leg.DistanceKm >= 0)
            .OrderBy(x => x.Leg!.DistanceKm)
            .ThenBy(x => double.IsFinite(x.Leg!.DurationMinutes) && x.Leg.DurationMinutes >= 0
                ? x.Leg.DurationMinutes : double.MaxValue)
            .ThenBy(x => x.Station.Id)
            .FirstOrDefault();

        if (routed != null)
        {
            logger.LogInformation("Assigned station {StationId} by road distance: {DistanceKm} km.",
                routed.Station.Id, routed.Leg!.DistanceKm);
            return routed.Station;
        }

        var fallback = candidates.OrderBy(s => DistanceKm(lat, lng, s.Latitude, s.Longitude))
            .ThenBy(s => s.Id).First();
        logger.LogWarning("No valid road route available. Assigned station {StationId} by APPROXIMATE straight-line proximity; this is not a road-distance result.",
            fallback.Id);
        return fallback;
    }

    /// <summary>
    /// Khoảng cách (km) vị trí nằm ngoài khu vực phục vụ; 0 = trong vùng hoặc chưa bật kiểm tra
    /// </summary>
    public static double DistanceOutsideArea(ServiceAreaSettingsDto area, double lat, double lng)
    {
        if (!area.Enabled || !(area.RadiusKm > 0))
        {
            return 0;
        }

        var outside = DistanceKm(lat, lng, area.CenterLat, area.CenterLng) - area.RadiusKm;
        return outside > 0 ? Math.Round(outside, 2) : 0;
    }

    public static List<string> ValidateServiceArea(ServiceAreaSettingsDto s)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(s.Name)) errors.Add("Vui lòng nhập tên khu vực.");
        if (s.CenterLat is < -90 or > 90) errors.Add("Vĩ độ tâm không hợp lệ.");
        if (s.CenterLng is < -180 or > 180) errors.Add("Kinh độ tâm không hợp lệ.");
        if (s.Enabled && s.CenterLat == 0 && s.CenterLng == 0) errors.Add("Vui lòng chọn tâm khu vực trên bản đồ.");
        if (!(s.RadiusKm >= 1 && s.RadiusKm <= 100)) errors.Add("Bán kính khu vực phải từ 1 đến 100 km.");
        return errors;
    }

    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6371.0;
        static double Rad(double deg) => deg * Math.PI / 180.0;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return r * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
