using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Incident;

// ============================== B. Đổi mức độ sau khi xác minh ==============================

/// <summary>
/// Operator / Admin đổi mức độ (và tùy chọn loại) của sự cố đã xác minh. Bắt buộc lý do ≥ 5 ký tự
/// </summary>
public class ChangeSeverityDto
{
    /// <summary>Level1 / Level2 / Level3</summary>
    public SeverityLevel NewSeverity { get; set; }

    /// <summary>Đổi loại sự cố (tùy chọn): FireExplosion / TrafficAccident</summary>
    public IncidentEventType? NewEventType { get; set; }

    public string Reason { get; set; } = string.Empty;
}

// ============================== A. Yêu cầu từ hiện trường ==============================

/// <summary>
/// Đội cứu hộ gửi yêu cầu về trung tâm: Backup (cần thêm xe) hoặc Other (vấn đề khác, bắt buộc mô tả)
/// </summary>
public class CreateFieldRequestDto
{
    public FieldRequestType Type { get; set; }

    /// <summary>(Backup) loại xe cần thêm, null = để Operator quyết định</summary>
    public RescueUnitType? RequestedUnitType { get; set; }

    /// <summary>(Backup) số xe cần thêm, 1–5</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>Mô tả tình huống (Other: bắt buộc ≥ 5 ký tự; Backup: tùy chọn)</summary>
    public string? Description { get; set; }
}

/// <summary>
/// Phản hồi yêu cầu: fulfill (ghi chú tùy chọn), decline (lý do ≥ 5 ký tự), cancel (lý do tùy chọn)
/// </summary>
public class RespondFieldRequestDto
{
    public string? Note { get; set; }
}

public class FieldRequestDto
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public string? IncidentTitle { get; set; }
    public string? IncidentAddress { get; set; }
    public SeverityLevel? IncidentSeverity { get; set; }
    public Guid? ResponsibleStationId { get; set; }
    public Guid AssignmentId { get; set; }
    public Guid RescueUnitId { get; set; }
    public string? PlateNumber { get; set; }
    public Guid UnitStationId { get; set; }

    /// <summary>
    /// Tổ phụ trách xe gửi yêu cầu (null = xe chưa gán tổ)
    /// </summary>
    public Guid? UnitTeamId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public string? RequestedByName { get; set; }
    public FieldRequestType Type { get; set; }
    public FieldRequestStatus Status { get; set; }
    public RescueUnitType? RequestedUnitType { get; set; }
    public int Quantity { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? AcknowledgedByUserId { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResponseNote { get; set; }
    public DateTime? EscalatedAt { get; set; }
    public double AgeMinutes => Math.Round((DateTime.UtcNow - CreatedAt).TotalMinutes, 1);
}

/// <summary>
/// Sự kiện realtime ReceiveFieldRequest: Created / Acknowledged / Fulfilled / Declined / Cancelled / Escalated
/// </summary>
public class FieldRequestEventDto
{
    public string Action { get; set; } = string.Empty;
    public FieldRequestDto Request { get; set; } = new();
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}

// ============================== C. Gom / tách báo cáo trùng ==============================

/// <summary>
/// Thông tin sự cố gốc trả cho người báo khi SOS của họ được tự gom (để app hiện "Không phải vụ này? → Đây là sự cố khác")
/// </summary>
public class LinkedToIncidentDto
{
    public Guid IncidentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string LocationAddress { get; set; } = string.Empty;
    public IncidentEventType EventType { get; set; }
    public IncidentStatus Status { get; set; }
    public double DistanceMeters { get; set; }
    public List<string> AssignedUnitPlates { get; set; } = new();
}

/// <summary>
/// Tách báo cáo khỏi sự cố gốc. Người báo là khách: gửi mã guestCancellationToken nhận được khi gửi SOS
/// </summary>
public class UnlinkIncidentDto
{
    public string? Reason { get; set; }
    public string? GuestCancellationToken { get; set; }
}

/// <summary>
/// Operator xác nhận báo cáo đã gom đúng là trùng (đóng báo cáo, lý do Duplicate)
/// </summary>
public class ConfirmDuplicateDto
{
    public string? Note { get; set; }
}

/// <summary>
/// Một báo cáo đã gom vào sự cố gốc (Operator xem để kiểm tra, tách hoặc xác nhận trùng)
/// </summary>
public class LinkedReportDto
{
    public Guid IncidentId { get; set; }
    public IncidentStatus Status { get; set; }
    public DateTime ReportedAt { get; set; }
    public DateTime? LinkedAt { get; set; }
    public double? DistanceMeters { get; set; }
    public string Description { get; set; } = string.Empty;
    public string LocationAddress { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? ReporterName { get; set; }
    public string? MaskedReporterPhone { get; set; }
    public IncidentEventType? ReportedEventType { get; set; }

    /// <summary>Kết quả YOLO trên ảnh (null = chưa kiểm tra / không có ảnh / YOLO tắt)</summary>
    public IncidentEventType? QuickCheckEventType { get; set; }

    /// <summary>Cảnh báo lệch: xa hơn 70% bán kính gom, hoặc loại tự chọn / YOLO khác loại sự cố gốc</summary>
    public bool IsOutlier { get; set; }

    public List<string> MediaUrls { get; set; } = new();
}

// ============================== D. Người báo bổ sung thông tin ==============================

/// <summary>
/// Người báo bổ sung ghi chú / ảnh vào sự cố của mình. Khách: gửi kèm guestCancellationToken
/// </summary>
public class AddSupplementDto
{
    public string Note { get; set; } = string.Empty;
    public List<string> MediaUrls { get; set; } = new();
    public string? GuestCancellationToken { get; set; }
}

public class IncidentSupplementDto
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }

    /// <summary>Sự cố gốc nhận thông tin (khác IncidentId khi người báo thuộc một báo cáo đã gom)</summary>
    public Guid TargetIncidentId { get; set; }

    public string Note { get; set; } = string.Empty;
    public List<string> MediaUrls { get; set; } = new();
    public string? ReporterName { get; set; }
    public DateTime CreatedAt { get; set; }
}
