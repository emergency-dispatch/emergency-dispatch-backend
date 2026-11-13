namespace EmergencyDispatch.Application.Interfaces;

/// <summary>
/// Truy vấn không gian chạy trong PostGIS (geofencing theo đa giác ranh giới khu vực phục vụ)
/// </summary>
public interface ISpatialQueryService
{
    /// <summary>
    /// Khoảng cách (km) từ điểm tới đa giác ranh giới (GeoJSON Polygon / MultiPolygon, tọa độ [kinh độ, vĩ độ]);
    /// 0 = nằm trong vùng. ST_Distance trên geography. null = GeoJSON lỗi / không truy vấn được
    /// </summary>
    Task<double?> DistanceOutsideBoundaryKmAsync(string boundaryGeoJson, double latitude, double longitude, CancellationToken cancellationToken = default);

    /// <summary>
    /// GeoJSON là Polygon / MultiPolygon hợp lệ (ST_GeomFromGeoJSON + ST_IsValid)
    /// </summary>
    Task<bool> IsValidBoundaryAsync(string boundaryGeoJson, CancellationToken cancellationToken = default);
}
