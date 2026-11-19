using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Infrastructure.BackgroundJobs;

public sealed class StationTransferWorker(IServiceScopeFactory scopes, ILogger<StationTransferWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            do
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var handover = scope.ServiceProvider.GetRequiredService<IStationTransferService>();
                    // Recover the crash window after AI saves its result but before the timeout is armed.
                    // Retry auto-dispatch first; its existing lock/idempotence checks prevent duplicate missions.
                    var repository = scope.ServiceProvider.GetRequiredService<IIncidentStationTransferRepository>();
                    foreach (var id in await repository.GetUnarmedIdsAsync(DateTime.UtcNow.AddMinutes(-1), 100, stoppingToken))
                    {
                        try
                        {
                            var autoDispatch = scope.ServiceProvider.GetService<IAutoDispatchService>();
                            if (autoDispatch != null) await autoDispatch.TryAutoDispatchAsync(id, stoppingToken);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            logger.LogError(ex, "Auto-dispatch recovery failed for {IncidentId}; releasing it for manual handling.", id);
                        }
                        finally
                        {
                            if (!stoppingToken.IsCancellationRequested)
                                await handover.MarkReadyForOperatorAsync(id, stoppingToken);
                        }
                    }
                    await handover.ProcessPendingAsync(stoppingToken);
                    await scope.ServiceProvider.GetRequiredService<IStationTransferDispatchService>().ProcessPendingAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogError(ex, "Station handover scan failed; retrying on the next scan."); }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
