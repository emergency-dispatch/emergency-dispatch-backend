using EmergencyDispatch.Application.DTOs.Dispatch;

namespace EmergencyDispatch.Application.Interfaces;

/// <summary>
/// Quãng đường / thời gian theo đường bộ của một xe tới hiện trường
/// </summary>
public sealed record RoadLeg(double DistanceKm, double DurationMinutes);

/// <summary>
/// Tuyến đường bộ để vẽ live map. IsApproximate = true khi máy chủ chỉ đường lỗi:
/// Points chỉ là đoạn thẳng 2 điểm (FE vẽ nét đứt), quãng đường / thời gian tính theo chim bay.
/// </summary>
public sealed class RouteResult
{
    public List<RoutePointDto> Points { get; init; } = new();
    public double DistanceKm { get; init; }
    public double DurationMinutes { get; init; }
    public bool IsApproximate { get; init; }

    /// <summary>
    /// Các bước rẽ (chỉ có khi gọi GetNavigationRouteAsync và máy chủ chỉ đường trả về được)
    /// </summary>
    public List<RouteStepDto> Steps { get; init; } = new();
}

/// <summary>
/// Chỉ đường theo đường bộ (OSRM). Địa chỉ máy chủ cấu hình ở Routing:OsrmBaseUrl.
/// </summary>
public interface IRoutingService
{
    Task<RouteResult> GetRouteAsync(double fromLat, double fromLng, double toLat, double toLng, CancellationToken cancellationToken = default);

    /// <summary>
    /// Như GetRouteAsync nhưng kèm các bước rẽ (OSRM steps=true) cho màn dẫn đường của tổ
    /// </summary>
    Task<RouteResult> GetNavigationRouteAsync(double fromLat, double fromLng, double toLat, double toLng, CancellationToken cancellationToken = default);

    /// <summary>
    /// Quãng đường / thời gian đường bộ từ nhiều điểm xuất phát tới một đích (một lần gọi OSRM /table).
    /// Phần tử null = không tính được cho xe đó; trả về null nếu máy chủ chỉ đường không phản hồi.
    /// </summary>
    Task<IReadOnlyList<RoadLeg?>?> GetLegsToAsync(IReadOnlyList<(double Lat, double Lng)> origins, double toLat, double toLng, CancellationToken cancellationToken = default);
}
