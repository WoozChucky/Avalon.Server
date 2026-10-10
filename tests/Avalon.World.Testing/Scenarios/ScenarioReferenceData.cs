using System.Diagnostics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Database.World.Extensions;
using Avalon.Database.World.Repositories;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Scripts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.World.Testing.Scenarios;

/// <summary>
/// The World server's reference data, loaded once per process and shared by every scenario world built with it
/// (<see cref="ScenarioWorld.CreateWithReferenceData" />): the World database's seed, its <c>HasData</c> rows (the rows
/// the migrations write and the balance simulator reads as its seed), in an in-memory SQLite database, with the chunk
/// catalog under <c>Maps/</c> seeded into it by <see cref="ChunkCatalogSeeder" /> as the World server seeds it at
/// start-up. <see cref="StaticData" />, the <see cref="ChunkLibrary" /> and the creature placement read it through the
/// production repositories, and the <see cref="ScriptManager" /> is loaded, so creatures get their real AI scripts and
/// casts their real ability scripts. Setup cost, never part of a measured tick; nothing changes it once loaded.
/// </summary>
public sealed class ScenarioReferenceData
{
    /// <summary>The forest's map template: the procedural map the town's portal leads to.</summary>
    public static readonly MapTemplateId ForestMap = new(2);

    // A named in-memory database: each context opens its own connection to it, so repositories may be read from more
    // than one thread, and the keep-alive connection held below keeps it for the life of the process.
    private const string ConnectionString = "Data Source=avalon-scenario-reference;Mode=Memory;Cache=Shared";

    private static readonly Lazy<ScenarioReferenceData> s_shared = new(Load);

    private readonly SqliteConnection _keepAlive;
    private readonly ServiceProvider _repositories;

    private ScenarioReferenceData(SqliteConnection keepAlive, ServiceProvider repositories, StaticData data,
        IScriptManager scripts, IChunkLibrary chunks, MapTemplate forestTemplate, ProceduralMapConfig forestConfig,
        TimeSpan loadTime)
    {
        _keepAlive = keepAlive;
        _repositories = repositories;
        Data = data;
        Scripts = scripts;
        Chunks = chunks;
        ForestTemplate = forestTemplate;
        ForestConfig = forestConfig;
        LoadTime = loadTime;
    }

    /// <summary>The reference data, loaded on first use (thread-safe).</summary>
    public static ScenarioReferenceData Shared => s_shared.Value;

    /// <summary>Every reference-data area, loaded as the World server loads it at start-up.</summary>
    public StaticData Data { get; }

    /// <summary>Every AI, ability, quest, item and aura script, by the names the seed gives them.</summary>
    public IScriptManager Scripts { get; }

    /// <summary>The chunk templates, pools and set pieces of the committed <c>Maps/</c> catalog.</summary>
    public IChunkLibrary Chunks { get; }

    /// <summary>The forest's map template row.</summary>
    public MapTemplate ForestTemplate { get; }

    /// <summary>The forest's procedural config (<c>Maps/ProceduralMaps/2.json</c>), depth bands included.</summary>
    public ProceduralMapConfig ForestConfig { get; }

    /// <summary>How long the load took: the database, the catalog, the reference data and the scripts.</summary>
    public TimeSpan LoadTime { get; }

    /// <summary>The spawn tables the creature placement rolls.</summary>
    internal ISpawnTableRepository SpawnTables => _repositories.GetRequiredService<ISpawnTableRepository>();

    /// <summary>The authored creature spawns the creature placement places after the rolled ones.</summary>
    internal IMapCreatureSpawnRepository AuthoredSpawns => _repositories.GetRequiredService<IMapCreatureSpawnRepository>();

    private static ScenarioReferenceData Load()
    {
        long start = Stopwatch.GetTimestamp();

        var keepAlive = new SqliteConnection(ConnectionString);
        keepAlive.Open();

        var database = new SqliteWorldDatabase(
            new DbContextOptionsBuilder<WorldDbContext>().UseSqlite(ConnectionString).Options);
        using (WorldDbContext db = database.CreateDbContext())
        {
            db.Database.EnsureCreated();
            ChunkCatalogSeeder.SeedAsync(db, TownNavmesh.ContentRoot).GetAwaiter().GetResult();
        }

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<WorldDbContext>>(database);
        services.AddWorldRepositories();
        ServiceProvider repositories = services.BuildServiceProvider();

        var scripts = new ScriptManager(NullLoggerFactory.Instance);
        scripts.Load();

        var data = new StaticData(
            repositories.GetRequiredService<ICharacterCreateInfoRepository>(),
            repositories.GetRequiredService<IClassLevelStatRepository>(),
            repositories.GetRequiredService<IItemTemplateRepository>(),
            repositories.GetRequiredService<IAbilityTemplateRepository>(),
            repositories.GetRequiredService<ICharacterLevelExperienceRepository>(),
            repositories.GetRequiredService<ICreatureTemplateRepository>(),
            repositories.GetRequiredService<ICreatureBaseStatRepository>(),
            repositories.GetRequiredService<ICreatureRarityModifierRepository>(),
            repositories.GetRequiredService<ILocalizedTextRepository>(),
            repositories.GetRequiredService<IDialogueRepository>(),
            repositories.GetRequiredService<ILootTableRepository>(),
            NullLoggerFactory.Instance,
            repositories.GetRequiredService<IVendorStockRepository>(),
            repositories.GetRequiredService<ICombatDataRepository>(),
            repositories.GetRequiredService<IQuestRepository>(),
            scripts,
            repositories.GetRequiredService<IAuraTemplateRepository>());
        data.LoadAsync().GetAwaiter().GetResult();

        var chunks = new ChunkLibrary(NullLoggerFactory.Instance, repositories.GetRequiredService<IServiceScopeFactory>());
        chunks.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();

        MapTemplate forestTemplate = repositories.GetRequiredService<IMapTemplateRepository>().FindByIdAsync(ForestMap)
                                         .GetAwaiter().GetResult()
                                     ?? throw new InvalidOperationException($"The seed has no map template {ForestMap.Value}");
        ProceduralMapConfig forestConfig = repositories.GetRequiredService<IProceduralMapConfigRepository>()
                                               .FindByTemplateIdAsync(ForestMap).GetAwaiter().GetResult()
                                           ?? throw new InvalidOperationException($"Map {ForestMap.Value} has no procedural config");

        // The one random draw on the forest's build path that no seed reaches: a creature placed without a level has it
        // drawn by CreatureSpawner.RollLevel from Random.Shared. An authored spawn is always placed so, and a rolled one
        // when its depth falls in no band (DepthBandLevels.For answers null). The forest has no authored spawn and bands
        // that cover every depth from 1 on, so its creatures are the seeded roll's alone; refuse, rather than measure a
        // forest whose levels change from run to run. ForestLayouts checks each layout's own depths too.
        if (BandProblem(forestConfig.DepthBands) is { } problem)
        {
            throw new InvalidOperationException(
                $"Map {ForestMap.Value}'s depth bands {problem}: a creature at a depth no band covers has its level drawn " +
                "from Random.Shared by CreatureSpawner.RollLevel, and a forest scenario would no longer be deterministic.");
        }

        int authored = repositories.GetRequiredService<IMapCreatureSpawnRepository>().FindByMapAsync(ForestMap)
            .GetAwaiter().GetResult().Count;
        if (authored > 0)
        {
            throw new InvalidOperationException(
                $"Map {ForestMap.Value} has {authored} authored creature spawns, whose levels CreatureSpawner.RollLevel " +
                "draws from Random.Shared: a forest scenario would no longer be deterministic. Seed that roll first.");
        }

        return new ScenarioReferenceData(keepAlive, repositories, data, scripts, chunks, forestTemplate, forestConfig,
            Stopwatch.GetElapsedTime(start));
    }

    /// <summary>
    /// Why the bands leave a depth from 1 on to no band, or null: none at all, a first band from another depth than 1, a
    /// gap or an overlap between two, or a last band with an end.
    /// </summary>
    private static string? BandProblem(IReadOnlyList<ProceduralDepthBand> bands)
    {
        if (bands.Count == 0)
            return "are empty";

        var ordered = bands.OrderBy(b => b.MinDepth).ToList();
        if (ordered[0].MinDepth != 1)
            return $"start at depth {ordered[0].MinDepth}, not 1";

        for (int i = 0; i + 1 < ordered.Count; i++)
        {
            if (ordered[i].MaxDepth is not { } end || end + 1 != ordered[i + 1].MinDepth)
                return $"do not meet between the band from depth {ordered[i].MinDepth} and the one from {ordered[i + 1].MinDepth}";
        }

        return ordered[^1].MaxDepth is { } last ? $"end at depth {last}" : null;
    }

    /// <summary>The repositories' contexts, each over its own connection to the named in-memory database.</summary>
    private sealed class SqliteWorldDatabase(DbContextOptions<WorldDbContext> options) : IDbContextFactory<WorldDbContext>
    {
        public WorldDbContext CreateDbContext() => new(options);
    }
}
