namespace Avalon.Api.Hosting.Worlds;

/// <summary>
/// Checks again, in the background, the worlds the startup check found unavailable, and makes each available once its
/// databases answer (<see cref="ApiDatabaseMigrator.RecheckUnavailableAsync"/>), so a world whose databases did not
/// exist yet when the api started (a fresh install, where the world server creates them) serves without a restart.
/// It runs only when <see cref="ApiStartup"/> hands it the worlds (<see cref="Watch"/>), that is when a world was
/// unavailable at startup: never for OpenAPI generation or a test host. The first recheck comes after
/// <see cref="FirstDelay"/>, each later one after twice the previous wait, at most <see cref="MaxDelay"/>, until every
/// world is available or the host stops. It is off every request path: a request reads the status only. It resolves
/// nothing from the worlds at construction, so a host that never parses <c>Database:Worlds</c> can build it.
/// </summary>
public sealed class WorldDatabaseRecheck(ApiDatabaseMigrator migrator, TimeProvider clock) : BackgroundService
{
    public static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(1);

    private (WorldDatabases Worlds, IWorldDbContextFactory Contexts)? _watched;

    /// <summary>Startup, before the host runs: the worlds to recheck, at least one of them unavailable.</summary>
    public void Watch(WorldDatabases worlds, IWorldDbContextFactory contexts) => _watched = (worlds, contexts);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_watched is not var (worlds, contexts)) return;

        TimeSpan delay = FirstDelay;
        bool remaining = true;
        while (remaining && !stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(delay, clock, stoppingToken);
            remaining = await migrator.RecheckUnavailableAsync(worlds, contexts, stoppingToken);
            delay = delay * 2 < MaxDelay ? delay * 2 : MaxDelay;
        }
    }
}
