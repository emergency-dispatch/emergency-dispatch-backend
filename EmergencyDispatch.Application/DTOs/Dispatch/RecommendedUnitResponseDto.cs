using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Dispatch;

/// <summary>
/// DTO chứa thông tin phương tiện cứu hộ được thuật toán đề xuất (RQ2)
/// </summary>
public class RecommendedUnitResponseDto
{
    public Guid RescueUnitId { get; set; }
    public string PlateNumber { get; set; } = string.Empty;
    public RescueUnitType UnitType { get; set; }
    public RescueUnitStatus Status { get; set; }
    public Guid StationId { get; set; }
    public string StationName { get; set; } = string.Empty;
    public double CurrentLat { get; set; }
    public double CurrentLng { get; set; }

    /// <summary>
    /// Khoảng cách đường thẳng địa lý (km) từ vị trí xe đến hiện trường sự cố
    /// </summary>
    public double DistanceKm { get; set; }

    /// <summary>
    /// Ước tính thời gian di chuyển (phút) dựa trên vận tốc trung bình nội khu (30-40 km/h)
    /// </summary>
    public double EstimatedEtaMinutes { get; set; }

    /// <summary>
    /// Số nhiệm vụ đang đảm trách hoặc tải công việc của xe/trạm (phục vụ thuật toán Load-Aware)
    /// </summary>
    public int ActiveTasksCount { get; set; }

    /// <summary>
    /// Điểm đánh giá ưu tiên của thuật toán (điểm càng cao càng được ưu tiên)
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Thuật toán được sử dụng để xếp hạng
    /// </summary>
    public DispatchAlgorithm AlgorithmUsed { get; set; }

    /// <summary>
    /// Diễn giải lý do đề xuất
    /// </summary>
    public string RecommendationReason { get; set; } = string.Empty;
}
