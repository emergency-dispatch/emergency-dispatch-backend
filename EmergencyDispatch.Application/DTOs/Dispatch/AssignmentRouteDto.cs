namespace EmergencyDispatch.Application.DTOs.Dispatch;

/// <summary>
/// Một bước chỉ đường (OSRM step). FE tự dựng câu hướng dẫn theo ngôn ngữ giao diện từ Type / Modifier / RoadName
/// </summary>
public class RouteStepDto
{
    /// <summary>
    /// Loại thao tác OSRM: depart, turn, new name, continue, merge, on ramp, off ramp, fork, end of road, roundabout, rotary, arrive...
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Hướng: left, right, slight left, slight right, sharp left, sharp right, straight, uturn (có thể rỗng)
    /// </summary>
    public string? Modifier { get; set; }

    /// <summary>
    /// Tên đường của bước (có thể rỗng nếu bản đồ không có tên)
    /// </summary>
    public string RoadName { get; set; } = string.Empty;

    public double DistanceMeters { get; set; }
    public double DurationSeconds { get; set; }

    /// <summary>
    /// Điểm thực hiện thao tác
    /// </summary>
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

/// <summary>
/// Tuyến đường bộ cho tổ đang làm nhiệm vụ (GET /api/dispatch/assignments/{id}/route), backend gọi OSRM thay cho trình duyệt
/// </summary>
public class AssignmentRouteDto
{
    public Guid AssignmentId { get; set; }
    public double FromLat { get; set; }
    public double FromLng { get; set; }
    public double ToLat { get; set; }
    public double ToLng { get; set; }

    /// <summary>
    /// Đường đi (lat, lng) để vẽ polyline
    /// </summary>
    public List<RoutePointDto> Points { get; set; } = new();

    /// <summary>
    /// Các bước rẽ; rỗng khi IsApproximate = true
    /// </summary>
    public List<RouteStepDto> Steps { get; set; } = new();

    public double DistanceKm { get; set; }
    public double DurationMinutes { get; set; }

    /// <summary>
    /// true = máy chủ chỉ đường lỗi: Points là đoạn thẳng 2 điểm, quãng đường / thời gian theo chim bay (FE vẽ nét đứt)
    /// </summary>
    public bool IsApproximate { get; set; }
}
