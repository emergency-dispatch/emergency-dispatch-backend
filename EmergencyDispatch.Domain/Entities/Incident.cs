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
    /// ID người dân gửi báo cáo (nếu đã đăng nhập)
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
    /// Thời điểm đóng hồ sơ sự cố
    /// </summary>
    public DateTime? ClosedAt { get; set; }

    /// <summary>
    /// ID Người đóng hồ sơ sự cố (Operator hoặc Staff)
    /// </summary>
    public Guid? ClosedByUserId { get; set; }
    public User? ClosedByUser { get; set; }

    /// <summary>
    /// Báo cáo tóm tắt giải quyết sự cố / kết quả xử lý hiện trường
    /// </summary>
    public string? ResolutionSummary { get; set; }

    /// <summary>
    /// Danh sách ảnh/video hiện trường đính kèm
    /// </summary>
    public ICollection<IncidentMedia> MediaItems { get; set; } = new List<IncidentMedia>();

    /// <summary>
    /// Kết quả phân tích từ AI Vision-Language
    /// </summary>
    public AiClassification? AiClassification { get; set; }

    /// <summary>
    /// Danh sách các lượt điều động phương tiện / đội xe cứu hộ
    /// </summary>
    public ICollection<DispatchAssignment> DispatchAssignments { get; set; } = new List<DispatchAssignment>();

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
