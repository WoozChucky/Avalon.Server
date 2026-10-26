using System.Diagnostics;
using Avalon.Common.ValueObjects;
using Avalon.Database;
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
using Microsoft.Extensions.Logging;
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

    /// <summary>The town's map template: where every character enters the world.</summary>
    public static readonly MapTemplateId TownMap = new(1);

    /// <summary>
    /// The scenarios' kill quest, in this reference data only (never in the World database's seed): see
    /// <see cref="NewKillQuest" />. Its id is far from the seeded quests' (1 to 9).
    /// </summary>
    public const uint KillQuest = 9001;

    /// <summary>The kill quest's objectives: two Thornback Boars, one Grey Fen Wolf.</summary>
    public const uint KillQuestBoars = 900101, KillQuestWolves = 900102;

    // A named in-memory database: each context opens its own connection to it, so repositories may be read from more
    // than one thread, and the keep-alive connection held below keeps it for the life of the process.
    private const string ConnectionString = "Data Source=avalon-scenario-reference;Mode=Memory;Cache=Shared";

    private static readonly Lazy<ScenarioReferenceData> s_shared = new(Load);

    private readonly SqliteConnection _keepAlive;
    private readonly ServiceProvider _repositories;
    private readonly Lazy<(MapTemplate Template, ChunkLayout Layout)> _town;

    private ScenarioReferenceData(SqliteConnection keepAlive, ServiceProvider repositories, StaticData data,
        IScriptManager scripts, IChunkLibrary chunks, ISceneryCatalog scenery, MapTemplate forestTemplate,
        ProceduralMapConfig forestConfig, TimeSpan loadTime)
    {
        _keepAlive = keepAlive;
        _repositories = repositories;
        Data = data;
        Scripts = scripts;
        Chunks = chunks;
        Scenery = scenery;
        ForestTemplate = forestTemplate;
        ForestConfig = forestConfig;
        LoadTime = loadTime;
        _town = new Lazy<(MapTemplate, ChunkLayout)>(LoadTown);
    }

    /// <summary>The reference data, loaded on first use (thread-safe).</summary>
    public static ScenarioReferenceData Shared => s_shared.Value;

    /// <summary>Every reference-data area, loaded as the World server loads it at start-up.</summary>
    public StaticData Data { get; }

    /// <summary>Every AI, ability, quest, item and aura script, by the names the seed gives them.</summary>
    public IScriptManager Scripts { get; }

    /// <summary>The chunk templates, pools and set pieces of the committed <c>Maps/</c> catalog.</summary>
    public IChunkLibrary Chunks { get; }

    /// <summary>
    /// Every map's scenery ring file under the committed <c>Maps/Scenery/</c>, read as the World server reads it at
    /// start-up (<see cref="SceneryCatalog.Load" />): the town's fixed ring and the forest's pool its layouts are ringed
    /// from.
    /// </summary>
    public ISceneryCatalog Scenery { get; }

    /// <summary>The forest's map template row.</summary>
    public MapTemplate ForestTemplate { get; }

    /// <summary>The forest's procedural config (<c>Maps/ProceduralMaps/2.json</c>), depth bands included.</summary>
    public ProceduralMapConfig ForestConfig { get; }

    /// <summary>The town's map template row. Read on first use: no scenario reads it.</summary>
    public MapTemplate TownTemplate => _town.Value.Template;

    /// <summary>
    /// The town's layout as the World server builds it: <see cref="PredefinedChunkLayoutSource" /> over the placement
    /// rows seeded from <c>Maps/TownLayouts/1.json</c>, so its four chunks under the ids and names of the catalog, its
    /// portal to the forest and its scenery ring from <see cref="Scenery" />. Built on first use (thread-safe): no
    /// scenario reads it; a scenario's town instance is <see cref="TownNavmesh.Layout" />, which leaves the portal out.
    /// </summary>
    public ChunkLayout TownLayout => _town.Value.Layout;

    /// <summary>How long the load took: the database, the catalog, the reference data and the scripts.</summary>
    public TimeSpan LoadTime { get; }

    /// <summary>The spawn tables the creature placement rolls.</summary>
    internal ISpawnTableRepository SpawnTables => _repositories.GetRequiredService<ISpawnTableRepository>();

    /// <summary>The authored creature spawns the creature placement places after the rolled ones.</summary>
    internal IMapCreatureSpawnRepository AuthoredSpawns => _repositories.GetRequiredService<IMapCreatureSpawnRepository>();

    /// <summary>The fixed doodad spawns the doodad placement places before a procedural map's plan (none in the forest).</summary>
    internal IMapDoodadSpawnRepository DoodadSpawns => _repositories.GetRequiredService<IMapDoodadSpawnRepository>();

    private (MapTemplate, ChunkLayout) LoadTown()
    {
        MapTemplate template = _repositories.GetRequiredService<IMapTemplateRepository>().FindByIdAsync(TownMap)
                                   .GetAwaiter().GetResult()
                               ?? throw new InvalidOperationException($"The seed has no map template {TownMap.Value}");
        ChunkLayout layout = PredefinedChunkLayoutSource
            .ForTesting(_repositories.GetRequiredService<IMapChunkPlacementRepository>(), Chunks, Scenery)
            .BuildAsync(template, CancellationToken.None).GetAwaiter().GetResult();
        return (template, layout);
    }

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
            db.QuestTemplates.Add(NewKillQuest());
            db.SaveChanges();
        }

        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<WorldDbContext>>(database);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddWorldRepositories();
        ServiceProvider repositories = services.BuildServiceProvider();

        var scripts = new ScriptManager(NullLoggerFactory.Instance);
        scripts.Load();

        StaticData data = NewData(repositories, scripts, repositories.GetRequiredService<IAuraTemplateRepository>());
        data.LoadAsync().GetAwaiter().GetResult();

        var chunks = new ChunkLibrary(NullLoggerFactory.Instance, repositories.GetRequiredService<IServiceScopeFactory>());
        chunks.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();

        var scenery = SceneryCatalog.Load(TownNavmesh.ContentRoot);

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

        return new ScenarioReferenceData(keepAlive, repositories, data, scripts, chunks, scenery, forestTemplate, forestConfig,
            Stopwatch.GetElapsedTime(start));
    }

    /// <summary>
    /// The scenarios' own quest (<see cref="KillQuest" />), written to the in-memory database only, never to the World
    /// database's seed: a level 1 quest of one stage, kill two Thornback Boars (creature template 4) and a Grey Fen Wolf
    /// (5), the forests' two commonest kills. Given and taken by Uriel (1), whose dialogue root the quest catalog requires
    /// of a giver and an ender, and worded with "Thinning the Herd"'s texts (quest 1); no reward, since no scenario turns
    /// it in.
    /// </summary>
    private static QuestTemplate NewKillQuest() => new()
    {
        Id = new QuestTemplateId(KillQuest),
        TitleTextId = new LocalizedTextId(25),
        DescriptionTextId = new LocalizedTextId(26),
        CompletionTextId = new LocalizedTextId(27),
        GiverCreatureId = new CreatureTemplateId(1),
        EnderCreatureId = new CreatureTemplateId(1),
        LevelRequirement = 1,
        Stages = [new QuestStage { QuestId = new QuestTemplateId(KillQuest), Sequence = 0 }],
        Objectives =
        [
            new QuestObjective
            {
                Id = KillQuestBoars, QuestId = new QuestTemplateId(KillQuest), StageSequence = 0,
                Type = QuestObjectiveType.Kill, CreatureTemplateId = new CreatureTemplateId(4), Count = 2,
                DescriptionTextId = new LocalizedTextId(28),
            },
            new QuestObjective
            {
                Id = KillQuestWolves, QuestId = new QuestTemplateId(KillQuest), StageSequence = 0,
                Type = QuestObjectiveType.Kill, CreatureTemplateId = new CreatureTemplateId(5), Count = 1,
                DescriptionTextId = new LocalizedTextId(36),
            },
        ],
    };

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

    /// <summary>
    /// A loaded StaticData of its own over this reference data's database and scripts, whose aura rows each read passes
    /// through <paramref name="auras" /> first (the read's own copies, which it may change): for a test that reloads an
    /// area, which <see cref="Data" />, shared by every scenario and test, must never see.
    /// </summary>
    public async Task<StaticData> NewDataAsync(
        Func<IReadOnlyCollection<AuraTemplate>, IReadOnlyCollection<AuraTemplate>> auras)
    {
        StaticData data = NewData(_repositories, Scripts,
            new EditedAuras(_repositories.GetRequiredService<IAuraTemplateRepository>(), auras));
        await data.LoadAsync();
        return data;
    }

    private static StaticData NewData(ServiceProvider repositories, IScriptManager scripts, IAuraTemplateRepository auras) =>
        new(
            repositories.GetRequiredService<ICharacterCreateInfoRepository>(),
            repositories.GetRequiredService<IClassLevelStatRepository>(),
            repositories.GetRequiredService<IItemTemplateRepository>(),
            repositories.GetRequiredService<IItemModifierRepository>(),
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
            auras,
            repositories.GetRequiredService<IDoodadTemplateRepository>());

    /// <summary>The repositories' contexts, each over its own connection to the named in-memory database.</summary>
    private sealed class SqliteWorldDatabase(DbContextOptions<WorldDbContext> options) : IDbContextFactory<WorldDbContext>
    {
        public WorldDbContext CreateDbContext() => new(options);
    }

    /// <summary>The database's aura rows, each whole read passed through an edit.</summary>
    private sealed class EditedAuras(
        IAuraTemplateRepository rows, Func<IReadOnlyCollection<AuraTemplate>, IReadOnlyCollection<AuraTemplate>> edit)
        : IAuraTemplateRepository
    {
        public async Task<IReadOnlyCollection<AuraTemplate>> GetAllAsync(CancellationToken cancellationToken = default) =>
            edit(await rows.GetAllAsync(cancellationToken));

        public Task<PagedResult<AuraTemplate>> PaginateAsync(int page, int pageSize, CancellationToken cancellationToken = default) =>
            rows.PaginateAsync(page, pageSize, cancellationToken);

        public Task<AuraTemplate?> FindByIdAsync(AuraId id, CancellationToken cancellationToken = default) =>
            rows.FindByIdAsync(id, cancellationToken);
    }
}
