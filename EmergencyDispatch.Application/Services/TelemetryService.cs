using EmergencyDispatch.Application.DTOs.Benchmark;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;

namespace EmergencyDispatch.Application.Services;

/// <summary>
/// Tổng hợp số liệu đo đạc thực nghiệm từ dữ liệu vận hành (RQ2, RQ3)
/// </summary>
public class TelemetryService : ITelemetryService
{
    private readonly IIncidentRepository _incidentRepository;

    public TelemetryService(IIncidentRepository incidentRepository)
    {
        _incidentRepository = incidentRepository;
    }

    public async Task<TelemetrySummaryDto> GetSummaryAsync(DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
    {
        var incidents = await _incidentRepository.GetForTelemetryAsync(from, to, cancellationToken);
        return BuildSummary(incidents, from, to);
    }

    public async Task<IReadOnlyList<TelemetryIncidentRowDto>> GetIncidentRowsAsync(DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
    {
        var incidents = await _incidentRepository.GetForTelemetryAsync(from, to, cancellationToken);
        return incidents.OrderByDescending(i => i.CreatedAt).Select(ToRow).ToList();
    }

    public static TelemetryIncidentRowDto ToRow(Incident i)
    {
        static long? Ms(DateTime? a, DateTime? b) => a.HasValue && b.HasValue ? (long)(b.Value - a.Value).TotalMilliseconds : null;

        var ai = i.AiClassification;
        var assignments = i.DispatchAssignments.Where(a => a.Status != DispatchAssignmentStatus.Rejected).OrderBy(a => a.DispatchedAt).ToList();
        var firstDispatch = assignments.Count > 0 ? assignments[0].DispatchedAt : (DateTime?)null;
        var firstAccepted = assignments.Where(a => a.AcceptedAt.HasValue).Select(a => a.AcceptedAt).Min();
        var firstArrived = assignments.Where(a => a.ArrivedAt.HasValue).Select(a => a.ArrivedAt).Min();
        var closedAt = i.ClosedAt ?? i.CancelledAt;
        var aiSucceeded = ai?.IsSuccess == true;

        // T_Operator: từ lúc Operator có kết quả AI (hoặc lúc tạo nếu không có AI) tới khi xác minh
        var operatorStart = aiSucceeded && i.VerifiedAt.HasValue && ai!.CreatedAt <= i.VerifiedAt ? ai.CreatedAt : i.CreatedAt;

        return new TelemetryIncidentRowDto
        {
            IncidentId = i.Id,
            CreatedAt = i.CreatedAt,
            Status = i.Status,
            ResponsibleStation = i.ResponsibleStation?.Name,
            IsOutOfServiceArea = i.IsOutOfServiceArea,
            HasAi = ai != null,
            AiSuccess = ai?.IsSuccess,
            AiProcessedAt = ai?.CreatedAt,
            AiModelLatencyMs = ai?.ProcessingDurationMs,
            AiModel = ai?.ModelName,
            AiSeverity = ai?.SeverityScore,
            AiEventType = ai?.EventType,
            VerifiedAt = i.VerifiedAt,
            VerifiedByUserId = i.VerifiedByUserId,
            ConfirmedSeverity = i.Severity,
            ConfirmedEventType = i.EventType,
            WasAiOverridden = aiSucceeded && i.VerifiedAt.HasValue
                              && (ai!.SeverityScore != i.Severity || (i.EventType != IncidentEventType.None && ai.EventType != i.EventType)),
            FirstDispatchAt = firstDispatch,
            Algorithm = assignments.Select(a => a.DispatchAlgorithm).FirstOrDefault(a => !string.IsNullOrEmpty(a)),
            UnitsDispatched = assignments.Count,
            FirstAcceptedAt = firstAccepted,
            FirstArrivedAt = firstArrived,
            ClosedAt = closedAt,
            CancellationReason = i.CancellationReason,
            TAiMs = Ms(i.CreatedAt, ai?.CreatedAt),
            TOperatorMs = i.VerifiedAt.HasValue ? Ms(operatorStart, i.VerifiedAt) : null,
            TDispatchMs = Ms(i.VerifiedAt, firstDispatch),
            TResponseMs = Ms(i.CreatedAt, firstArrived),
            TResolutionMs = i.ClosedAt.HasValue ? Ms(i.CreatedAt, i.ClosedAt) : null
        };
    }

    public static TelemetrySummaryDto BuildSummary(IReadOnlyList<Incident> incidents, DateTime? from, DateTime? to)
    {
        var tAi = new List<double>();
        var modelLatency = new List<double>();
        var tOperator = new List<double>();
        var tOperatorAi = new List<double>();
        var tOperatorManual = new List<double>();
        var tDispatch = new List<double>();
        var tResponse = new List<double>();
        var tResponseByAlgo = new Dictionary<string, List<double>>();
        var tResolution = new List<double>();
        var agreement = new AiAgreementDto();
        var overrides = 0;

        foreach (var incident in incidents)
        {
            var ai = incident.AiClassification;
            var aiSucceeded = ai is { IsSuccess: true };

            if (ai != null)
            {
                tAi.Add((ai.CreatedAt - incident.CreatedAt).TotalMilliseconds);
                if (ai.ProcessingDurationMs.HasValue) modelLatency.Add(ai.ProcessingDurationMs.Value);
            }

            if (incident.VerifiedAt.HasValue)
            {
                var start = ai != null && ai.CreatedAt <= incident.VerifiedAt.Value ? ai.CreatedAt : incident.CreatedAt;
                var ms = (incident.VerifiedAt.Value - start).TotalMilliseconds;
                tOperator.Add(ms);
                (aiSucceeded ? tOperatorAi : tOperatorManual).Add(ms);

                if (aiSucceeded)
                {
                    var aiIdx = Math.Clamp((int)ai!.SeverityScore, 0, 3);
                    var opIdx = Math.Clamp((int)incident.Severity, 0, 3);
                    agreement.ConfusionMatrix[aiIdx][opIdx]++;
                    agreement.SampleCount++;
                    if (aiIdx != opIdx) overrides++;
                }
            }

            var assignments = incident.DispatchAssignments
                .Where(a => a.Status != DispatchAssignmentStatus.Cancelled && a.Status != DispatchAssignmentStatus.Rejected)
                .ToList();
            if (incident.VerifiedAt.HasValue && assignments.Count > 0)
            {
                tDispatch.Add((assignments.Min(a => a.DispatchedAt) - incident.VerifiedAt.Value).TotalMilliseconds);
            }

            foreach (var a in assignments.Where(a => a.ArrivedAt.HasValue))
            {
                var ms = (a.ArrivedAt!.Value - a.DispatchedAt).TotalMilliseconds;
                tResponse.Add(ms);
                var algo = string.IsNullOrWhiteSpace(a.DispatchAlgorithm) ? "Unknown" : a.DispatchAlgorithm;
                if (!tResponseByAlgo.TryGetValue(algo, out var list)) tResponseByAlgo[algo] = list = new List<double>();
                list.Add(ms);
            }

            if (incident.ClosedAt.HasValue)
            {
                tResolution.Add((incident.ClosedAt.Value - incident.CreatedAt).TotalMilliseconds);
            }
        }

        if (agreement.SampleCount > 0)
        {
            agreement.Accuracy = Math.Round(Enumerable.Range(0, 4).Sum(i => agreement.ConfusionMatrix[i][i]) / (double)agreement.SampleCount, 4);
            agreement.CohensKappa = CohensKappa(agreement.ConfusionMatrix);
            agreement.OverrideRate = Math.Round(overrides / (double)agreement.SampleCount, 4);
        }

        return new TelemetrySummaryDto
        {
            From = from,
            To = to,
            IncidentCount = incidents.Count,
            TAi = Stats(tAi),
            AiModelLatency = Stats(modelLatency),
            TOperator = Stats(tOperator),
            TOperatorWithAi = Stats(tOperatorAi),
            TOperatorManual = Stats(tOperatorManual),
            TDispatch = Stats(tDispatch),
            TResponse = Stats(tResponse),
            TResponseByAlgorithm = tResponseByAlgo.ToDictionary(kv => kv.Key, kv => Stats(kv.Value)),
            TResolution = Stats(tResolution),
            AiAgreement = agreement
        };
    }

    /// <summary>
    /// κ = (p_o − p_e) / (1 − p_e) trên ma trận nhầm lẫn vuông
    /// </summary>
    public static double? CohensKappa(int[][] matrix)
    {
        var n = matrix.Length;
        double total = matrix.Sum(r => r.Sum());
        if (total == 0) return null;

        var po = Enumerable.Range(0, n).Sum(i => matrix[i][i]) / total;
        var pe = Enumerable.Range(0, n).Sum(i =>
        {
            var rowSum = matrix[i].Sum();
            var colSum = matrix.Sum(r => r[i]);
            return rowSum / total * (colSum / total);
        });

        if (Math.Abs(1 - pe) < 1e-12) return po >= 1 ? 1.0 : null;
        return Math.Round((po - pe) / (1 - pe), 4);
    }

    public static LatencyStatsDto Stats(List<double> values)
    {
        var v = values.Where(x => x >= 0).OrderBy(x => x).ToList();
        if (v.Count == 0) return new LatencyStatsDto();

        return new LatencyStatsDto
        {
            Count = v.Count,
            AvgMs = Math.Round(v.Average(), 1),
            MedianMs = Math.Round(Percentile(v, 0.5), 1),
            P95Ms = Math.Round(Percentile(v, 0.95), 1),
            MinMs = Math.Round(v[0], 1),
            MaxMs = Math.Round(v[^1], 1)
        };
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 1) return sorted[0];
        var rank = p * (sorted.Count - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
    }
}
