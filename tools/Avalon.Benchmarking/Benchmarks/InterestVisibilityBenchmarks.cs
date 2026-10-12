using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>Real interest radii: nearby objects, mostly out-of-range objects, and tracked objects in the margin.</summary>
[MemoryDiagnoser]
public class InterestVisibilityBenchmarks
{
    [Params("dense", "sparse", "margin")]
    public string Layout { get; set; } = "dense";

    private CharacterCharacterGameState _state = null!;
    private Dictionary<ObjectGuid, ICreature> _creatures = null!;
    private readonly Dictionary<ObjectGuid, ICharacter> _characters = [];
    private readonly List<IWorldObject> _worldObjects = [];
    private readonly Dictionary<ObjectGuid, GameEntityFields> _dirty = [];
    private static readonly ObjectGuid s_watcher = new(ObjectType.Character, 1);
    private static readonly InterestRange s_range = new(60f, 10f);

    [GlobalSetup]
    public void Setup()
    {
        _state = new CharacterCharacterGameState();
        _creatures = EntityTrackingBenchmarks.BuildCreatures(200);
        _dirty.Clear();
        int i = 0;
        foreach (ICreature creature in _creatures.Values)
        {
            creature.Position = Layout == "sparse" ? new Vector3(i * 5f, 0, i * 5f) : new Vector3(i % 10, 0, i / 10);
            if (i < 20)
                _dirty.Add(creature.Guid, GameEntityFields.Position);
            i++;
        }
        Update();
        if (Layout == "margin")
        {
            foreach (ICreature creature in _creatures.Values)
                creature.Position = new Vector3(65f, 0, 0);
        }
        Update();
        if (_state.NewObjects.Count != 0 || _state.RemovedObjects.Count != 0 || _state.UpdatedObjects.Count == 0)
            throw new InvalidOperationException("Expected stable visibility with dirty updates.");
    }

    [Benchmark]
    public int Update()
    {
        _state.Update(s_watcher, Vector3.zero, s_range, _creatures, _characters, _worldObjects, _dirty);
        return _state.UpdatedObjects.Count;
    }
}
