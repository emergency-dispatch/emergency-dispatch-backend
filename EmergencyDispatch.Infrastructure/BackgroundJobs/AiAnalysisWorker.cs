using EmergencyDispatch.Application.DTOs.Ai;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Infrastructure.BackgroundJobs;

/// <summary>
/// Background Worker tiêu thụ hàng đợi AI: mỗi job chạy trong một DI scope riêng
/// (DbContext Scoped), giới hạn số job song song bằng SemaphoreSlim.
/// Khi khởi động sẽ nạp lại các sự cố kẹt ở AiProcessing (mất job do restart).
/// </summary>
public sealed class AiAnalysisWorker : BackgroundService
{
    private readonly IAiAnalysisQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AiAnalysisWorker> _logger;
    private readonly int _maxConcurrency;

    public AiAnalysisWorker(
        IAiAnalysisQueue queue,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<AiAnalysisWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _maxConcurrency = int.TryParse(configuration["AiWorker:MaxConcurrency"], out var configured) && configured > 0
            ? configured
            : 3;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AI Analysis Worker khởi động (MaxConcurrency={Max}).", _maxConcurrency);

        await RecoverStuckIncidentsAsync(stoppingToken);

        using var throttler = new SemaphoreSlim(_maxConcurrency);
        var running = new List<Task>();

        try
        {
            await foreach (var job in _queue.DequeueAllAsync(stoppingToken))
            {
                await throttler.WaitAsync(stoppingToken);
                running.Add(Task.Run(async () =>
                {
                    try
                    {
                        await ProcessJobAsync(job, stoppingToken);
                    }
                    finally
                    {
                        throttler.Release();
                    }
                }, stoppingToken));

                running.RemoveAll(t => t.IsCompleted);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Ứng dụng đang tắt
        }

        await Task.WhenAll(running.Where(t => !t.IsCompleted));
        _logger.LogInformation("AI Analysis Worker đã dừng.");
    }

    private async Task ProcessJobAsync(AiAnalysisJob job, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var incidentService = scope.ServiceProvider.GetRequiredService<IIncidentService>();
            await incidentService.ProcessAiAnalysisAsync(job, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Bỏ qua khi tắt ứng dụng, job sẽ được khôi phục ở lần khởi động sau
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI Worker: lỗi khi xử lý sự cố {IncidentId}.", job.IncidentId);
        }
    }

    private async Task RecoverStuckIncidentsAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IIncidentRepository>();
            var stuck = await repository.GetStuckAiProcessingAsync(stoppingToken);

            foreach (var incident in stuck)
            {
                var mediaUrl = incident.MediaItems.FirstOrDefault(m => m.MediaType == MediaType.Photo)?.MediaUrl;
                if (string.IsNullOrWhiteSpace(mediaUrl)) continue;

                await _queue.EnqueueAsync(
                    new AiAnalysisJob(incident.Id, mediaUrl, incident.Description, DateTime.UtcNow),
                    stoppingToken);
            }

            if (stuck.Count > 0)
            {
                _logger.LogWarning("AI Worker: đã nạp lại {Count} sự cố kẹt ở trạng thái AiProcessing.", stuck.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Không chặn worker nếu DB tạm thời không truy cập được
            _logger.LogWarning(ex, "AI Worker: không thể khôi phục các sự cố kẹt ở AiProcessing.");
        }
    }
}
