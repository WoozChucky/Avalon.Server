using Avalon.Database.Auth;
using Avalon.Database.Character;
using Avalon.Database.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Api.Worlds;

/// <summary>
/// The api's startup migration (#523). Auth first: a failure there propagates and stops the api, as
/// it always has. Then each configured world, its World database and then its Characters database:
/// a world that fails is logged (its id and the exception's type, never a connection string or the
/// driver's message) and marked <see cref="WorldDatabaseStatus.Unavailable"/>, so it answers 503
/// until the next restart while the other worlds serve. There is no retry. Every world's status is
/// logged at the end.
/// </summary>
public sealed class ApiDatabaseMigrator
{
    private readonly ILogger<ApiDatabaseMigrator> _logger;
    private readonly Func<DbContext, CancellationToken, Task> _migrate;

    /// <param name="migrate">How one context is migrated; <c>Database.MigrateAsync</c> unless a test says otherwise.</param>
    public ApiDatabaseMigrator(ILogger<ApiDatabaseMigrator> logger, Func<DbContext, CancellationToken, Task>? migrate = null)
    {
        _logger = logger;
        _migrate = migrate ?? ((context, cancellationToken) => context.Database.MigrateAsync(cancellationToken));
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
                await using (WorldDbContext worldDb = contexts.CreateWorld(world.Id))
                {
                    await _migrate(worldDb, cancellationToken);
                }

                await using (CharacterDbContext charactersDb = contexts.CreateCharacters(world.Id))
                {
                    await _migrate(charactersDb, cancellationToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException
                                              || !cancellationToken.IsCancellationRequested)
            {
                worlds.MarkUnavailable(world.Id);
                _logger.LogError(
                    "World {WorldId} is unavailable until the next restart: migrating its databases failed with {ExceptionType}",
                    world.Id.Value, exception.GetType().Name);
            }
        }

        foreach (ConfiguredWorld world in worlds.All)
            _logger.LogInformation("World {WorldId}: {WorldStatus}", world.Id.Value, world.Status);
    }
}
