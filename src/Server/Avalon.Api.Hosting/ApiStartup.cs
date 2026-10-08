using Avalon.Api.Hosting.Worlds;
using Avalon.Database.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Hosting;

/// <summary>The startup work an API process runs before serving, unless it only generates the OpenAPI document.</summary>
public static class ApiStartup
{
    /// <summary>
    /// Validates the options first (#543): <c>ValidateOnStart</c> alone runs only inside
    /// <c>StartAsync</c>, after the migrations below and the cache connection the host makes next,
    /// so a missing cache host would fail with their error instead of naming the setting.
    /// Database:Worlds is checked right after them, here rather than as an options validation,
    /// because OpenAPI generation starts the host with no world configured and skips this (#523).
    /// Then the services' own checks (<see cref="IApiStartupCheck"/>), still before any database call;
    /// then the auth schema (<see cref="AuthSchemaGate"/>: the owner migrates it, a reader waits for it)
    /// and each world's databases, through <see cref="ApiDatabaseMigrator"/> (#523).
    /// </summary>
    public static async Task ValidateAndMigrateAsync(IServiceProvider services, ILogger logger)
    {
        services.GetRequiredService<IStartupValidator>().Validate();
        // Builds WorldDatabases, which parses Database:Worlds and refuses it naming the setting,
        // before any database call; the world check below reuses that instance. A process that
        // reads no world database has none.
        WorldDatabases? worlds = services.GetService<WorldDatabases>();
        foreach (IApiStartupCheck check in services.GetServices<IApiStartupCheck>())
            check.Check(services);

        AuthSchemaGate gate = services.GetRequiredService<AuthSchemaGate>();
        ApiDatabaseMigrator migrator = services.GetRequiredService<ApiDatabaseMigrator>();
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        if (gate.Role == AuthSchemaRole.Owner)
            logger.LogInformation("Migrating database if necessary...");
        else
            logger.LogInformation("Waiting until the auth database has no migration this build knows of pending...");
        // Startup migration — host lifetime not active yet, so CancellationToken.None is intentional.
        // Auth first: a failure stops the api. Then each world under Database:Worlds is checked, not
        // migrated (each world server migrates its own): one that can't be reached is logged and
        // answers 503 until the recheck reaches its databases, and the others serve (#523).
        await gate.PassAsync(scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthDbContext>>(), migrator,
            CancellationToken.None);
        if (worlds is not null)
        {
            await migrator.CheckWorldsAsync(worlds, services.GetRequiredService<IWorldDbContextFactory>(),
                CancellationToken.None);
            if (worlds.All.Any(world => world.Status == WorldDatabaseStatus.Unavailable))
                services.GetService<WorldDatabaseRecheck>()?.Watch(worlds);
        }
    }
}
