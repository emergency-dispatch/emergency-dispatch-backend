using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Traffic;

/// <summary>
/// DTO thông tin điểm nghẽn giao thông phục vụ hiển thị layer bản đồ và giao đội xử lý
/// </summary>
public class TrafficHotspotResponseDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string LocationAddress { get; set; } = string.Empty;
    public TrafficCongestionLevel CongestionLevel { get; set; }
    public string CongestionLevelName => CongestionLevel.ToString();
    public string? MediaUrl { get; set; }
    public Guid? ReportedByUserId { get; set; }
    public string? ReporterName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// true khi điểm nghẽn còn trên bản đồ (Active hoặc Assigned)
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Vòng đời: Active / Assigned / Resolved / Expired
    /// </summary>
    public TrafficHotspotStatus Status { get; set; }

    public Guid? ResponsibleStationId { get; set; }
    public string? ResponsibleStationName { get; set; }

    /// <summary>
    /// Xe / đội đang được giao xử lý (null nếu chưa giao)
    /// </summary>
    public Guid? AssignedRescueUnitId { get; set; }
    public string? AssignedUnitPlate { get; set; }
    public Guid? AssignedUnitStationId { get; set; }
    public DateTime? AssignedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }
    public string? ResolutionNote { get; set; }
    public Guid? SourceIncidentId { get; set; }

    /// <summary>
    /// Số phút còn lại trước khi tự động hết hạn hiển thị (điểm nghẽn đang có đội xử lý không tự hết hạn)
    /// </summary>
    public double RemainingMinutes
    {
        get
        {
            if (Status == TrafficHotspotStatus.Assigned) return 0;
            var remaining = (ExpiresAt - DateTime.UtcNow).TotalMinutes;
            return remaining > 0 ? Math.Round(remaining, 1) : 0;
        }
    }
}

/// <summary>
/// Operator giao một xe / đội xuống phân luồng, giải tỏa điểm nghẽn
/// </summary>
public class AssignTrafficHotspotDto
{
    public Guid RescueUnitId { get; set; }

    /// <summary>
    /// Ghi chú cho đội (tùy chọn), VD: "Phân luồng xe container cổng D1"
    /// </summary>
    public string? Notes { get; set; }
}

/// <summary>
/// Operator rút đội khỏi điểm nghẽn (VD: cần xe cho sự cố khẩn cấp). Bắt buộc lý do ≥ 5 ký tự
/// </summary>
public class UnassignTrafficHotspotDto
{
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Giải tỏa điểm nghẽn (body tùy chọn)
/// </summary>
public class ResolveTrafficHotspotDto
{
    public string? Note { get; set; }
}

/// <summary>
/// Sự kiện realtime ReceiveTrafficTaskAssigned gửi tới nhân viên trạm của xe và trung tâm điều phối
/// </summary>
public class TrafficTaskEventDto
{
    /// <summary>
    /// Assigned / Unassigned / Resolved
    /// </summary>
    public string Action { get; set; } = string.Empty;
    public Guid RescueUnitId { get; set; }
    public string? PlateNumber { get; set; }
    public Guid UnitStationId { get; set; }
    public string? Notes { get; set; }
    public TrafficHotspotResponseDto Hotspot { get; set; } = new();
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
