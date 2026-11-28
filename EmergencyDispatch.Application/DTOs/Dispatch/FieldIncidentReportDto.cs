using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Dispatch;

/// <summary>
/// Nhân viên cứu hộ phát hiện sự cố khi đang ở ngoài (tuần tra, trên đường về trạm...): báo lên trung tâm và tổ của
/// mình nhận xử lý ngay tại chỗ. Sự cố được xác minh luôn, lượt phân công của tổ vào thẳng trạng thái "đang ở hiện trường".
/// </summary>
public class FieldIncidentReportDto
{
    public string? Title { get; set; }
    public string? Description { get; set; }

    /// <summary>
    /// Vị trí sự cố (thường là GPS của nhân viên)
    /// </summary>
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string LocationAddress { get; set; } = string.Empty;

    /// <summary>
    /// Nhóm sự cố: FireExplosion / TrafficAccident / Other. Gửi incidentTypeCode thì nhóm lấy theo danh mục
    /// </summary>
    public IncidentEventType EventType { get; set; } = IncidentEventType.Other;

    /// <summary>
    /// Mã loại sự cố chi tiết từ GET /api/incident-types (tùy chọn)
    /// </summary>
    public string? IncidentTypeCode { get; set; }

    /// <summary>
    /// Level1–Level3, do tổ đánh giá tại hiện trường (Operator vẫn chỉnh được sau)
    /// </summary>
    public SeverityLevel Severity { get; set; }

    /// <summary>
    /// Xe của tổ dùng để xử lý (tùy chọn). Bỏ trống: chọn xe sẵn sàng gần nhất của tổ
    /// </summary>
    public Guid? RescueUnitId { get; set; }

    /// <summary>
    /// Ảnh / video hiện trường đã tải lên qua API media
    /// </summary>
    public List<string> MediaUrls { get; set; } = new();
}

/// <summary>
/// Kết quả tổ báo sự cố tại hiện trường: sự cố mới và lượt phân công của tổ (đang ở hiện trường)
/// </summary>
public class FieldIncidentReportResultDto
{
    public Guid IncidentId { get; set; }
    public string IncidentCode { get; set; } = string.Empty;
    public DispatchAssignmentResponseDto Assignment { get; set; } = new();
}
