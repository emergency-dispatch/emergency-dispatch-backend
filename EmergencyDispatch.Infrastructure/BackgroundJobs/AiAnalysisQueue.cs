using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using EmergencyDispatch.Application.DTOs.Ai;
using EmergencyDispatch.Application.Interfaces;

namespace EmergencyDispatch.Infrastructure.BackgroundJobs;

/// <summary>
/// Hàng đợi phân tích AI in-memory dựa trên System.Threading.Channels (Singleton).
/// Kênh chính giới hạn <see cref="DefaultCapacity"/> job. Khi kênh đầy, job sang hàng tràn trong bộ nhớ thay vì bắt
/// request SOS chờ: trước đây (FullMode Wait) SOS phải đợi tới khi AI xử lý xong bớt job mới trả về, load test đo được
/// p95 68 s và Operator cũng nhận thông báo trễ theo. Mỗi lần worker lấy một job, job kế tiếp ở hàng tràn được chuyển
/// sang kênh chính nên thứ tự FIFO được giữ. Sự cố đã lưu DB trước khi vào hàng đợi; nếu tiến trình khởi động lại,
/// AiAnalysisWorker nạp lại các sự cố còn AiProcessing.
/// </summary>
public sealed class AiAnalysisQueue : IAiAnalysisQueue
{
    public const int DefaultCapacity = 500;

    private readonly Channel<AiAnalysisJob> _channel;
    private readonly ConcurrentQueue<AiAnalysisJob> _overflow = new();
    private readonly object _gate = new();

    public AiAnalysisQueue(int capacity = DefaultCapacity)
    {
        _channel = Channel.CreateBounded<AiAnalysisJob>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>
    /// Số job đang chờ ở hàng tràn (kênh chính đã đầy)
    /// </summary>
    public int OverflowCount => _overflow.Count;

    /// <summary>
    /// Đưa job vào hàng đợi, không bao giờ chờ: kênh chính đầy thì xếp vào hàng tràn
    /// </summary>
    public ValueTask EnqueueAsync(AiAnalysisJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            // Đã có job ở hàng tràn → job mới xếp sau để giữ FIFO
            if (!_overflow.IsEmpty || !_channel.Writer.TryWrite(job))
            {
                _overflow.Enqueue(job);
            }
        }

        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<AiAnalysisJob> DequeueAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (await _channel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_channel.Reader.TryRead(out var job))
            {
                PromoteOverflow();
                yield return job;
            }
        }
    }

    // Chuyển job từ hàng tràn sang kênh chính khi kênh có chỗ
    private void PromoteOverflow()
    {
        if (_overflow.IsEmpty) return;

        lock (_gate)
        {
            while (_overflow.TryPeek(out var next) && _channel.Writer.TryWrite(next))
            {
                _overflow.TryDequeue(out _);
            }
        }
    }
}
