using Avalon.Database.Character;
using Avalon.Database.World;
using Avalon.Database.World.Seeding;
using Avalon.Hosting.Networking;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Serialization;
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

        // The packet reader is built here, and with it the decoder of every packet this server reads (#897): a
        // contract that cannot be decoded stops the start, and no decoder is built on a connection's read loop. Left
        // to the container it was built by the first connection accepted. First of the two: it sets the depth limit
        // of the process's one protobuf model (PacketReader.MaxDecodeDepth), so nothing is built before the limit is.
        _ = host.Services.GetRequiredService<IPacketReader>();

        // Every server packet is prepared here, before the port opens: its scratch reset is compiled (#875) and its
        // serializer built (#883), so neither is left to the first packet of the type, which a world sends on the
        // tick. A packet that cannot be encoded stops the start.
        PacketEncoder.PrepareServerPackets();

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
            "Chunk catalog seeded: {Added} added, {Updated} updated, {Layouts} town layouts, {Pools} pools, {Groups} groups, {SpawnTables} spawn tables, {Maps} procedural maps",
            seeded.TemplatesAdded, seeded.TemplatesUpdated, seeded.LayoutsReplaced, seeded.PoolsSynced, seeded.GroupsReplaced,
            seeded.SpawnTablesSynced, seeded.ProceduralMapsSynced);

        IReplicatedCache cache = scope.ServiceProvider.GetRequiredService<IReplicatedCache>();
        await cache.ConnectAsync();
        host.Services.TraceRedis(cache.Connection);
    }
}
