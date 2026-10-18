using System.Reflection;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Doodads;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// Real interest radii: nearby objects, mostly out-of-range objects, tracked objects in the margin, and every object
/// out of range (the cost of a check that finds nothing, what most of a forest's checks are). <see cref="Update" /> scans
/// every creature (<c>CharacterCharacterGameState.Update</c>); <see cref="Indexed" /> decides the same view of the same
/// creatures in an instance, over its spatial index (#982: <c>MapInstance.DecideView</c>, bound by name), whose layout
/// covers every position used, 30 m cells from 0 to 1,020 m on X and Z. The indexed view also holds the watcher itself.
/// With <see cref="Doodads" /> 20, the instance also holds 20 doodads without rules and 20 behind a quest gate the
/// watcher meets, all 15 m from it, so the 0 and 20 rows compare the indexed view's cost with and without doodads in
/// view (the full scan lists no doodads either way): the ungated ones listed by distance alone, the gated ones by the
/// answer the view holds, steady, as in a town or a forest clearing.
/// </summary>
[MemoryDiagnoser]
public class InterestVisibilityBenchmarks
{
    private const int Creatures = 200;
    private const int Dirty = 20;

    [Params("dense", "sparse", "margin", "far")]
    public string Layout { get; set; } = "dense";

    /// <summary>How many doodads without rules the instance holds, and as many behind a quest gate: 0 or 20.</summary>
    [Params(0, 20)]
    public int Doodads { get; set; }

    private CharacterCharacterGameState _state = null!;
    private Dictionary<ObjectGuid, ICreature> _creatures = null!;
    private readonly Dictionary<ObjectGuid, ICharacter> _characters = [];
    private readonly List<IWorldObject> _worldObjects = [];
    private readonly Dictionary<ObjectGuid, GameEntityFields> _dirty = [];
    private static readonly ObjectGuid s_watcher = new(ObjectType.Character, 1);
    private static readonly InterestRange s_range = new(60f, 10f);

    private ScenarioWorld _world = null!;
    private CharacterEntity _watcher = null!;
    private Action<CharacterEntity, List<IWorldObject>> _decideView = null!;

    [GlobalSetup]
    public void Setup()
    {
        _state = new CharacterCharacterGameState();
        _creatures = EntityTrackingBenchmarks.BuildCreatures(Creatures);
        Dictionary<ObjectGuid, ICreature> indexed = EntityTrackingBenchmarks.BuildCreatures(Creatures);
        SetUpInstance(indexed);
        _dirty.Clear();
        Place(_creatures);
        Place(indexed);
        Update();
        Indexed();
        if (Layout == "margin")
        {
            foreach (ICreature creature in _creatures.Values.Concat(indexed.Values))
                creature.Position = new Vector3(65f, 0, 0);
        }
        Update();
        Indexed();
        RequireStable(_state);
        RequireStable(_watcher.CharacterGameState);
    }

    [Benchmark]
    public int Update()
    {
        _state.Update(s_watcher, Vector3.zero, s_range, _creatures, _characters, _worldObjects, _dirty);
        return _state.UpdatedObjects.Count;
    }

    [Benchmark]
    public int Indexed()
    {
        _decideView(_watcher, _worldObjects);
        return _watcher.CharacterGameState.UpdatedObjects.Count;
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();

    private void SetUpInstance(Dictionary<ObjectGuid, ICreature> creatures)
    {
        _world = new ScenarioWorld();
        PlacedChunk first = new(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero);
        PlacedChunk last = new(new ChunkTemplateId(1), 33, 33, 0, new Vector3(990f, 0f, 990f));
        MapInstance map = _world.AddInstance(layout: new ChunkLayout(0, [first, last], first, null, [], Vector3.zero, 30f));
        foreach (ICreature creature in creatures.Values)
            map.AddCreature(creature);

        AddDoodads(map);
        _watcher = _world.NewCharacter(1);
        _world.Join(map, _watcher, Vector3.zero);

        // The instance's own view decision and its dirty fields, private, so bound by name; the same 20 creatures dirty.
        _decideView = typeof(MapInstance).GetMethod("DecideView", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<CharacterEntity, List<IWorldObject>>>(map);
        var instanceDirty = (Dictionary<ObjectGuid, GameEntityFields>)typeof(MapInstance)
            .GetField("_frameDirtyFields", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(map)!;
        foreach (ICreature creature in creatures.Values.Take(Dirty))
            instanceDirty.Add(creature.Guid, GameEntityFields.Position);
    }

    /// <summary>
    /// <see cref="Doodads" /> ungated doodads and as many gated ones, alternating on a ring 15 m round the watcher, placed
    /// as an instance build places them. The gate is NotStarted on a quest the watcher never takes, so it sees them all.
    /// </summary>
    private void AddDoodads(MapInstance map)
    {
        DoodadTemplateView plain = DoodadTemplate(1, visible: null);
        DoodadTemplateView gated = DoodadTemplate(2, visible: new DoodadGate(1, DoodadGateCondition.NotStarted, null));
        int count = Doodads * 2;
        for (int i = 0; i < count; i++)
        {
            float angle = i * MathF.Tau / count;
            map.AddDoodad(new Doodad(new ObjectGuid(ObjectType.Doodad, (uint)(1 + i)), i % 2 == 0 ? plain : gated,
                new Vector3(15f * MathF.Cos(angle), 0f, 15f * MathF.Sin(angle)), 0f));
        }
    }

    private static DoodadTemplateView DoodadTemplate(uint id, DoodadGate? visible) => new(
        new DoodadTemplateId(id), "Bench crate", "props-kit", "Crate", null, null, null, null,
        DoodadInteraction.Click, 0, 5f, 0f, DoodadUseLimit.OncePerInstance, DoodadSpentLook.ShowUsed, null, null,
        null, 0, 0, 0, DoodadCollision.None, 0f, 0f, 0f, visible, null, [], 0, ScriptCanSee: false);

    private void Place(Dictionary<ObjectGuid, ICreature> creatures)
    {
        int i = 0;
        foreach (ICreature creature in creatures.Values)
        {
            creature.Position = Layout switch
            {
                "sparse" => new Vector3(i * 5f, 0, i * 5f),
                "far" => new Vector3(100f + i, 0, 0),
                _ => new Vector3(i % 10, 0, i / 10)
            };
            if (i < Dirty && ReferenceEquals(creatures, _creatures))
                _dirty.Add(creature.Guid, GameEntityFields.Position);
            i++;
        }
    }

    private void RequireStable(CharacterCharacterGameState state)
    {
        if (state.NewObjects.Count != 0 || state.RemovedObjects.Count != 0 || (state.UpdatedObjects.Count == 0) != (Layout == "far"))
            throw new InvalidOperationException("Expected stable visibility with dirty updates (none when far).");
    }
}
