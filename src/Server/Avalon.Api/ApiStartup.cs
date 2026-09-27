using Avalon.Api.Worlds;
using Avalon.Database.Auth;
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
    /// Then the migrations, through ApiDatabaseMigrator (#523).
    /// </summary>
    public static async Task ValidateAndMigrateAsync(IServiceProvider services, ILogger logger)
    {
        services.GetRequiredService<IStartupValidator>().Validate();
        // Builds WorldDatabases, which parses Database:Worlds and refuses it naming the setting,
        // before any database call; the migrator below reuses that instance.
        WorldDatabases worlds = services.GetRequiredService<WorldDatabases>();

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        logger.LogInformation("Migrating database if necessary...");
        // Startup migration — host lifetime not active yet, so CancellationToken.None is intentional.
        // Auth first: a failure stops the api. Then each world under Database:Worlds: one that fails
        // is logged and answers 503 until the next restart, and the others serve (#523).
        await new ApiDatabaseMigrator(services.GetRequiredService<ILogger<ApiDatabaseMigrator>>())
            .MigrateAsync(
                scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthDbContext>>(),
                worlds,
                services.GetRequiredService<IWorldDbContextFactory>(),
                CancellationToken.None);
    }
}
