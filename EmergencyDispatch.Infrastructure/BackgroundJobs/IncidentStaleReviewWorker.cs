using EmergencyDispatch.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Infrastructure.BackgroundJobs;

/// <summary>
/// BR-09: sự cố chưa được Operator xác minh sau StaleReviewHours (setting dispatch.autoExpireHours, mặc định 24h)
/// được đánh dấu "Quá hạn – cần rà soát" và cảnh báo Operator của trạm + Admin.
/// Không tự hủy: sự cố vẫn ở hàng đợi, chỉ Operator / Admin mới hủy được.
/// </summary>
public class IncidentStaleReviewWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IncidentStaleReviewWorker> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1);

    public IncidentStaleReviewWorker(IServiceScopeFactory scopeFactory, ILogger<IncidentStaleReviewWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Incident Stale Review Worker khởi động (chu kỳ quét: {Interval} phút).", _checkInterval.TotalMinutes);

        // Lần quét đầu sau 1 chu kỳ (không gửi loạt cảnh báo ngay khi vừa khởi động lại server)
        using var timer = new PeriodicTimer(_checkInterval);
        try
        {
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Dừng worker khi ứng dụng shutdown
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var settings = await scope.ServiceProvider.GetRequiredService<ISystemSettingsService>().GetDispatchSettingsAsync(cancellationToken);
            var incidents = scope.ServiceProvider.GetRequiredService<IIncidentService>();

            var flagged = await incidents.FlagStaleIncidentsAsync(TimeSpan.FromHours(settings.AutoExpireHours), cancellationToken);
            if (flagged > 0)
            {
                _logger.LogWarning("Stale review: đã đánh dấu {Count} sự cố chưa được xác minh sau {Hours} giờ (cần Operator rà soát).", flagged, settings.AutoExpireHours);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Lỗi khi quét sự cố quá hạn chưa xác minh.");
        }
    }
}
