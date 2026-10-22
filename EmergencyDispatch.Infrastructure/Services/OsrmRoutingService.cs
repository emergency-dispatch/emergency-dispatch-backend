using System.Globalization;
using System.Text;
using System.Text.Json;
using EmergencyDispatch.Application.DTOs.Dispatch;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Infrastructure.Services;

/// <summary>
/// Chỉ đường theo đường bộ bằng OSRM (API /route và /table).
/// Dùng được với máy chủ demo công cộng hoặc OSRM tự host (Docker, bản đồ Việt Nam) — chỉ cần đổi Routing:OsrmBaseUrl.
/// </summary>
public class OsrmRoutingService : IRoutingService
{
    public const string HttpClientName = "Routing";
    public const string DefaultBaseUrl = "https://router.project-osrm.org";

    // Vận tốc trung bình nội khu khi phải ước lượng theo chim bay
    private const double FallbackSpeedKmh = 35.0;
    private static readonly TimeSpan RouteCacheDuration = TimeSpan.FromSeconds(60);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<OsrmRoutingService> _logger;
    private readonly string _baseUrl;

    public OsrmRoutingService(IHttpClientFactory httpClientFactory, IMemoryCache cache, IConfiguration configuration, ILogger<OsrmRoutingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _logger = logger;
        _baseUrl = (configuration["Routing:OsrmBaseUrl"] ?? DefaultBaseUrl).TrimEnd('/');
    }

    private static string Coord(double lat, double lng) => string.Create(CultureInfo.InvariantCulture, $"{lng:F6},{lat:F6}");

    public async Task<RouteResult> GetRouteAsync(double fromLat, double fromLng, double toLat, double toLng, CancellationToken cancellationToken = default)
    {
        // Làm tròn 4 chữ số (~11 m) để các lần gọi liên tiếp khi xe đứng yên dùng lại kết quả
        var key = string.Create(CultureInfo.InvariantCulture, $"route:{fromLat:F4},{fromLng:F4}:{toLat:F4},{toLng:F4}");
        if (_cache.TryGetValue(key, out RouteResult? cached) && cached != null)
        {
            return cached;
        }

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var url = $"{_baseUrl}/route/v1/driving/{Coord(fromLat, fromLng)};{Coord(toLat, toLng)}?overview=full&geometries=geojson";
            using var response = await client.GetAsync(url, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                if (doc.RootElement.TryGetProperty("routes", out var routes) && routes.GetArrayLength() > 0)
                {
                    var route = routes[0];
                    var points = route.GetProperty("geometry").GetProperty("coordinates").EnumerateArray()
                        .Select(c => new RoutePointDto(Math.Round(c[1].GetDouble(), 6), Math.Round(c[0].GetDouble(), 6)))
                        .ToList();

                    var result = new RouteResult
                    {
                        Points = points,
                        DistanceKm = Math.Round(route.GetProperty("distance").GetDouble() / 1000.0, 3),
                        DurationMinutes = Math.Round(route.GetProperty("duration").GetDouble() / 60.0, 1),
                        IsApproximate = false
                    };
                    _cache.Set(key, result, RouteCacheDuration);
                    return result;
                }
            }
            else
            {
                _logger.LogWarning("OSRM /route trả về {Status}", (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Không gọi được máy chủ chỉ đường {Url}: {Message}", _baseUrl, ex.Message);
        }

        return Approximate(fromLat, fromLng, toLat, toLng);
    }

    public async Task<IReadOnlyList<RoadLeg?>?> GetLegsToAsync(IReadOnlyList<(double Lat, double Lng)> origins, double toLat, double toLng, CancellationToken cancellationToken = default)
    {
        if (origins.Count == 0)
        {
            return Array.Empty<RoadLeg?>();
        }

        try
        {
            var coords = new StringBuilder();
            foreach (var (lat, lng) in origins)
            {
                coords.Append(Coord(lat, lng)).Append(';');
            }
            coords.Append(Coord(toLat, toLng));

            var sources = string.Join(';', Enumerable.Range(0, origins.Count));
            var url = $"{_baseUrl}/table/v1/driving/{coords}?sources={sources}&destinations={origins.Count}&annotations=duration,distance";

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OSRM /table trả về {Status}", (int)response.StatusCode);
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var durations = doc.RootElement.GetProperty("durations");
            var distances = doc.RootElement.TryGetProperty("distances", out var d) ? d : default;

            var legs = new List<RoadLeg?>(origins.Count);
            for (var i = 0; i < origins.Count; i++)
            {
                var dur = durations[i][0];
                var dist = distances.ValueKind == JsonValueKind.Array ? distances[i][0] : default;
                legs.Add(dur.ValueKind == JsonValueKind.Number && dist.ValueKind == JsonValueKind.Number
                    ? new RoadLeg(Math.Round(dist.GetDouble() / 1000.0, 3), Math.Round(dur.GetDouble() / 60.0, 1))
                    : null);
            }
            return legs;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Không gọi được OSRM /table tại {Url}: {Message}", _baseUrl, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Máy chủ chỉ đường lỗi: đoạn thẳng 2 điểm (không vẽ đường cong giả), quãng đường chim bay
    /// </summary>
    private static RouteResult Approximate(double fromLat, double fromLng, double toLat, double toLng)
    {
        var km = HaversineKm(fromLat, fromLng, toLat, toLng);
        return new RouteResult
        {
            Points = new List<RoutePointDto> { new(fromLat, fromLng), new(toLat, toLng) },
            DistanceKm = Math.Round(km, 3),
            DurationMinutes = Math.Round(km / FallbackSpeedKmh * 60.0, 1),
            IsApproximate = true
        };
    }

    public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
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
