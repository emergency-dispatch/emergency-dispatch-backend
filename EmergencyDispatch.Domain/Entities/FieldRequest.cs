using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Yêu cầu đội cứu hộ gửi về trung tâm điều phối từ hiện trường (cần chi viện thêm xe hoặc vấn đề khác).
/// Gắn với một lượt điều phối đang chạy; mọi thao tác đều ghi vào IncidentAuditTrail của sự cố.
/// </summary>
public class FieldRequest : BaseEntity
{
    public Guid IncidentId { get; set; }
    public Incident? Incident { get; set; }

    /// <summary>
    /// Lượt điều phối của xe gửi yêu cầu
    /// </summary>
    public Guid AssignmentId { get; set; }
    public DispatchAssignment? Assignment { get; set; }

    public Guid RescueUnitId { get; set; }
    public RescueUnit? RescueUnit { get; set; }

    public Guid RequestedByUserId { get; set; }
    public User? RequestedByUser { get; set; }

    public FieldRequestType Type { get; set; }
    public FieldRequestStatus Status { get; set; } = FieldRequestStatus.Open;

    /// <summary>
    /// (Backup) Loại xe cần chi viện; null = để Operator quyết định
    /// </summary>
    public RescueUnitType? RequestedUnitType { get; set; }

    /// <summary>
    /// (Backup) Số xe cần thêm (1–5)
    /// </summary>
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Mô tả tình huống (bắt buộc với Other)
    /// </summary>
    public string? Description { get; set; }

    public Guid? AcknowledgedByUserId { get; set; }
    public DateTime? AcknowledgedAt { get; set; }

    public Guid? ResolvedByUserId { get; set; }
    public DateTime? ResolvedAt { get; set; }

    /// <summary>
    /// Phản hồi của trung tâm (đáp ứng / lý do từ chối) hoặc lý do staff tự hủy
    /// </summary>
    public string? ResponseNote { get; set; }

    /// <summary>
    /// Thời điểm yêu cầu quá ngưỡng chưa được xử lý và đã báo lên Admin
    /// </summary>
    public DateTime? EscalatedAt { get; set; }

    public bool IsOpen => Status is FieldRequestStatus.Open or FieldRequestStatus.Acknowledged;
}
