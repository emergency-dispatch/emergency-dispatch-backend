using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.DTOs.Benchmark;

/// <summary>
/// Một sự cố trong bộ dữ liệu thực nghiệm (RQ2: thuật toán điều phối, RQ3: hiệu quả AI hỗ trợ Operator).
/// Mọi mốc thời gian là UTC; *Ms là độ trễ tính bằng mili-giây.
/// </summary>
public class TelemetryIncidentRowDto
{
    public Guid IncidentId { get; set; }
    public DateTime CreatedAt { get; set; }
    public IncidentStatus Status { get; set; }
    public string? ResponsibleStation { get; set; }
    public bool IsOutOfServiceArea { get; set; }

    /// <summary>
    /// CitizenApp = SOS qua app; khác = Operator tự tạo (TOperatorMs = null cho các sự cố này)
    /// </summary>
    public IncidentSource Source { get; set; }

    // AI
    public bool HasAi { get; set; }
    public bool? AiSuccess { get; set; }
    public DateTime? AiProcessedAt { get; set; }
    public long? AiModelLatencyMs { get; set; }
    public string? AiModel { get; set; }
    public SeverityLevel? AiSeverity { get; set; }
    public IncidentEventType? AiEventType { get; set; }

    // Operator
    public DateTime? VerifiedAt { get; set; }
    public Guid? VerifiedByUserId { get; set; }
    public SeverityLevel ConfirmedSeverity { get; set; }
    public IncidentEventType ConfirmedEventType { get; set; }
    public bool WasAiOverridden { get; set; }

    // Điều phối
    public DateTime? FirstDispatchAt { get; set; }
    public string? Algorithm { get; set; }
    public int UnitsDispatched { get; set; }
    public DateTime? FirstAcceptedAt { get; set; }
    public DateTime? FirstArrivedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public CancellationReason? CancellationReason { get; set; }

    // Độ trễ suy ra
    public long? TAiMs { get; set; }
    public long? TOperatorMs { get; set; }
    public long? TDispatchMs { get; set; }
    public long? TResponseMs { get; set; }
    public long? TResolutionMs { get; set; }
}
