using Avalon.Crashes.Worker.Processing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Avalon.Crashes.Worker.Diagnostics;

public sealed class CrashWorkerState(TimeProvider time)
{
    private readonly object _sync = new();
    private long _heartbeat = time.GetTimestamp();
    private long _databaseGeneration;
    private bool _databaseReady;
    private volatile bool _toolsReady;
    private volatile bool _storageReady;
    private volatile bool _symbolsReady = true;
    private long _symbolProbeAfter;
    public bool ToolsReady { get => _toolsReady; set => _toolsReady = value; }
    public bool StorageReady { get => _storageReady; set => _storageReady = value; }
    public bool SymbolsReady => _symbolsReady;
    public void SymbolsFailed()
    {
        Interlocked.Exchange(ref _symbolProbeAfter, time.GetUtcNow().AddMinutes(1).ToUnixTimeSeconds());
        _symbolsReady = false;
    }
    public void SymbolsSucceeded() => _symbolsReady = true;
    public bool Paused { get; init; }
    public bool DatabaseReady { get { lock (_sync) return _databaseReady; } }
    public long DatabaseGeneration { get { lock (_sync) return _databaseGeneration; } }
    public bool IntakeReady => !Paused && DatabaseReady && ToolsReady && StorageReady
        && (SymbolsReady || time.GetUtcNow().ToUnixTimeSeconds() >= Interlocked.Read(ref _symbolProbeAfter));
    public bool Alive => time.GetElapsedTime(Interlocked.Read(ref _heartbeat)) < TimeSpan.FromMinutes(2);
    public void Heartbeat() => Interlocked.Exchange(ref _heartbeat, time.GetTimestamp());
    public void DatabaseFailed()
    {
        lock (_sync) { _databaseReady = false; _databaseGeneration++; }
    }
    public void Purged(long generation)
    {
        lock (_sync)
            if (_databaseGeneration == generation) _databaseReady = true;
    }
}

public sealed class CrashHealthCheck(CrashWorkerState state, CrashProcessingBackoff backoff) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(!state.DatabaseReady || !state.ToolsReady || !state.Paused && (!state.StorageReady || !state.SymbolsReady) || backoff.State is not null
            ? HealthCheckResult.Unhealthy("Worker dependency unavailable.") : HealthCheckResult.Healthy());
}
