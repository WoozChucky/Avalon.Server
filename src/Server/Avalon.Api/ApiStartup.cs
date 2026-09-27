using Avalon.Api.Worlds;
using Avalon.Database.Auth;
using Avalon.Database.Character;
using Avalon.Database.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Avalon.Api;

/// <summary>The startup work Program runs before serving, unless it only generates the OpenAPI document.</summary>
public static class ApiStartup
{
    /// <summary>
    /// Validates the options first (#543): <c>ValidateOnStart</c> alone runs only inside
    /// <c>StartAsync</c>, after the migrations below and the cache connection Program makes next,
    /// so a missing cache host would fail with their error instead of naming the setting.
    /// Database:Worlds is checked right after them, here rather than as an options validation,
    /// because OpenAPI generation starts the host with no world configured and skips this (#523).
    /// </summary>
    public static async Task ValidateAndMigrateAsync(IServiceProvider services, ILogger logger)
    {
        services.GetRequiredService<IStartupValidator>().Validate();
        // Builds WorldDatabases, which parses Database:Worlds and refuses it naming the setting,
        // before any database call; the migration loop below reuses that instance.
        _ = services.GetRequiredService<IWorldDatabases>();

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await using AuthDbContext authDb = await scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<AuthDbContext>>().CreateDbContextAsync(CancellationToken.None);
        logger.LogInformation("Migrating database if necessary...");
        // Startup migration — host lifetime not active yet, so CancellationToken.None is intentional.
        await authDb.Database.MigrateAsync(CancellationToken.None);

        // Each world under Database:Worlds (#523), through the factory for a named world.
        IWorldDbContextFactory worldContexts = services.GetRequiredService<IWorldDbContextFactory>();
        foreach (ConfiguredWorld world in services.GetRequiredService<IWorldDatabases>().All)
        {
            await using WorldDbContext worldDb = worldContexts.CreateWorld(world.Id);
            await worldDb.Database.MigrateAsync(CancellationToken.None);
            await using CharacterDbContext characterDb = worldContexts.CreateCharacters(world.Id);
            await characterDb.Database.MigrateAsync(CancellationToken.None);
        }
    }
}
