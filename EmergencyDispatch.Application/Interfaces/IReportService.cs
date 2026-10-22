using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Report;

namespace EmergencyDispatch.Application.Interfaces;

public interface IReportService
{
    Task<PaginatedResultDto<IncidentHistoryItemDto>> GetIncidentHistoryAsync(IncidentHistoryFilterDto filter, CancellationToken cancellationToken = default);

    Task<IncidentReportSummaryDto> GetIncidentSummaryAsync(IncidentHistoryFilterDto filter, CancellationToken cancellationToken = default);

    Task<ReportFileDto> ExportIncidentsAsync(IncidentHistoryFilterDto filter, ReportFormat format, CancellationToken cancellationToken = default);
}

/// <summary>
/// Dựng file báo cáo (CSV / PDF) — cài đặt ở Infrastructure vì cần thư viện PDF
/// </summary>
public interface IReportFileGenerator
{
    byte[] BuildIncidentCsv(IReadOnlyList<IncidentHistoryItemDto> rows, IncidentReportMetaDto meta);
    byte[] BuildIncidentPdf(IReadOnlyList<IncidentHistoryItemDto> rows, IncidentReportMetaDto meta);
    byte[] BuildTelemetryCsv(IReadOnlyList<EmergencyDispatch.Application.DTOs.Benchmark.TelemetryIncidentRowDto> rows);
}
