using EmergencyDispatch.Application.DTOs.Ai;

namespace EmergencyDispatch.Application.Interfaces;

/// <summary>
/// Hàng đợi nền cho các yêu cầu phân tích AI (tách khỏi request HTTP để SOS phản hồi &lt; 1.5s)
/// </summary>
public interface IAiAnalysisQueue
{
    /// <summary>
    /// Đưa một công việc phân tích AI vào hàng đợi
    /// </summary>
    ValueTask EnqueueAsync(AiAnalysisJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc tuần tự các công việc trong hàng đợi (dùng bởi Background Worker)
    /// </summary>
    IAsyncEnumerable<AiAnalysisJob> DequeueAllAsync(CancellationToken cancellationToken);
}
