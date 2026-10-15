using System.Reflection;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Creatures;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// An instance's spatial index (#982) in seed 1's forest, as production builds it
/// (<see cref="ScenarioWorld.NewForestInstance" />): what keeping it current adds to a position write, and what one
/// view's decision costs over it against a scan of every member (<c>MapInstance.DecideView</c>, bound by name, in a
/// second forest of the same seed that scans, <c>MapInstance.DecideViewsByFullScan</c>).
/// </summary>
/// <remarks>
/// <para>
/// The writes alternate between two positions, 1 m apart within one cell, or 30 m apart in two cells, for a forest
/// creature and for a client's character; a creature in no instance is the setter without an index. Per write.
/// </para>
/// <para>
/// A view is decided for a client at the forest's entry, where few creatures stand, and for one at the creature nearest
/// the middle of the layout; nothing changes between decisions, so each is the steady decision of a broadcast tick.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class SpatialIndexBenchmarks
{
    private const int Writes = 64;

    private ScenarioWorld _world = null!;
    private Creature _member = null!;
    private Creature _outside = null!;
    private CharacterEntity _character = null!;
    private Vector3 _here;
    private Vector3 _nearby;
    private Vector3 _nextCell;

    private CharacterEntity _entryOverIndex = null!;
    private CharacterEntity _middleOverIndex = null!;
    private CharacterEntity _entryByScan = null!;
    private CharacterEntity _middleByScan = null!;
    private Action<CharacterEntity, List<IWorldObject>> _decideOverIndex = null!;
    private Action<CharacterEntity, List<IWorldObject>> _decideByScan = null!;
    private readonly List<IWorldObject> _noObjects = [];

    [GlobalSetup]
    public void Setup()
    {
        _world = ScenarioWorld.CreateWithReferenceData(seed: 1);
        MapInstance indexed = _world.NewForestInstance(1);
        MapInstance scanned = _world.NewForestInstance(1);
        typeof(MapInstance).GetMethod("DecideViewsByFullScan", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(scanned, null);

        // A cell's centre, a metre from it in the same cell, and the next cell's centre.
        _here = new Vector3(indexed.Layout.EntryChunk.WorldPos.x + 15f, 0f, indexed.Layout.EntryChunk.WorldPos.z + 15f);
        _nearby = _here + new Vector3(1f, 0f, 0f);
        _nextCell = _here + new Vector3(30f, 0f, 0f);
        _member = (Creature)indexed.Creatures.Values.First(c => c is Creature);
        _outside = new Creature { Guid = new ObjectGuid(ObjectType.Creature, 982_000) };
        _character = _world.NewCharacter(1);
        _world.Join(indexed, _character, _here);

        Vector3 middle = Middle(indexed);
        _entryOverIndex = Watcher(indexed, 2, indexed.EntrySpawnWorldPos);
        _middleOverIndex = Watcher(indexed, 3, middle);
        _entryByScan = Watcher(scanned, 4, scanned.EntrySpawnWorldPos);
        _middleByScan = Watcher(scanned, 5, middle);
        _decideOverIndex = DecideView(indexed);
        _decideByScan = DecideView(scanned);

        // The views' first decisions list everything in view; the measured ones find it all again.
        for (int i = 0; i < 2; i++)
        {
            EntryOverIndex();
            MiddleOverIndex();
            EntryByScan();
            MiddleByScan();
        }
    }

    [Benchmark(OperationsPerInvoke = Writes)]
    public void CreatureOutsideAnInstance()
    {
        for (int i = 0; i < Writes; i += 2)
        {
            _outside.Position = _nextCell;
            _outside.Position = _here;
        }
    }

    [Benchmark(OperationsPerInvoke = Writes)]
    public void CreatureWithinACell()
    {
        for (int i = 0; i < Writes; i += 2)
        {
            _member.Position = _nearby;
            _member.Position = _here;
        }
    }

    [Benchmark(OperationsPerInvoke = Writes)]
    public void CreatureAcrossCells()
    {
        for (int i = 0; i < Writes; i += 2)
        {
            _member.Position = _nextCell;
            _member.Position = _here;
        }
    }

    [Benchmark(OperationsPerInvoke = Writes)]
    public void CharacterWithinACell()
    {
        for (int i = 0; i < Writes; i += 2)
        {
            _character.Position = _nearby;
            _character.Position = _here;
        }
    }

    [Benchmark(OperationsPerInvoke = Writes)]
    public void CharacterAcrossCells()
    {
        for (int i = 0; i < Writes; i += 2)
        {
            _character.Position = _nextCell;
            _character.Position = _here;
        }
    }

    [Benchmark]
    public void EntryOverIndex() => _decideOverIndex(_entryOverIndex, _noObjects);

    [Benchmark]
    public void EntryByScan() => _decideByScan(_entryByScan, _noObjects);

    [Benchmark]
    public void MiddleOverIndex() => _decideOverIndex(_middleOverIndex, _noObjects);

    [Benchmark]
    public void MiddleByScan() => _decideByScan(_middleByScan, _noObjects);

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();

    private CharacterEntity Watcher(MapInstance map, uint id, Vector3 at)
    {
        CharacterEntity watcher = _world.NewCharacter(id);
        _world.Join(map, watcher, at);
        return watcher;
    }

    private static Action<CharacterEntity, List<IWorldObject>> DecideView(MapInstance map) =>
        typeof(MapInstance).GetMethod("DecideView", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<CharacterEntity, List<IWorldObject>>>(map);

    // The creature nearest the middle of the layout's chunks.
    private static Vector3 Middle(MapInstance map)
    {
        float x = map.Layout.Chunks.Average(c => c.WorldPos.x) + 15f;
        float z = map.Layout.Chunks.Average(c => c.WorldPos.z) + 15f;
        ICreature nearest = map.Creatures.Values.MinBy(c => (c.Position.x - x) * (c.Position.x - x) + (c.Position.z - z) * (c.Position.z - z))!;
        return nearest.Position;
    }
}
