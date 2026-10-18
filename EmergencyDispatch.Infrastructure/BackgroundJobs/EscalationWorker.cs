using EmergencyDispatch.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Infrastructure.BackgroundJobs;

/// <summary>
/// Background Service định kỳ 30 giây quét các sự cố mức độ nghiêm trọng (Level 3+)
/// chưa có đơn vị cứu hộ tiếp nhận sau thời gian quy định để tự động leo thang (Escalated)
/// và phát cảnh báo còi hú toàn hệ thống tới Dispatcher và Ban Quản lý.
/// </summary>
public sealed class EscalationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EscalationWorker> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);

    public EscalationWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<EscalationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚨 Escalation Rule Engine Worker đã khởi động (Chu kỳ quét: {Interval}s).", _checkInterval.TotalSeconds);

        // Chờ 5 giây sau khi ứng dụng khởi động để ổn định kết nối trước khi quét lần đầu
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            await RunEscalationCheckAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        using var timer = new PeriodicTimer(_checkInterval);

        try
        {
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunEscalationCheckAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Dừng worker khi ứng dụng shutdown
        }

        _logger.LogInformation("Escalation Rule Engine Worker đã dừng.");
    }

    private async Task RunEscalationCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var escalationService = scope.ServiceProvider.GetRequiredService<IEscalationService>();

            var escalatedCount = await escalationService.ProcessEscalationsAsync(cancellationToken);
            if (escalatedCount > 0)
            {
                _logger.LogWarning("🚨 [Escalation Engine] Đã tự động kích hoạt leo thang {Count} sự cố khẩn cấp quá hạn!", escalatedCount);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Lỗi xảy ra trong quá trình quét tự động leo thang sự cố.");
        }
    }
}
