using System.Data.Common;
using Avalon.Database.Crashes;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Api.Identity.Crashes;

public interface ICrashSchemaReadiness
{
    Task<bool> IsReadyAsync(CancellationToken ct);
}

/// <summary>Lazy read-only schema check, cached for 30 seconds; never part of global startup/readiness.</summary>
public sealed class CrashSchemaReadiness(IServiceProvider services, TimeProvider timeProvider) : ICrashSchemaReadiness, IDisposable
{
    private readonly SemaphoreSlim _probeLock = new(1, 1);
    private DateTimeOffset _nextProbe;
    private bool _ready;

    public async Task<bool> IsReadyAsync(CancellationToken ct)
    {
        await _probeLock.WaitAsync(ct);
        try
        {
            if (timeProvider.GetUtcNow() < _nextProbe) return _ready;
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5), timeProvider);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);
            try
            {
                IDbContextFactory<CrashesDbContext> factory = services.GetRequiredService<IDbContextFactory<CrashesDbContext>>();
                await using CrashesDbContext db = await factory.CreateDbContextAsync(linked.Token);
                db.Database.SetCommandTimeout(TimeSpan.FromSeconds(5));
                IEnumerable<string> applied = await db.Database.GetAppliedMigrationsAsync(linked.Token);
                _ready = applied.Contains("20261023221459_InitialCrashAnalysis", StringComparer.Ordinal);
            }
            catch (Exception exception) when (exception is DbException or TimeoutException or InvalidOperationException or ArgumentException
                || exception is OperationCanceledException && !ct.IsCancellationRequested)
            {
                _ready = false;
            }
            _nextProbe = timeProvider.GetUtcNow().AddSeconds(30);
            return _ready;
        }
        finally
        {
            _probeLock.Release();
        }
    }

    public void Dispose() => _probeLock.Dispose();
}
