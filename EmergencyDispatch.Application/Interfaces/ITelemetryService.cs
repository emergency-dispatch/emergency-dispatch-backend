using EmergencyDispatch.Application.DTOs.Benchmark;

namespace EmergencyDispatch.Application.Interfaces;

public interface ITelemetryService
{
    Task<TelemetrySummaryDto> GetSummaryAsync(DateTime? from, DateTime? to, CancellationToken cancellationToken = default);
}
