using Avalon.Database.Character;
using Avalon.Database.World;
using Avalon.Database.World.Seeding;
using Avalon.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.Server.World;

/// <summary>The startup work Program runs between building the host and running it.</summary>
public static class WorldStartup
{
    /// <summary>
    /// Validates the options first (#543): <c>ValidateOnStart</c> alone runs only inside
    /// <c>StartAsync</c>, after the migrations, the seeding and the cache connection below, so a
    /// missing connection string or cache host would fail with their error instead of naming the
    /// setting.
    /// </summary>
    public static async Task PrepareAsync(IHost host)
    {
        host.Services.GetRequiredService<IStartupValidator>().Validate();

        ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Program));
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        await using CharacterDbContext characterDb = await scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<CharacterDbContext>>().CreateDbContextAsync(CancellationToken.None);
        await using WorldDbContext worldDb = await scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<WorldDbContext>>().CreateDbContextAsync(CancellationToken.None);
        logger.LogInformation("Migrating database if necessary...");
        // Startup migration — host lifetime not active yet, so CancellationToken.None is intentional.
        await characterDb.Database.MigrateAsync(CancellationToken.None);
        await worldDb.Database.MigrateAsync(CancellationToken.None);

        // The chunk catalog lives in files under Maps/, not in migrations: bring the database in
        // line with them on every start so a fresh install (or a release adding chunks) has them.
        ChunkCatalogSeedResult seeded = await ChunkCatalogSeeder.SeedAsync(worldDb,
            Path.Combine(AppContext.BaseDirectory, "Maps"), CancellationToken.None);
        logger.LogInformation(
            "Chunk catalog seeded: {Added} added, {Updated} updated, {Layouts} town layouts, {Pools} pools",
            seeded.TemplatesAdded, seeded.TemplatesUpdated, seeded.LayoutsReplaced, seeded.PoolsSynced);

        IReplicatedCache cache = scope.ServiceProvider.GetRequiredService<IReplicatedCache>();
        await cache.ConnectAsync();
        host.Services.TraceRedis(cache.Connection);
    }
}
