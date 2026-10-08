using Avalon.Database.Auth;
using Avalon.Database.Character;
using Avalon.Database.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Api.Hosting.Worlds;

/// <summary>
/// The api's startup check (#523). Auth is migrated first: a failure there propagates and stops the api.
/// Each world server is the only migrator of its own World and Characters databases (homelab spec
/// 2026-09-28-release-channels-design D2), so here each configured world's databases are only checked for
/// reachability. A world that cannot be reached, or whose check throws, is logged (its id, and the
/// exception's type, never a connection string or the driver's message) and marked
/// <see cref="WorldDatabaseStatus.Unavailable"/>, so it answers 503 while the other worlds serve, until
/// <see cref="RecheckUnavailableAsync"/> (run by <see cref="WorldDatabaseRecheck"/>) reaches its databases.
/// Every world's status is logged at the end. Only the databases this process reads are checked
/// (<see cref="WorldDatabaseParts"/>, #794), and only the service that owns the auth schema migrates it
/// (<see cref="AuthSchemaGate"/>).
/// </summary>
public sealed class ApiDatabaseMigrator
{
    private readonly ILogger<ApiDatabaseMigrator> _logger;
    private readonly Func<DbContext, CancellationToken, Task> _migrate;
    private readonly Func<DbContext, CancellationToken, Task<bool>> _canConnect;

    /// <param name="migrate">How the auth context is migrated; <c>Database.MigrateAsync</c> unless a test says otherwise.</param>
    /// <param name="canConnect">How a world's context is checked; <c>Database.CanConnectAsync</c> unless a test says otherwise.</param>
    public ApiDatabaseMigrator(ILogger<ApiDatabaseMigrator> logger,
        Func<DbContext, CancellationToken, Task>? migrate = null,
        Func<DbContext, CancellationToken, Task<bool>>? canConnect = null)
    {
        _logger = logger;
        _migrate = migrate ?? ((context, cancellationToken) => context.Database.MigrateAsync(cancellationToken));
        _canConnect = canConnect ?? ((context, cancellationToken) => context.Database.CanConnectAsync(cancellationToken));
    }

    /// <summary>Migrates the auth database, then checks every world's databases.</summary>
    public async Task MigrateAsync(IDbContextFactory<AuthDbContext> auth, WorldDatabases worlds,
        IWorldDbContextFactory contexts, CancellationToken cancellationToken)
    {
        await MigrateAuthAsync(auth, cancellationToken);
        await CheckWorldsAsync(worlds, contexts, cancellationToken);
    }

    /// <summary>Migrates the auth database; a failure propagates and stops the api.</summary>
    public async Task MigrateAuthAsync(IDbContextFactory<AuthDbContext> auth, CancellationToken cancellationToken)
    {
        await using AuthDbContext authDb = await auth.CreateDbContextAsync(cancellationToken);
        await _migrate(authDb, cancellationToken);
    }

    /// <summary>
    /// Checks the databases of every world this process reads; one that cannot be reached is marked unavailable and
    /// the others still serve.
    /// </summary>
    public async Task CheckWorldsAsync(WorldDatabases worlds, IWorldDbContextFactory contexts,
        CancellationToken cancellationToken)
    {
        foreach (ConfiguredWorld world in worlds.All)
        {
            (bool reachable, string? exceptionType) = await ProbeAsync(world, contexts, cancellationToken);
            if (reachable) continue;

            worlds.MarkUnavailable(world.Id);
            if (exceptionType is null)
            {
                _logger.LogError("World {WorldId} is unavailable until its databases can be reached: they cannot be reached now",
                    world.Id.Value);
            }
            else
            {
                _logger.LogError(
                    "World {WorldId} is unavailable until its databases can be reached: checking them failed with {ExceptionType}",
                    world.Id.Value, exceptionType);
            }
        }

        foreach (ConfiguredWorld world in worlds.All)
            _logger.LogInformation("World {WorldId}: {WorldStatus}", world.Id.Value, world.Status);
    }

    /// <summary>
    /// Checks again the databases of each world marked unavailable, and marks available each that can now be reached
    /// (on a fresh install the world server creates them only at its first start). A failure is logged at Debug only:
    /// the startup check has already said the world is unavailable. Returns whether a world is still unavailable.
    /// </summary>
    public async Task<bool> RecheckUnavailableAsync(WorldDatabases worlds, IWorldDbContextFactory contexts,
        CancellationToken cancellationToken)
    {
        bool remaining = false;
        foreach (ConfiguredWorld world in worlds.All)
        {
            if (world.Status != WorldDatabaseStatus.Unavailable) continue;

            (bool reachable, string? exceptionType) = await ProbeAsync(world, contexts, cancellationToken);
            if (reachable)
            {
                world.MarkAvailable();
                _logger.LogInformation("World {WorldId} is available: its databases can be reached now", world.Id.Value);
            }
            else
            {
                remaining = true;
                _logger.LogDebug("World {WorldId} is still unavailable: {ExceptionType}", world.Id.Value,
                    exceptionType ?? "its databases cannot be reached");
            }
        }

        return remaining;
    }

    /// <summary>
    /// Whether every database of <paramref name="world"/> the process reads answers: its World database, then its
    /// Characters database once the World database answered (or was not read). When the check threw, the exception's
    /// type name, never its message, which can carry a connection string. A cancellation of
    /// <paramref name="cancellationToken"/> propagates.
    /// </summary>
    private async Task<(bool Reachable, string? ExceptionType)> ProbeAsync(ConfiguredWorld world,
        IWorldDbContextFactory contexts, CancellationToken cancellationToken)
    {
        try
        {
            if (world.WorldConnectionString is not null)
            {
                await using WorldDbContext worldDb = contexts.CreateWorld(world.Id);
                if (!await _canConnect(worldDb, cancellationToken)) return (false, null);
            }

            if (world.CharactersConnectionString is not null)
            {
                await using CharacterDbContext charactersDb = contexts.CreateCharacters(world.Id);
                if (!await _canConnect(charactersDb, cancellationToken)) return (false, null);
            }

            return (true, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
                                          || !cancellationToken.IsCancellationRequested)
        {
            return (false, exception.GetType().Name);
        }
    }
}
