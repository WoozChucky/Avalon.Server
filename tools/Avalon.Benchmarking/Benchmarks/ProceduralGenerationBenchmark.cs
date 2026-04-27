using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Benchmarking.Benchmarks;

[MemoryDiagnoser]
public class ProceduralGenerationBenchmark
{
    private ProceduralChunkLayoutSource _gen = default!;
    private IReadOnlyList<ChunkPoolMember> _pool = default!;
    private ProceduralMapConfig _cfg = default!;

    [Params(10, 20, 50)] public int PoolSize;
    [Params(5, 10, 15)]  public int PathLen;

    [GlobalSetup]
    public void Setup()
    {
        // Library + scope factory are only used by BuildAsync (not exercised here).
        // The benchmark calls Generate(config, pool, seed) directly, so passing
        // null! is safe and avoids adding an NSubstitute dep to the bench project.
        _gen = new ProceduralChunkLayoutSource(NullLoggerFactory.Instance, library: null!, scopeFactory: null!);
        _pool = BuildTestPool(PoolSize);
        _cfg = new ProceduralMapConfig
        {
            MapTemplateId = new MapTemplateId(1),
            ChunkPoolId   = new ChunkPoolId(1),
            SpawnTableId  = new SpawnTableId(1),
            MainPathMin = (ushort)PathLen,
            MainPathMax = (ushort)PathLen,
            BranchChance = 0,
            BranchMaxDepth = 0,
            HasBoss = false,
            BackPortalTargetMapId = 1,
            ForwardPortalTargetMapId = null,
        };
    }

    [Benchmark]
    public ChunkLayout Generate() => _gen.Generate(_cfg, _pool, seed: 1);

    private static IReadOnlyList<ChunkPoolMember> BuildTestPool(int size)
    {
        var list = new List<ChunkPoolMember>(capacity: size);

        // Entry chunk: N-center exit, Spawn_Entry + Portal_Back.
        var entry = MakeChunk(1, entryTag: true, exits: 0b_0000_0000_0000_0010, back: true);
        list.Add(new ChunkPoolMember(entry, Weight: 1f));

        // Fill rest with N+S center through-corridors.
        for (int i = 2; i <= size; i++)
            list.Add(new ChunkPoolMember(
                MakeChunk(i, entryTag: false, exits: 0b_0000_0000_1000_0010, back: false),
                Weight: 1f));

        return list;
    }

    private static ChunkTemplate MakeChunk(int id, bool entryTag, ushort exits, bool back)
    {
        var t = new ChunkTemplate
        {
            Id = new ChunkTemplateId(id),
            Name = $"c{id}",
            AssetKey = $"Chunks/c{id}",
            GeometryFile = $"Chunks/c{id}.obj",
            CellSize = 30f,
            Exits = exits,
        };
        if (entryTag)
            t.SpawnSlots.Add(new ChunkSpawnSlot { Tag = "entry", LocalX = 5, LocalY = 0, LocalZ = 5 });
        if (back)
            t.PortalSlots.Add(new ChunkPortalSlot { Role = PortalRole.Back, LocalX = 5, LocalY = 0, LocalZ = 5 });
        return t;
    }
}
