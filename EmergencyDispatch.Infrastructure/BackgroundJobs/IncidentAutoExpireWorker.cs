using EmergencyDispatch.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Infrastructure.BackgroundJobs;

/// <summary>
/// Tự hủy sự cố không được Operator xác minh sau AutoExpireHours (mặc định 24h) — CancellationReason.AutoExpired.
/// Sự cố đã xác minh / đã điều xe không bao giờ bị tự hủy.
/// </summary>
public class IncidentAutoExpireWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IncidentAutoExpireWorker> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1);

    public IncidentAutoExpireWorker(IServiceScopeFactory scopeFactory, ILogger<IncidentAutoExpireWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Incident Auto-Expire Worker khởi động (chu kỳ quét: {Interval} phút).", _checkInterval.TotalMinutes);

        // Lần quét đầu sau 1 chu kỳ (không hủy dữ liệu ngay khi vừa khởi động lại server)
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

            var expired = await incidents.AutoExpireStaleIncidentsAsync(TimeSpan.FromHours(settings.AutoExpireHours), cancellationToken);
            if (expired > 0)
            {
                _logger.LogInformation("Auto-expire: đã tự hủy {Count} sự cố không được xác minh sau {Hours} giờ.", expired, settings.AutoExpireHours);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Lỗi khi quét tự hủy sự cố quá hạn.");
        }
    }
}
