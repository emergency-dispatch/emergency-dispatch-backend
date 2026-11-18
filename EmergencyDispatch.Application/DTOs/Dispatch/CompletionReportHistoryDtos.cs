using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Dispatch;

/// <summary>
/// Bộ lọc lịch sử báo cáo nghiệm thu (GET /api/dispatch/completion-reports)
/// </summary>
public class CompletionReportFilterDto
{
    /// <summary>
    /// Thời điểm nộp báo cáo (UTC), từ
    /// </summary>
    public DateTime? From { get; set; }

    /// <summary>
    /// Thời điểm nộp báo cáo (UTC), đến
    /// </summary>
    public DateTime? To { get; set; }

    /// <summary>
    /// Chỉ Admin dùng được; Operator luôn bị giới hạn ở trạm mình, nhân viên cứu hộ ở tổ mình
    /// </summary>
    public Guid? StationId { get; set; }

    public Guid? TeamId { get; set; }
    public Guid? IncidentId { get; set; }

    /// <summary>
    /// Tìm theo nội dung xử lý, tiêu đề hoặc địa chỉ sự cố
    /// </summary>
    public string? Search { get; set; }

    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// Bộ lọc lịch sử nhiệm vụ của đội (GET /api/dispatch/my-assignments/history)
/// </summary>
public class MyAssignmentHistoryFilterDto
{
    /// <summary>
    /// Thời điểm điều phối (UTC), từ
    /// </summary>
    public DateTime? From { get; set; }

    /// <summary>
    /// Thời điểm điều phối (UTC), đến
    /// </summary>
    public DateTime? To { get; set; }

    public int PageIndex { get; set; } = 1;

    /// <summary>
    /// 1–100, mặc định 20
    /// </summary>
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// Một báo cáo nghiệm thu trong lịch sử: nội dung báo cáo + sự cố + tổ + người nộp + các xe được hoàn tất
/// </summary>
public class CompletionReportHistoryItemDto : CompletionReportDto
{
    public Guid IncidentId { get; set; }
    public string IncidentCode { get; set; } = string.Empty;
    public string IncidentTitle { get; set; } = string.Empty;
    public string IncidentAddress { get; set; } = string.Empty;
    public IncidentStatus? IncidentStatus { get; set; }
    public SeverityLevel? IncidentSeverity { get; set; }
    public DateTime? IncidentClosedAt { get; set; }

    public Guid? StationId { get; set; }
    public string? StationName { get; set; }
    public string? SubmittedByName { get; set; }

    public List<CompletionReportUnitDto> Units { get; set; } = new();
}

/// <summary>
/// Một xe / lượt điều phối được hoàn tất bởi báo cáo
/// </summary>
public class CompletionReportUnitDto
{
    public Guid AssignmentId { get; set; }
    public Guid RescueUnitId { get; set; }
    public string PlateNumber { get; set; } = string.Empty;
    public string UnitType { get; set; } = string.Empty;
    public DateTime DispatchedAt { get; set; }
    public DateTime? ArrivedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
