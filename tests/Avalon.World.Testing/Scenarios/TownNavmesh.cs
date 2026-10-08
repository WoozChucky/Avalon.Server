using System.Diagnostics;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using DotRecast.Detour;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.World.Testing.Scenarios;

/// <summary>
/// The town's navmesh, baked once per process from the chunk objs the World server ships
/// (<c>src/Server/Avalon.Server.World/Maps/Chunks</c>) in the layout of <c>Maps/TownLayouts/1.json</c>, and shared by
/// every scenario instance: a baked navmesh is only read once built, and each instance queries it through its own
/// navigator. The bake is setup cost, never part of a measured tick.
/// </summary>
public static class TownNavmesh
{
    private static readonly Lazy<string> s_contentRoot = new(LocateContentRoot);
    private static readonly Lazy<DtNavMesh> s_shared = new(Bake);

    /// <summary>The navmesh, baked on first use (thread-safe).</summary>
    public static DtNavMesh Shared => s_shared.Value;

    /// <summary>
    /// The repository's <c>Maps</c> directory (<c>src/Server/Avalon.Server.World/Maps</c>), found by walking up from
    /// the test's base directory to the directory holding <c>Avalon.sln</c>. Read in place: nothing changes the
    /// process directory.
    /// </summary>
    public static string ContentRoot => s_contentRoot.Value;

    /// <summary>How long the bake took, once it has run; zero before.</summary>
    public static TimeSpan BakeTime { get; private set; }

    /// <summary>Where a player enters the town: the middle of the entry chunk (1.json's entry spawn, local 15, 0, 15).</summary>
    public static Vector3 EntrySpawn => new(15f, 0f, 15f);

    /// <summary>
    /// The centre of the walking scenarios' loop: the open square of the south-east chunk, where a
    /// <see cref="LoopWalker.Radius" /> circle stays a metre clear of every wall (the only such centre on a half-metre
    /// scan of the town), so no walker is ever pressed into one. Around the entry spawn the circle would cross a
    /// building and a wall, where a walker stops for good.
    /// </summary>
    public static Vector3 LoopCentre => new(45f, 0f, 10f);

    /// <summary>
    /// <c>Maps/TownLayouts/1.json</c> as <c>PredefinedChunkLayoutSource</c> builds it: four chunks at rotation 0 on a
    /// 30 m grid, the south-west one the entry. The town's forward portal is left out: no scenario walks through it.
    /// </summary>
    public static ChunkLayout Layout()
    {
        PlacedChunk sw = new(new ChunkTemplateId(1), 0, 0, 0, new Vector3(0, 0, 0));
        PlacedChunk se = new(new ChunkTemplateId(2), 1, 0, 0, new Vector3(30, 0, 0));
        PlacedChunk nw = new(new ChunkTemplateId(3), 0, 1, 0, new Vector3(0, 0, 30));
        PlacedChunk ne = new(new ChunkTemplateId(4), 1, 1, 0, new Vector3(30, 0, 30));
        return new ChunkLayout(0, [sw, se, nw, ne], sw, null, [], EntrySpawn, 30f, null);
    }

    private static string LocateContentRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Avalon.sln")))
            dir = Path.GetDirectoryName(dir);

        if (dir is null)
            throw new DirectoryNotFoundException($"No directory above {AppContext.BaseDirectory} holds Avalon.sln");

        string maps = Path.Combine(dir, "src", "Server", "Avalon.Server.World", "Maps");
        if (!Directory.Exists(Path.Combine(maps, "Chunks")))
            throw new DirectoryNotFoundException($"The town's chunk objs are not under {maps}");

        return maps;
    }

    private static DtNavMesh Bake()
    {
        // The builder reads <content root>/Maps/Chunks/<name>.obj, so it is handed the directory holding Maps.
        string serverContentRoot = Path.GetDirectoryName(ContentRoot)!;
        var builder = ChunkLayoutNavmeshBuilder.ForTesting(NullLoggerFactory.Instance, new TownLibrary(), serverContentRoot);

        long start = Stopwatch.GetTimestamp();
        DtNavMesh navMesh = builder.BuildAsync(Layout(), CancellationToken.None).GetAwaiter().GetResult();
        BakeTime = Stopwatch.GetElapsedTime(start);
        return navMesh;
    }

    /// <summary>The town's four chunks by the ids <see cref="Layout" /> places them under; the bake reads only their names.</summary>
    private sealed class TownLibrary : IChunkLibrary
    {
        private static readonly Dictionary<int, string> s_names = new()
        {
            [1] = "town_sw_01",
            [2] = "town_se_01",
            [3] = "town_nw_01",
            [4] = "town_ne_01",
        };

        public Task LoadAsync(CancellationToken ct) => Task.CompletedTask;

        public ChunkTemplate GetById(ChunkTemplateId id) =>
            new() { Id = id, Name = s_names[id.Value], CellSize = 30f };

        public IReadOnlyList<ChunkPoolMember> GetByPool(ChunkPoolId poolId) => [];

        public IReadOnlyList<ChunkGroupDefinition> GetGroupsByPool(ChunkPoolId poolId) => [];

        public IReadOnlyDictionary<ChunkTemplateId, ChunkTemplate> LookupByIds(IEnumerable<ChunkTemplateId> ids) =>
            ids.ToDictionary(id => id, GetById);
    }
}
