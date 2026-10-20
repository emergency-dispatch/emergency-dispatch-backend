namespace EmergencyDispatch.Application.DTOs.Dispatch;

public class RoutePointDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public RoutePointDto() { }

    public RoutePointDto(double lat, double lng)
    {
        Latitude = lat;
        Longitude = lng;
    }
}

/// <summary>
/// DTO theo dõi lộ trình và xe cứu hộ thời gian thực dành cho Người dân (Citizen Live Tracking)
/// </summary>
public class CitizenTrackingResponseDto
{
    public Guid IncidentId { get; set; }
    public string IncidentTitle { get; set; } = string.Empty;
    public string IncidentAddress { get; set; } = string.Empty;
    public double IncidentLat { get; set; }
    public double IncidentLng { get; set; }
    public string IncidentStatus { get; set; } = string.Empty;
    public string IncidentSeverity { get; set; } = string.Empty;

    // Thông tin xe & trạm cứu hộ
    public Guid? RescueUnitId { get; set; }
    public string? PlateNumber { get; set; }
    public string? UnitTypeName { get; set; }
    public string? StationName { get; set; }
    public string? StationPhoneNumber { get; set; }

    // Tọa độ GPS hiện tại của xe
    public double? VehicleLat { get; set; }
    public double? VehicleLng { get; set; }
    public DateTime? LastLocationUpdateAt { get; set; }

    // Đường di chuyển cứu hộ (Route trajectory) & Thời gian dự kiến (ETA)
    public List<RoutePointDto> RouteTrajectory { get; set; } = new();
    public double DistanceKm { get; set; }
    public int EstimatedArrivalMinutes { get; set; }

    // Trạng thái điều phối & Các mốc thời gian
    public string DispatchStatus { get; set; } = string.Empty;
    public DateTime? DispatchedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? ArrivedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Notes { get; set; }
}
