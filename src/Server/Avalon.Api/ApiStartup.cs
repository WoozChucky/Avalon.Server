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
    /// </summary>
    public static async Task ValidateAndMigrateAsync(IServiceProvider services, ILogger logger)
    {
        services.GetRequiredService<IStartupValidator>().Validate();

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await using AuthDbContext authDb = await scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<AuthDbContext>>().CreateDbContextAsync(CancellationToken.None);
        await using CharacterDbContext characterDb = await scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<CharacterDbContext>>().CreateDbContextAsync(CancellationToken.None);
        await using WorldDbContext worldDb = await scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<WorldDbContext>>().CreateDbContextAsync(CancellationToken.None);
        logger.LogInformation("Migrating database if necessary...");
        // Startup migration — host lifetime not active yet, so CancellationToken.None is intentional.
        await authDb.Database.MigrateAsync(CancellationToken.None);
        await characterDb.Database.MigrateAsync(CancellationToken.None);
        await worldDb.Database.MigrateAsync(CancellationToken.None);
    }
}
