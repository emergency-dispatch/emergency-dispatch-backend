using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Infrastructure.Services;

/// <summary>
/// Geofencing bằng PostGIS: ranh giới khu vực phục vụ là đa giác GeoJSON (SRID 4326),
/// khoảng cách ngoài vùng = ST_Distance(geography(ranh giới), geography(điểm)) — bằng 0 khi điểm nằm trong đa giác
/// </summary>
public class PostGisSpatialQueryService : ISpatialQueryService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<PostGisSpatialQueryService> _logger;

    public PostGisSpatialQueryService(ApplicationDbContext context, ILogger<PostGisSpatialQueryService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<double?> DistanceOutsideBoundaryKmAsync(string boundaryGeoJson, double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        try
        {
            var meters = await _context.Database.SqlQuery<double>($@"
                SELECT ST_Distance(
                    ST_SetSRID(ST_GeomFromGeoJSON({boundaryGeoJson}), 4326)::geography,
                    ST_SetSRID(ST_MakePoint({longitude}, {latitude}), 4326)::geography) AS ""Value""")
                .SingleAsync(cancellationToken);
            return Math.Round(meters / 1000.0, 2);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Không tính được khoảng cách tới ranh giới khu vực phục vụ (PostGIS). Dùng hình tròn tâm + bán kính.");
            return null;
        }
    }

    public async Task<bool> IsValidBoundaryAsync(string boundaryGeoJson, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.Database.SqlQuery<bool>($@"
                SELECT (ST_IsValid(g) AND GeometryType(g) IN ('POLYGON', 'MULTIPOLYGON')) AS ""Value""
                FROM (SELECT ST_GeomFromGeoJSON({boundaryGeoJson}) AS g) AS boundary")
                .SingleAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogInformation("GeoJSON ranh giới không hợp lệ: {Message}", ex.Message);
            return false;
        }
    }
}
