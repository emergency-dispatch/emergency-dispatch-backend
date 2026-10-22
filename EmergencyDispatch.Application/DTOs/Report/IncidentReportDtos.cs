using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Report;

/// <summary>
/// Nhóm trạng thái của sự cố đã đóng
/// </summary>
public enum ClosedIncidentFilter
{
    All = 0,
    Completed = 1,
    Cancelled = 2
}

public enum ReportFormat
{
    Csv = 0,
    Pdf = 1
}

/// <summary>
/// Bộ lọc lịch sử sự cố đã đóng (Admin / Operator)
/// </summary>
public class IncidentHistoryFilterDto
{
    /// <summary>
    /// Lọc theo thời điểm báo cáo (UTC), từ
    /// </summary>
    public DateTime? From { get; set; }

    /// <summary>
    /// Lọc theo thời điểm báo cáo (UTC), đến
    /// </summary>
    public DateTime? To { get; set; }

    /// <summary>
    /// Trạm có xe tham gia xử lý
    /// </summary>
    public Guid? StationId { get; set; }

    public ClosedIncidentFilter Status { get; set; } = ClosedIncidentFilter.All;
    public SeverityLevel? Severity { get; set; }

    /// <summary>
    /// Tìm theo tiêu đề hoặc địa chỉ
    /// </summary>
    public string? Search { get; set; }

    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// Một sự cố trong lịch sử / báo cáo
/// </summary>
public class IncidentHistoryItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string LocationAddress { get; set; } = string.Empty;
    public SeverityLevel Severity { get; set; }
    public int? AiSeverityScore { get; set; }
    public IncidentStatus Status { get; set; }
    public CancellationReason? CancellationReason { get; set; }
    public string? ResolutionSummary { get; set; }

    public DateTime ReportedAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime? FirstDispatchedAt { get; set; }
    public DateTime? FirstArrivedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    /// <summary>
    /// Từ lúc báo cáo đến khi xe đầu tiên tới hiện trường (phút)
    /// </summary>
    public double? ResponseMinutes { get; set; }

    /// <summary>
    /// Từ lúc báo cáo đến khi đóng sự cố (phút)
    /// </summary>
    public double? ResolutionMinutes { get; set; }

    public string? VerifiedByName { get; set; }
    public string? ClosedByName { get; set; }
    public List<string> Stations { get; set; } = new();
    public List<string> Units { get; set; } = new();
}

/// <summary>
/// Số liệu tổng hợp in ở đầu báo cáo
/// </summary>
public class IncidentReportSummaryDto
{
    public int Total { get; set; }
    public int Completed { get; set; }
    public int Cancelled { get; set; }
    public Dictionary<string, int> BySeverity { get; set; } = new();
    public double? AvgResponseMinutes { get; set; }
    public double? AvgResolutionMinutes { get; set; }
}

/// <summary>
/// Thông tin tiêu đề báo cáo xuất file
/// </summary>
public class IncidentReportMetaDto
{
    public string Title { get; set; } = "Báo cáo sự cố đã đóng";
    public string FilterDescription { get; set; } = string.Empty;
    public string GeneratedBy { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public bool Truncated { get; set; }
    public int TotalMatched { get; set; }
    public IncidentReportSummaryDto Summary { get; set; } = new();
}

public class ReportFileDto
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = "application/octet-stream";
    public string FileName { get; set; } = "report";
}
