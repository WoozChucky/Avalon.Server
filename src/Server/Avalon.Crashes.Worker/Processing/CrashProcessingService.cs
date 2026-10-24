using System.Data.Common;
using Avalon.Crashes.Persistence;

namespace Avalon.Crashes.Worker.Processing;

/// <summary>Single-report intake. Register only after migrations, scratch cleanup and tool readiness succeed.</summary>
public sealed class CrashProcessingService(ICrashQueue queue, CrashProcessor processor,
    CrashProcessingBackoff backoff, TimeProvider? timeProvider = null) : BackgroundService
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                CrashProcessingBackoffState? state = backoff.State;
                DateTimeOffset now = _time.GetUtcNow();
                if (state is not null && state.RetryAt > now)
                {
                    await Task.Delay(state.RetryAt - now, _time, stoppingToken);
                    continue;
                }
                CrashLease? lease = await queue.ClaimAsync(_time.GetUtcNow(), stoppingToken);
                if (lease is null)
                    await Task.Delay(TimeSpan.FromSeconds(1), _time, stoppingToken);
                else
                    await processor.ProcessAsync(lease, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is DbException or TimeoutException or IOException)
            {
                // Durable claim/completion remains authoritative after database outages and uncertain commits.
                await Task.Delay(TimeSpan.FromMinutes(1), _time, stoppingToken);
            }
        }
    }
}
