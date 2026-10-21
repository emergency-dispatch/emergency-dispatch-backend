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
