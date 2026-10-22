using EmergencyDispatch.Application.DTOs.Audit;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Report;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;

namespace EmergencyDispatch.Application.Services;

/// <summary>
/// Lịch sử sự cố đã đóng và xuất báo cáo CSV / PDF cho Admin
/// </summary>
public class ReportService : IReportService
{
    public const int MaxPageSize = 100;
    public const int MaxExportRows = 5000;

    private readonly IIncidentRepository _incidentRepository;
    private readonly IStationRepository _stationRepository;
    private readonly IReportFileGenerator _fileGenerator;
    private readonly ISystemAuditService _auditService;
    private readonly ICurrentUserContext _currentUser;

    public ReportService(
        IIncidentRepository incidentRepository,
        IStationRepository stationRepository,
        IReportFileGenerator fileGenerator,
        ISystemAuditService auditService,
        ICurrentUserContext currentUser)
    {
        _incidentRepository = incidentRepository;
        _stationRepository = stationRepository;
        _fileGenerator = fileGenerator;
        _auditService = auditService;
        _currentUser = currentUser;
    }

    public async Task<PaginatedResultDto<IncidentHistoryItemDto>> GetIncidentHistoryAsync(IncidentHistoryFilterDto filter, CancellationToken cancellationToken = default)
    {
        Validate(filter);
        var pageIndex = Math.Max(1, filter.PageIndex);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);

        var (items, total) = await QueryAsync(filter, pageIndex, pageSize, cancellationToken);
        return new PaginatedResultDto<IncidentHistoryItemDto>(items.Select(Map).ToList(), total, pageIndex, pageSize);
    }

    public async Task<IncidentReportSummaryDto> GetIncidentSummaryAsync(IncidentHistoryFilterDto filter, CancellationToken cancellationToken = default)
    {
        Validate(filter);
        var (items, _) = await QueryAsync(filter, 1, 0, cancellationToken);
        return BuildSummary(items.Select(Map).ToList());
    }

    public async Task<ReportFileDto> ExportIncidentsAsync(IncidentHistoryFilterDto filter, ReportFormat format, CancellationToken cancellationToken = default)
    {
        Validate(filter);
        var (items, total) = await QueryAsync(filter, 1, MaxExportRows, cancellationToken);
        var rows = items.Select(Map).ToList();

        var meta = new IncidentReportMetaDto
        {
            FilterDescription = await DescribeFilterAsync(filter),
            GeneratedBy = _currentUser.FullName ?? "System",
            GeneratedAtUtc = DateTime.UtcNow,
            TotalMatched = total,
            Truncated = total > rows.Count,
            Summary = BuildSummary(rows)
        };

        var stamp = DateTime.UtcNow.AddHours(7).ToString("yyyyMMdd_HHmm");
        var file = format == ReportFormat.Pdf
            ? new ReportFileDto
            {
                Content = _fileGenerator.BuildIncidentPdf(rows, meta),
                ContentType = "application/pdf",
                FileName = $"bao-cao-su-co_{stamp}.pdf"
            }
            : new ReportFileDto
            {
                Content = _fileGenerator.BuildIncidentCsv(rows, meta),
                ContentType = "text/csv; charset=utf-8",
                FileName = $"bao-cao-su-co_{stamp}.csv"
            };

        await _auditService.LogAsync(SystemAuditAction.Exported, AuditEntityTypes.Report, null, file.FileName,
            $"Xuất báo cáo sự cố ({format}, {rows.Count} dòng). {meta.FilterDescription}", cancellationToken: cancellationToken);

        return file;
    }

    private Task<(IReadOnlyList<Incident> Items, int TotalCount)> QueryAsync(IncidentHistoryFilterDto filter, int pageIndex, int pageSize, CancellationToken cancellationToken)
    {
        var statuses = filter.Status switch
        {
            ClosedIncidentFilter.Completed => new[] { IncidentStatus.Completed },
            ClosedIncidentFilter.Cancelled => new[] { IncidentStatus.Cancelled },
            _ => new[] { IncidentStatus.Completed, IncidentStatus.Cancelled }
        };

        return _incidentRepository.GetClosedHistoryAsync(
            statuses, filter.From, filter.To, filter.StationId, filter.Severity, filter.Search,
            pageIndex, pageSize, cancellationToken);
    }

    private static void Validate(IncidentHistoryFilterDto filter)
    {
        if (filter.From.HasValue && filter.To.HasValue && filter.From > filter.To)
        {
            throw new ArgumentException("Thời gian bắt đầu phải trước thời gian kết thúc.");
        }
    }

    private async Task<string> DescribeFilterAsync(IncidentHistoryFilterDto filter)
    {
        var parts = new List<string>();
        static string Local(DateTime utc) => utc.AddHours(7).ToString("dd/MM/yyyy HH:mm");

        parts.Add(filter.From.HasValue || filter.To.HasValue
            ? $"Thời gian báo cáo: {(filter.From.HasValue ? Local(filter.From.Value) : "…")} – {(filter.To.HasValue ? Local(filter.To.Value) : "…")}"
            : "Thời gian: toàn bộ");

        if (filter.StationId.HasValue)
        {
            var station = await _stationRepository.GetByIdAsync(filter.StationId.Value);
            parts.Add($"Trạm: {station?.Name ?? filter.StationId.ToString()}");
        }
        parts.Add(filter.Status switch
        {
            ClosedIncidentFilter.Completed => "Trạng thái: Hoàn thành",
            ClosedIncidentFilter.Cancelled => "Trạng thái: Đã hủy",
            _ => "Trạng thái: Hoàn thành + Đã hủy"
        });
        if (filter.Severity.HasValue) parts.Add($"Mức độ: {filter.Severity}");
        if (!string.IsNullOrWhiteSpace(filter.Search)) parts.Add($"Từ khóa: \"{filter.Search.Trim()}\"");

        return string.Join(" · ", parts);
    }

    public static IncidentHistoryItemDto Map(Incident i)
    {
        var assignments = i.DispatchAssignments
            .Where(a => a.Status != DispatchAssignmentStatus.Rejected)
            .ToList();
        var firstDispatched = assignments.Select(a => (DateTime?)a.DispatchedAt).Min();
        var firstArrived = assignments.Where(a => a.ArrivedAt.HasValue).Select(a => a.ArrivedAt).Min();
        var closedAt = i.ClosedAt ?? i.CancelledAt ?? (i.Status is IncidentStatus.Completed or IncidentStatus.Cancelled ? i.UpdatedAt : null);

        return new IncidentHistoryItemDto
        {
            Id = i.Id,
            Title = i.Title,
            LocationAddress = i.LocationAddress,
            Severity = i.Severity,
            AiSeverityScore = i.AiClassification?.IsSuccess == true ? (int)i.AiClassification.SeverityScore : null,
            Status = i.Status,
            CancellationReason = i.CancellationReason,
            ResolutionSummary = i.ResolutionSummary,
            ReportedAt = i.CreatedAt,
            VerifiedAt = i.VerifiedAt,
            FirstDispatchedAt = firstDispatched,
            FirstArrivedAt = firstArrived,
            ClosedAt = closedAt,
            ResponseMinutes = firstArrived.HasValue ? Math.Round((firstArrived.Value - i.CreatedAt).TotalMinutes, 1) : null,
            ResolutionMinutes = closedAt.HasValue ? Math.Round((closedAt.Value - i.CreatedAt).TotalMinutes, 1) : null,
            VerifiedByName = i.VerifiedByUser?.FullName,
            ClosedByName = i.ClosedByUser?.FullName,
            Stations = assignments.Select(a => a.RescueUnit?.Station?.Name).Where(n => n != null).Distinct().Select(n => n!).ToList(),
            Units = assignments.Select(a => a.RescueUnit?.PlateNumber).Where(p => p != null).Distinct().Select(p => p!).ToList()
        };
    }

    public static IncidentReportSummaryDto BuildSummary(IReadOnlyList<IncidentHistoryItemDto> rows)
    {
        var response = rows.Where(r => r.ResponseMinutes.HasValue).Select(r => r.ResponseMinutes!.Value).ToList();
        var resolution = rows.Where(r => r.Status == IncidentStatus.Completed && r.ResolutionMinutes.HasValue)
            .Select(r => r.ResolutionMinutes!.Value).ToList();

        return new IncidentReportSummaryDto
        {
            Total = rows.Count,
            Completed = rows.Count(r => r.Status == IncidentStatus.Completed),
            Cancelled = rows.Count(r => r.Status == IncidentStatus.Cancelled),
            BySeverity = Enum.GetValues<SeverityLevel>().ToDictionary(s => s.ToString(), s => rows.Count(r => r.Severity == s)),
            AvgResponseMinutes = response.Count > 0 ? Math.Round(response.Average(), 1) : null,
            AvgResolutionMinutes = resolution.Count > 0 ? Math.Round(resolution.Average(), 1) : null
        };
    }
}
