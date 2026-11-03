using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Thực thể Điểm nghẽn giao thông / Chướng ngại vật nội khu (Traffic Hotspot)
/// Do người dân, công nhân hoặc nhân viên tuần tra báo cáo. Có cơ chế tự động hết hạn (TTL 30-45 phút).
/// </summary>
public class TrafficHotspot : BaseEntity
{
    /// <summary>
    /// Tiêu đề điểm nghẽn (vd: "Ùn tắc cổng D1", "Cây đổ chắn đường D2")
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả chi tiết chướng ngại vật hoặc nguyên nhân kẹt xe
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Vĩ độ GPS vị trí điểm nghẽn
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Kinh độ GPS vị trí điểm nghẽn
    /// </summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Địa chỉ hoặc tên tuyến đường hiển thị
    /// </summary>
    public string LocationAddress { get; set; } = string.Empty;

    /// <summary>
    /// Mức độ ùn tắc / nghiêm trọng của chướng ngại vật
    /// </summary>
    public TrafficCongestionLevel CongestionLevel { get; set; } = TrafficCongestionLevel.Moderate;

    /// <summary>
    /// Ảnh chụp hiện trường kẹt xe / chướng ngại vật
    /// </summary>
    public string? MediaUrl { get; set; }

    /// <summary>
    /// Người dùng báo cáo (nếu đã đăng nhập)
    /// </summary>
    public Guid? ReportedByUserId { get; set; }
    public User? ReportedByUser { get; set; }

    /// <summary>
    /// Tên người báo (lưu độc lập cho khách vãng lai)
    /// </summary>
    public string? ReporterName { get; set; }

    /// <summary>
    /// Số điện thoại liên hệ
    /// </summary>
    public string? ReporterPhone { get; set; }

    /// <summary>
    /// Thời điểm hết hạn hiển thị trên bản đồ điều phối (TTL 30 - 45 phút)
    /// </summary>
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(45);

    /// <summary>
    /// Cờ trạng thái đang hoạt động (true: còn hiệu lực, false: đã hết hạn hoặc đã được giải tỏa)
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Thời điểm giải tỏa điểm nghẽn thủ công (nếu có)
    /// </summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>
    /// Người giải tỏa điểm nghẽn (Operator hoặc tuần tra viên)
    /// </summary>
    public Guid? ResolvedByUserId { get; set; }

    /// <summary>
    /// Trạng thái vòng đời (IsActive = true khi Active hoặc Assigned)
    /// </summary>
    public TrafficHotspotStatus Status { get; set; } = TrafficHotspotStatus.Active;

    /// <summary>
    /// Trạm phụ trách (trạm gần nhất theo vị trí). Operator chỉ giao đội cho điểm nghẽn của trạm mình
    /// </summary>
    public Guid? ResponsibleStationId { get; set; }
    public Station? ResponsibleStation { get; set; }

    /// <summary>
    /// Xe / đội đang được giao xuống phân luồng, giải tỏa điểm nghẽn
    /// </summary>
    public Guid? AssignedRescueUnitId { get; set; }
    public RescueUnit? AssignedRescueUnit { get; set; }

    /// <summary>
    /// Operator đã giao đội và thời điểm giao
    /// </summary>
    public Guid? AssignedByUserId { get; set; }
    public DateTime? AssignedAt { get; set; }

    /// <summary>
    /// Ghi chú khi giải tỏa (kết quả xử lý của đội / Operator)
    /// </summary>
    public string? ResolutionNote { get; set; }

    /// <summary>
    /// Sự cố gốc nếu điểm nghẽn được tạo từ đánh giá kẹt xe trên ảnh một sự cố (tùy chọn)
    /// </summary>
    public Guid? SourceIncidentId { get; set; }

    /// <summary>
    /// Điểm nghẽn còn nằm trên bản đồ: đang chờ xử lý (chưa hết TTL) hoặc đang có đội xử lý
    /// </summary>
    public bool IsOpenAt(DateTime utcNow)
        => Status == TrafficHotspotStatus.Assigned || (Status == TrafficHotspotStatus.Active && ExpiresAt > utcNow);
}
