using Avalon.Database.Auth;
using Avalon.Database.Character;
using Avalon.Database.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Api.Worlds;

/// <summary>
/// The api's startup check (#523). Auth is migrated first: a failure there propagates and stops the api.
/// Each world server is the only migrator of its own World and Characters databases (homelab spec
/// 2026-09-28-release-channels-design D2), so here each configured world's databases are only checked for
/// reachability. A world that cannot be reached, or whose check throws, is logged (its id, and the
/// exception's type, never a connection string or the driver's message) and marked
/// <see cref="WorldDatabaseStatus.Unavailable"/>, so it answers 503 until the next restart while the other
/// worlds serve. There is no retry. Every world's status is logged at the end.
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

    public async Task MigrateAsync(IDbContextFactory<AuthDbContext> auth, WorldDatabases worlds,
        IWorldDbContextFactory contexts, CancellationToken cancellationToken)
    {
        await using (AuthDbContext authDb = await auth.CreateDbContextAsync(cancellationToken))
        {
            await _migrate(authDb, cancellationToken);
        }

        foreach (ConfiguredWorld world in worlds.All)
        {
            try
            {
                bool reachable;
                await using (WorldDbContext worldDb = contexts.CreateWorld(world.Id))
                {
                    reachable = await _canConnect(worldDb, cancellationToken);
                }

                if (reachable)
                {
                    await using CharacterDbContext charactersDb = contexts.CreateCharacters(world.Id);
                    reachable = await _canConnect(charactersDb, cancellationToken);
                }

                if (!reachable)
                {
                    worlds.MarkUnavailable(world.Id);
                    _logger.LogError("World {WorldId} is unavailable until the next restart: its databases cannot be reached",
                        world.Id.Value);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException
                                              || !cancellationToken.IsCancellationRequested)
            {
                worlds.MarkUnavailable(world.Id);
                _logger.LogError(
                    "World {WorldId} is unavailable until the next restart: checking its databases failed with {ExceptionType}",
                    world.Id.Value, exception.GetType().Name);
            }
        }

        foreach (ConfiguredWorld world in worlds.All)
            _logger.LogInformation("World {WorldId}: {WorldStatus}", world.Id.Value, world.Status);
    }
}
