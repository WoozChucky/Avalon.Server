using Avalon.World.Maintenance;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Avalon.Server.World.Presence;

public sealed class WorldMaintenanceReconciler(
    WorldMaintenanceCoordinator coordinator,
    ILogger<WorldMaintenanceReconciler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await coordinator.ReconcileAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "World maintenance reconciliation failed; retrying");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
