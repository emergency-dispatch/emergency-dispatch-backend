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
    /// Số nhiệm vụ đang đảm trách của chính xe này
    /// </summary>
    public int ActiveTasksCount { get; set; }

    /// <summary>
    /// Tỷ lệ xe đang bận của trạm trực thuộc (0–1) — thành phần LoadRatio(S) của Load-Aware
    /// </summary>
    public double StationLoad { get; set; }

    /// <summary>
    /// Số sự cố trạm đang xử lý — thành phần ActiveMissions(S) của Load-Aware (trước chuẩn hóa)
    /// </summary>
    public int StationActiveMissions { get; set; }

    /// <summary>
    /// Chi phí Load-Aware (BR-11): 0.5·D/R + 0.3·LoadRatio + 0.2·ActiveMissions. Càng thấp càng ưu tiên.
    /// </summary>
    public double Cost { get; set; }

    /// <summary>
    /// Điểm hiển thị 0–100 = (1 − Cost) × 100 (càng cao càng được ưu tiên)
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Bán kính tìm kiếm (km) đã dùng để tìm ra xe này
    /// </summary>
    public double SearchRadiusKm { get; set; }

    /// <summary>
    /// true nếu đã phải mở rộng bán kính vượt nấc đầu tiên (BR-12)
    /// </summary>
    public bool RadiusExpanded { get; set; }

    /// <summary>
    /// Thuật toán được sử dụng để xếp hạng
    /// </summary>
    public DispatchAlgorithm AlgorithmUsed { get; set; }

    /// <summary>
    /// Diễn giải lý do đề xuất
    /// </summary>
    public string RecommendationReason { get; set; } = string.Empty;

    /// <summary>
    /// Quãng đường theo đường bộ (OSRM); null khi máy chủ chỉ đường lỗi
    /// </summary>
    public double? RoadDistanceKm { get; set; }

    /// <summary>
    /// Thời gian chạy xe theo đường bộ (phút, chưa gồm thời gian xuất xe)
    /// </summary>
    public double? RoadDurationMinutes { get; set; }

    /// <summary>
    /// true = ETA / quãng đường ước lượng theo chim bay vì không gọi được máy chủ chỉ đường
    /// </summary>
    public bool IsApproximate { get; set; }

    /// <summary>
    /// Xe thuộc trạm khác trạm phụ trách sự cố (điều xe hỗ trợ)
    /// </summary>
    public bool IsSupportUnit { get; set; }
}
