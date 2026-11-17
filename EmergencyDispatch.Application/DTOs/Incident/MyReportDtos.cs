using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Incident;

/// <summary>
/// Bộ lọc nhanh lịch sử báo cáo của người dân (FR-CIT-05)
/// </summary>
public enum MyReportFilter
{
    All = 0,
    Completed = 1,
    Cancelled = 2
}

/// <summary>
/// Một sự cố trong lịch sử báo cáo của người dân, kèm các mốc thời gian xử lý (FR-CIT-05)
/// </summary>
public class MyReportDto
{
    public Guid Id { get; set; }
    /// <summary>Mã sự cố hiển thị (SC-yyMMdd-NNNN) thay cho Guid</summary>
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string LocationAddress { get; set; } = string.Empty;
    public IncidentStatus Status { get; set; }
    public SeverityLevel Severity { get; set; }
    public string? ResolutionSummary { get; set; }
    public CancellationReason? CancellationReason { get; set; }
    public List<string> MediaUrls { get; set; } = new();

    /// <summary>
    /// Báo cáo đã được gom (Status = Linked) hoặc bị hủy vì trùng (Cancelled + Duplicate): sự cố gốc. null với các ca khác
    /// </summary>
    public Guid? LinkedToIncidentId { get; set; }

    /// <summary>
    /// Tiêu đề / địa chỉ / trạng thái của sự cố gốc (để app hiện "Đã gộp vào sự cố ..." mà không cần gọi thêm)
    /// </summary>
    public string? LinkedToIncidentCode { get; set; }
    public string? LinkedToIncidentTitle { get; set; }
    public string? LinkedToIncidentAddress { get; set; }
    public IncidentStatus? LinkedToIncidentStatus { get; set; }

    // Mốc thời gian (FR-CIT-05)
    public DateTime ReportedAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public DateTime? OnSceneAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public List<MyReportUnitDto> DispatchedUnits { get; set; } = new();
}

public class MyReportUnitDto
{
    public string PlateNumber { get; set; } = string.Empty;
    public string UnitType { get; set; }
    public string? StationName { get; set; }
    public DispatchAssignmentStatus Status { get; set; }
}

/// <summary>
/// Một dòng nhật ký kiểm toán bất biến của sự cố (BR-03, BR-AD-06)
/// </summary>
public class AuditTrailEntryDto
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public string? IncidentCode { get; set; }
    public string? IncidentTitle { get; set; }
    public Guid? ActorId { get; set; }
    public string? ActorName { get; set; }
    public string? ActorRole { get; set; }
    public AuditAction Action { get; set; }
    public string? Description { get; set; }
    public string? OldStateJson { get; set; }
    public string? NewStateJson { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Bộ lọc nhật ký kiểm toán cho Admin
/// </summary>
public class AuditTrailFilterDto
{
    public Guid? IncidentId { get; set; }

    /// <summary>
    /// Lọc theo mã sự cố (SC-yyMMdd-NNNN, không phân biệt hoa thường) thay cho IncidentId
    /// </summary>
    public string? IncidentCode { get; set; }
    public Guid? ActorId { get; set; }
    public AuditAction? Action { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
