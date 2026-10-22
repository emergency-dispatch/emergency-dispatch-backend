using EmergencyDispatch.Application.DTOs.Settings;
using EmergencyDispatch.Domain.Entities;

namespace EmergencyDispatch.Application.Services;

/// <summary>
/// Các hàm tính thuần cho mô hình "mỗi Operator phụ trách 1 trạm":
/// chọn trạm phụ trách theo vị trí sự cố và kiểm tra khu vực phục vụ.
/// </summary>
public static class StationAssignment
{
    /// <summary>
    /// Trạm phụ trách = trạm đang hoạt động gần nhất có bán kính phủ chứa vị trí;
    /// không trạm nào phủ tới thì lấy trạm đang hoạt động gần nhất. Null nếu chưa có trạm nào.
    /// </summary>
    public static Station? PickResponsibleStation(IEnumerable<Station> stations, double lat, double lng)
    {
        var ranked = stations
            .Where(s => s.IsActive && !s.IsDeleted)
            .Select(s => (Station: s, Km: DistanceKm(lat, lng, s.Latitude, s.Longitude)))
            .OrderBy(x => x.Km)
            .ToList();

        if (ranked.Count == 0)
        {
            return null;
        }

        var covering = ranked.FirstOrDefault(x => x.Km <= x.Station.CoverageRadiusKm);
        return covering.Station ?? ranked[0].Station;
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
