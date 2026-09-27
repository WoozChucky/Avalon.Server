using Avalon.Database.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Login;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Avalon.Server.Auth;

/// <summary>The startup work Program runs between building the host and running it.</summary>
public static class AuthStartup
{
    /// <summary>
    /// Validates the options first (#543): <c>ValidateOnStart</c> alone runs only inside
    /// <c>StartAsync</c>, after the migration and the cache connection below, so a missing
    /// connection string or cache host would fail with their error instead of naming the setting.
    /// </summary>
    public static async Task PrepareAsync(IHost host)
    {
        host.Services.GetRequiredService<IStartupValidator>().Validate();

        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        await using AuthDbContext db = await scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<AuthDbContext>>().CreateDbContextAsync(CancellationToken.None);
        ILogger<Program> logger = host.Services.GetRequiredService<ILogger<Program>>();
        // For comparison with the API's line: both count on the same Redis budgets (#478 review).
        LoginLimitsValidation.LogAtStartup(logger, host.Services.GetRequiredService<ILoginLimits>(), "Application");
        logger.LogInformation("Migrating database if necessary...");
        // Startup migration — host lifetime not active yet, so CancellationToken.None is intentional.
        await db.Database.MigrateAsync(CancellationToken.None);

        IReplicatedCache cache = scope.ServiceProvider.GetRequiredService<IReplicatedCache>();
        await cache.ConnectAsync();
        host.Services.TraceRedis(cache.Connection);
    }
}
