using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Domain.Entities;

/// <summary>
/// Thực thể Sự cố khẩn cấp (Incident)
/// </summary>
public class Incident : BaseEntity
{
    /// <summary>
    /// Tiêu đề hoặc phân loại ngắn gọn của sự cố
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả chi tiết tình huống từ người báo cáo
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Vĩ độ GPS hiện trường
    /// </summary>
    public double Latitude { get; set; }

    /// <summary>
    /// Kinh độ GPS hiện trường
    /// </summary>
    public double Longitude { get; set; }

    /// <summary>
    /// Địa chỉ bằng chữ hiển thị cho người điều phối và cứu hộ
    /// </summary>
    public string LocationAddress { get; set; } = string.Empty;

    /// <summary>
    /// Trạng thái sự cố
    /// </summary>
    public IncidentStatus Status { get; set; } = IncidentStatus.Pending;

    /// <summary>
    /// Mức độ nghiêm trọng hiện hành (AI đề xuất trước, Operator có quyền ghi đè)
    /// </summary>
    public SeverityLevel Severity { get; set; } = SeverityLevel.Unclassified;

    /// <summary>
    /// Kênh tiếp nhận: SOS qua app, hoặc Operator tự tạo khi biết tin từ điện thoại / bộ đàm / camera...
    /// </summary>
    public IncidentSource Source { get; set; } = IncidentSource.CitizenApp;

    /// <summary>
    /// ID người dân gửi báo cáo (nếu đã đăng nhập, null nếu nặc danh)
    /// </summary>
    public Guid? ReportedByUserId { get; set; }
    public User? ReportedByUser { get; set; }

    /// <summary>
    /// Tên người báo cáo (lưu độc lập đề phòng báo cáo nặc danh hoặc khách)
    /// </summary>
    public string? ReporterName { get; set; }

    /// <summary>
    /// Số điện thoại liên hệ khẩn cấp của người báo cáo
    /// </summary>
    public string? ReporterPhone { get; set; }

    /// <summary>
    /// ID điều phối viên (Operator) đã xác minh sự cố
    /// </summary>
    public Guid? VerifiedByUserId { get; set; }
    public User? VerifiedByUser { get; set; }

    /// <summary>
    /// Thời điểm Operator xác minh sự cố
    /// </summary>
    public DateTime? VerifiedAt { get; set; }

    /// <summary>
    /// Ghi chú nghiệp vụ của Operator khi xác minh hoặc điều chỉnh AI
    /// </summary>
    public string? OperatorNotes { get; set; }

    /// <summary>
    /// Lý do hủy sự cố (nếu Status = Cancelled)
    /// </summary>
    public CancellationReason? CancellationReason { get; set; }

    /// <summary>
    /// Khóa ngoại tự tham chiếu tới sự cố gốc nếu phát hiện trùng lặp
    /// </summary>
    public Guid? DuplicateOfIncidentId { get; set; }
    public Incident? DuplicateOfIncident { get; set; }
    public ICollection<Incident> DuplicateIncidents { get; set; } = new List<Incident>();

    /// <summary>
    /// Cờ đánh dấu báo cáo bị trễ (offline sync hoặc người dân gửi muộn)
    /// </summary>
    public bool IsDelayed { get; set; } = false;
    public DateTime? OriginalReportTimestamp { get; set; }

    /// <summary>
    /// ID điều phối viên / quản trị viên đóng sự cố
    /// </summary>
    public Guid? ClosedByUserId { get; set; }
    public User? ClosedByUser { get; set; }

    /// <summary>
    /// Thời điểm đóng sự cố hoàn tất
    /// </summary>
    public DateTime? ClosedAt { get; set; }

    /// <summary>
    /// Báo cáo tóm tắt giải quyết sự cố / kết quả xử lý hiện trường
    /// </summary>
    public string? ResolutionSummary { get; set; }

    /// <summary>
    /// Khóa mềm 2 phút khi một Operator đang xử lý sự cố (FR-OP-02, BR-15)
    /// </summary>
    public Guid? LockedByOperatorId { get; set; }
    public User? LockedByOperator { get; set; }
    public DateTime? LockExpiresAt { get; set; }

    /// <summary>
    /// Trạm phụ trách theo vị trí sự cố: chỉ Operator của trạm này (và Admin) xử lý.
    /// Null = dữ liệu cũ / chưa xác định → mọi Operator đều xử lý được.
    /// </summary>
    public Guid? ResponsibleStationId { get; set; }
    public Station? ResponsibleStation { get; set; }

    /// <summary>
    /// Vị trí báo cáo nằm ngoài khu vực phục vụ đã cấu hình (vẫn tiếp nhận, nhưng cảnh báo Operator)
    /// </summary>
    public bool IsOutOfServiceArea { get; set; }
    public double? DistanceOutsideAreaKm { get; set; }

    /// <summary>
    /// Loại sự cố đã xác nhận (Cháy nổ / Tai nạn). Mặc định lấy từ AI, Operator xác nhận hoặc sửa khi verify.
    /// Dùng để chọn chủng loại xe khi gợi ý điều phối (BR-13).
    /// </summary>
    public IncidentEventType EventType { get; set; } = IncidentEventType.None;

    /// <summary>
    /// Thông tin hủy sự cố — tách riêng để không ghi đè VerifiedAt / OperatorNotes (số liệu RQ3)
    /// </summary>
    public Guid? CancelledByUserId { get; set; }
    public User? CancelledByUser { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationNote { get; set; }

    /// <summary>
    /// Thời gian phản hồi tính bằng mili-giây (từ khi tạo đến khi Operator xác minh)
    /// </summary>
    public long? ResponseTimeMs { get; set; }

    /// <summary>
    /// Thời gian điều phối (từ khi xác minh đến khi có đội cứu hộ nhận việc)
    /// </summary>
    public long? DispatchTimeMs { get; set; }

    /// <summary>
    /// Tổng thời gian giải quyết sự cố (từ khi tạo đến khi đóng)
    /// </summary>
    public long? ResolutionTimeMs { get; set; }

    /// <summary>
    /// Phiên bản dòng dữ liệu (cột hệ thống xmin của PostgreSQL) — chống ghi đè khi nhiều người / worker cùng sửa sự cố
    /// </summary>
    public uint Version { get; set; }

    // Navigation properties
    public ICollection<IncidentMedia> MediaItems { get; set; } = new List<IncidentMedia>();
    public AiClassification? AiClassification { get; set; }
    public ICollection<DispatchAssignment> DispatchAssignments { get; set; } = new List<DispatchAssignment>();
    public ICollection<IncidentStatusHistory> StatusHistories { get; set; } = new List<IncidentStatusHistory>();
    public ICollection<IncidentAuditTrail> AuditTrails { get; set; } = new List<IncidentAuditTrail>();
    public CitizenFeedback? CitizenFeedback { get; set; }

    /// <summary>
    /// Sự cố đang bị một Operator khác giữ khóa mềm còn hiệu lực (BR-15)
    /// </summary>
    public bool IsLockedByOther(Guid operatorId, DateTime utcNow)
        => LockedByOperatorId.HasValue
           && LockedByOperatorId.Value != operatorId
           && LockExpiresAt.HasValue
           && LockExpiresAt.Value > utcNow;

    /// <summary>
    /// Cập nhật trạng thái sự cố
    /// </summary>
    public void UpdateStatus(IncidentStatus newStatus)
    {
        Status = newStatus;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Cập nhật mức độ nghiêm trọng
    /// </summary>
    public void UpdateSeverity(SeverityLevel newSeverity)
    {
        Severity = newSeverity;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Điều phối viên xác minh sự cố
    /// </summary>
    public void Verify(Guid operatorId, SeverityLevel confirmedSeverity, string? notes = null)
    {
        VerifiedByUserId = operatorId;
        VerifiedAt = DateTime.UtcNow;
        Severity = confirmedSeverity;
        Status = IncidentStatus.Verified;
        OperatorNotes = notes;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Đóng hồ sơ sự cố hoàn tất
    /// </summary>
    public void Close(Guid closedById, string resolutionSummary)
    {
        ClosedByUserId = closedById;
        ClosedAt = DateTime.UtcNow;
        ResolutionSummary = resolutionSummary;
        Status = IncidentStatus.Completed;
        UpdatedAt = DateTime.UtcNow;
    }
}
