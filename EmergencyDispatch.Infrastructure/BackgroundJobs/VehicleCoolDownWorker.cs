using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Infrastructure.BackgroundJobs;

/// <summary>
/// BR-27: Quét định kỳ các xe đã hết thời gian hồi sức (CoolDown) sau nhiệm vụ và trả về trạng thái Available
/// </summary>
public class VehicleCoolDownWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VehicleCoolDownWorker> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);

    public VehicleCoolDownWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<VehicleCoolDownWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Vehicle CoolDown Worker khởi động (Chu kỳ quét: {Interval}s).", _checkInterval.TotalSeconds);

        using var timer = new PeriodicTimer(_checkInterval);

        try
        {
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ReleaseExpiredAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Dừng worker khi ứng dụng shutdown
        }

        _logger.LogInformation("Vehicle CoolDown Worker đã dừng.");
    }

    private async Task ReleaseExpiredAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRescueUnitRepository>();
            var released = await repository.ReleaseExpiredCoolDownsAsync(DateTime.UtcNow, cancellationToken);
            if (released.Count > 0)
            {
                _logger.LogInformation("CoolDown: {Count} xe đã hết thời gian hồi sức, trở lại trạng thái sẵn sàng.", released.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Lỗi khi quét xe hết thời gian hồi sức.");
        }
    }
}
