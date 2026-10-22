using EmergencyDispatch.Application.DTOs.Benchmark;

namespace EmergencyDispatch.Application.Interfaces;

public interface ITelemetryService
{
    Task<TelemetrySummaryDto> GetSummaryAsync(DateTime? from, DateTime? to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dữ liệu từng sự cố (mới nhất trước) để phân tích RQ2 / RQ3
    /// </summary>
    Task<IReadOnlyList<TelemetryIncidentRowDto>> GetIncidentRowsAsync(DateTime? from, DateTime? to, CancellationToken cancellationToken = default);
}
