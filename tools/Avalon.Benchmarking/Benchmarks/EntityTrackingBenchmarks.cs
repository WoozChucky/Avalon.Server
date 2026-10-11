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

/// <summary>Visibility filtering and tracking with stable membership and prebuilt frame dirty maps.</summary>
[MemoryDiagnoser]
public class EntityTrackingBenchmarks
{
    [Params(30, 200)]
    public int CreatureCount { get; set; }

    [Params(0, 10, 100)]
    public int DirtyPercent { get; set; }

    private CharacterCharacterGameState _gameState = null!;
    private Dictionary<ObjectGuid, ICreature> _creatures = null!;
    private readonly Dictionary<ObjectGuid, ICharacter> _characters = [];
    private readonly List<IWorldObject> _worldObjects = [];
    private readonly Dictionary<ObjectGuid, GameEntityFields> _dirty = [];
    private static readonly ObjectGuid s_watcher = new(ObjectType.Character, 1);
    private static readonly InterestRange s_seeEverything = new(float.MaxValue, 0f);

    [GlobalSetup]
    public void Setup()
    {
        _gameState = new CharacterCharacterGameState();
        _creatures = BuildCreatures(CreatureCount);
        _dirty.Clear();
        int active = CreatureCount * DirtyPercent / 100;
        foreach (ObjectGuid guid in _creatures.Keys)
        {
            if (_dirty.Count >= active)
                break;
            _dirty.Add(guid, GameEntityFields.CreatureUpdate);
        }

        // Prime membership and notification-list capacity outside the measured operation.
        Update();
        Update();
        if (_gameState.UpdatedObjects.Count != active || _gameState.NewObjects.Count != 0)
            throw new InvalidOperationException("Tracking benchmark did not exercise its dirty map.");
    }

    [Benchmark]
    public int Update()
    {
        _gameState.Update(s_watcher, Vector3.zero, s_seeEverything, _creatures, _characters, _worldObjects, _dirty);
        return _gameState.UpdatedObjects.Count;
    }

    internal static Dictionary<ObjectGuid, ICreature> BuildCreatures(int count, uint firstGuid = 1)
    {
        var creatures = new Dictionary<ObjectGuid, ICreature>(count);
        for (uint i = 0; i < (uint)count; i++)
        {
            var creature = new Creature
            {
                Guid = new ObjectGuid(ObjectType.Creature, firstGuid + i),
                Name = "Benchmark creature",
                Health = 1000,
                CurrentHealth = 1000,
                Position = new Vector3(i, 0, i),
            };
            creatures.Add(creature.Guid, creature);
        }
        return creatures;
    }
}

/// <summary>Alternating prebuilt views exercise removals and additions without allocating inputs per update.</summary>
[MemoryDiagnoser]
public class EntityTrackingChurnBenchmarks
{
    [Params(30, 200)]
    public int CreatureCount { get; set; }

    [Params(1, 100)]
    public int ReplacedPercent { get; set; }

    private CharacterCharacterGameState _gameState = null!;
    private Dictionary<ObjectGuid, ICreature> _first = null!;
    private Dictionary<ObjectGuid, ICreature> _second = null!;
    private readonly Dictionary<ObjectGuid, ICharacter> _characters = [];
    private readonly List<IWorldObject> _worldObjects = [];
    private readonly Dictionary<ObjectGuid, GameEntityFields> _dirty = [];
    private static readonly ObjectGuid s_watcher = new(ObjectType.Character, 1);
    private static readonly InterestRange s_seeEverything = new(float.MaxValue, 0f);
    private bool _useSecond;

    [GlobalSetup]
    public void Setup()
    {
        _gameState = new CharacterCharacterGameState();
        _first = EntityTrackingBenchmarks.BuildCreatures(CreatureCount);
        _second = EntityTrackingBenchmarks.BuildCreatures(CreatureCount, (uint)CreatureCount + 1);
        int replaced = Math.Max(1, CreatureCount * ReplacedPercent / 100);
        int index = 0;
        foreach (ICreature creature in _first.Values)
        {
            if (index >= replaced)
            {
                _second.Remove(new ObjectGuid(ObjectType.Creature, (uint)(CreatureCount + index + 1)));
                _second.Add(creature.Guid, creature);
            }
            index++;
        }
        _useSecond = false;
        for (int i = 0; i < 8; i++)
            Update();
        if (_gameState.NewObjects.Count != replaced || _gameState.RemovedObjects.Count != replaced)
            throw new InvalidOperationException("Tracking benchmark did not exercise membership changes.");
    }

    [Benchmark]
    public int Update()
    {
        _useSecond = !_useSecond;
        _gameState.Update(s_watcher, Vector3.zero, s_seeEverything, _useSecond ? _second : _first,
            _characters, _worldObjects, _dirty);
        return _gameState.NewObjects.Count + _gameState.RemovedObjects.Count;
    }
}
