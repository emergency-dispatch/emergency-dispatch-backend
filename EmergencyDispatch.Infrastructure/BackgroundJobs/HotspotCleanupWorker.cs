using EmergencyDispatch.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Infrastructure.BackgroundJobs;

/// <summary>
/// Background Service định kỳ 5 phút dọn dẹp các điểm nghẽn giao thông đã hết hạn TTL (ExpiresAt &lt; UtcNow)
/// </summary>
public sealed class HotspotCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HotspotCleanupWorker> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);

    public HotspotCleanupWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<HotspotCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Hotspot Cleanup Worker khởi động (Chu kỳ quét: {Interval} phút).", _checkInterval.TotalMinutes);

        using var timer = new PeriodicTimer(_checkInterval);

        try
        {
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                await DoCleanupAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Dừng worker theo chu kỳ tắt ứng dụng
        }

        _logger.LogInformation("Hotspot Cleanup Worker đã dừng.");
    }

    private async Task DoCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var trafficService = scope.ServiceProvider.GetRequiredService<ITrafficService>();
            var cleanedCount = await trafficService.CleanupExpiredHotspotsAsync(cancellationToken);
            if (cleanedCount > 0)
            {
                _logger.LogInformation("Hotspot Cleanup: Đã tự động giải tỏa {Count} điểm nghẽn hết hạn.", cleanedCount);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Lỗi trong quá trình quét dọn điểm nghẽn giao thông hết hạn.");
        }
    }
}
