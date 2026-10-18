using System.Threading.Channels;
using EmergencyDispatch.Application.DTOs.Ai;
using EmergencyDispatch.Application.Interfaces;

namespace EmergencyDispatch.Infrastructure.BackgroundJobs;

/// <summary>
/// Hàng đợi phân tích AI in-memory dựa trên System.Threading.Channels (Singleton).
/// Bounded để tránh tràn bộ nhớ khi AI chậm; khi đầy, producer sẽ chờ (backpressure).
/// </summary>
public sealed class AiAnalysisQueue : IAiAnalysisQueue
{
    public const int DefaultCapacity = 500;

    private readonly Channel<AiAnalysisJob> _channel;

    public AiAnalysisQueue(int capacity = DefaultCapacity)
    {
        _channel = Channel.CreateBounded<AiAnalysisJob>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ValueTask EnqueueAsync(AiAnalysisJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        return _channel.Writer.WriteAsync(job, cancellationToken);
    }

    public IAsyncEnumerable<AiAnalysisJob> DequeueAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
