namespace EmergencyDispatch.Application.DTOs.Settings;

/// <summary>
/// Khu vực phục vụ của hệ thống (VD: Khu Công nghệ cao): tâm + bán kính, hoặc đa giác ranh giới (PostGIS).
/// Có BoundaryGeoJson thì kiểm tra trong / ngoài vùng theo đa giác; không có thì theo hình tròn.
/// SOS ngoài vùng bị từ chối khi bật RejectOutside; nếu tắt thì vẫn tiếp nhận nhưng gắn cờ và báo Operator / Admin.
/// Sự cố do Operator tạo (điện thoại, bộ đàm) không bị chặn.
/// </summary>
public class ServiceAreaSettingsDto
{
    /// <summary>
    /// Chưa bật thì không kiểm tra (mặc định tắt cho tới khi Admin cấu hình)
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Từ chối SOS của người dân / khách nằm ngoài khu vực (mặc định bật). Tắt = tiếp nhận nhưng gắn cờ ngoài vùng.
    /// </summary>
    public bool RejectOutside { get; set; } = true;

    public string Name { get; set; } = "Khu vực phục vụ";
    public double CenterLat { get; set; }
    public double CenterLng { get; set; }
    public double RadiusKm { get; set; } = 10;

    /// <summary>
    /// Ranh giới thật của khu vực (tùy chọn): GeoJSON Polygon / MultiPolygon, tọa độ [kinh độ, vĩ độ] (SRID 4326).
    /// Ví dụ: {"type":"Polygon","coordinates":[[[106.78,10.84],[106.81,10.84],[106.81,10.86],[106.78,10.86],[106.78,10.84]]]}.
    /// Có giá trị thì geofencing dùng PostGIS ST_Distance tới đa giác; tâm + bán kính vẫn dùng để hiển thị bản đồ
    /// </summary>
    public string? BoundaryGeoJson { get; set; }
}
