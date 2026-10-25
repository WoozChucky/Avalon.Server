using Avalon.Crashes.Worker.Diagnostics;

namespace Avalon.Crashes.Worker.Maintenance;

public interface ICrashWorkerRuntime
{
    Task MigrateAsync(CancellationToken ct);
    Task ValidateAsync(CancellationToken ct);
    Task CleanupAsync(CancellationToken ct);
    Task DiscoverAsync(CancellationToken ct);
    Task ProcessOneAsync(CancellationToken ct);
}

/// <summary>Independent maintenance, heartbeat, discovery and single-report loops. Only run owns migrations.</summary>
public sealed class CrashWorkerService(ICrashWorkerRuntime runtime, CrashWorkerState state, TimeProvider time) : BackgroundService
{
    private readonly TaskCompletionSource _firstPurge = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.WhenAll(MaintainAsync(stoppingToken), ValidateAsync(stoppingToken), HeartbeatAsync(stoppingToken),
                IntakeAsync(true, stoppingToken), IntakeAsync(false, stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task MaintainAsync(CancellationToken ct)
    {
        bool migrated = false;
        while (!ct.IsCancellationRequested)
        {
            long generation = state.DatabaseGeneration;
            try
            {
                if (!migrated)
                {
                    await runtime.MigrateAsync(ct);
                    migrated = true;
                }
                await runtime.CleanupAsync(ct);
                state.Purged(generation);
                _firstPurge.TrySetResult();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch { state.DatabaseFailed(); }
            await Task.Delay(TimeSpan.FromMinutes(1), time, ct);
        }
    }

    private async Task ValidateAsync(CancellationToken ct)
    {
        await _firstPurge.Task.WaitAsync(ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await runtime.ValidateAsync(ct);
                state.ToolsReady = true;
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch { state.ToolsReady = false; }
            await Task.Delay(TimeSpan.FromMinutes(1), time, ct);
        }
    }

    private async Task HeartbeatAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            state.Heartbeat();
            await Task.Delay(TimeSpan.FromSeconds(30), time, ct);
        }
    }

    private async Task IntakeAsync(bool discovery, CancellationToken ct)
    {
        await _firstPurge.Task.WaitAsync(ct);
        while (!ct.IsCancellationRequested)
        {
            bool ready = discovery ? !state.Paused && state.DatabaseReady && state.ToolsReady : state.IntakeReady;
            if (ready)
            {
                try
                {
                    if (discovery) await runtime.DiscoverAsync(ct);
                    else await runtime.ProcessOneAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch { state.DatabaseFailed(); }
            }
            await Task.Delay(discovery && ready ? TimeSpan.FromMinutes(1) : TimeSpan.FromSeconds(1), time, ct);
        }
    }
}
