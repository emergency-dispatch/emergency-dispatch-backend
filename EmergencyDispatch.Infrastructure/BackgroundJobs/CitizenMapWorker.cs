using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Infrastructure.BackgroundJobs;

/// <summary>Invalidates citizen map snapshots when the eligible set changes, including removals.</summary>
public sealed class CitizenMapWorker(IServiceScopeFactory scopes, ILogger<CitizenMapWorker> logger, TimeProvider? timeProvider = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? previousRevision = null;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10), timeProvider ?? TimeProvider.System);
        try
        {
            do
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var revision = await scope.ServiceProvider.GetRequiredService<ICitizenMapRepository>().GetRevisionAsync(stoppingToken);
                    if (revision != previousRevision)
                    {
                        await scope.ServiceProvider.GetRequiredService<ICitizenMapNotifier>()
                            .ChangedAsync(new CitizenMapChangedDto(revision, DateTime.UtcNow), stoppingToken);
                        previousRevision = revision; // Failed sends must be retried on the next scan.
                    }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "Citizen map refresh failed; retrying next scan.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
