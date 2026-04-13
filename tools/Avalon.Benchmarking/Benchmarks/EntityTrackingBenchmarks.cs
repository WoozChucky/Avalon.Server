using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// Benchmarks the current entity tracking system (full snapshot comparison per tick).
/// These are the BEFORE numbers; re-run after the dirty-flag redesign to compare.
///
/// Scenarios:
///   AllIdle          — no entity changes between updates (the common case at 250 instances)
///   TenPercentActive — ~10% of creatures change each tick (realistic mid-combat)
///   AllActive        — every creature changes every tick (stress test)
///
/// Scale parameter: CreatureCount mirrors realistic instance density (50–200).
/// </summary>
[MemoryDiagnoser]
public class EntityTrackingBenchmarks
{
    [Params(50, 100, 200)]
    public int CreatureCount { get; set; }

    private CharacterCharacterGameState _gameState = null!;
    private Dictionary<ObjectGuid, ICreature> _creatures = null!;
    private Dictionary<ObjectGuid, ICharacter> _characters = null!;
    private List<IWorldObject> _worldObjects = null!;

    // Incremented each benchmark iteration so position changes are always novel
    private uint _tick;

    [GlobalSetup]
    public void Setup()
    {
        _tick = 0;
        _gameState = new CharacterCharacterGameState();
        _creatures = BuildCreatures(CreatureCount);
        _characters = [];
        _worldObjects = [];

        // Prime the tracking state: first Update marks all creatures as "known".
        // Subsequent calls measure steady-state cost, not enter-visibility cost.
        _gameState.Update(_creatures, _characters, _worldObjects);
    }

    /// <summary>
    /// Baseline: all creatures idle, nothing changes between ticks.
    /// Measures the pure snapshot-comparison overhead for unchanged entities.
    /// After the redesign this should approach zero for idle entities.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void Update_AllIdle()
    {
        _gameState.Update(_creatures, _characters, _worldObjects);
    }

    /// <summary>
    /// 10% of creatures change position each tick — realistic mid-instance scenario
    /// where some creatures are in combat while most are idle or dead.
    /// </summary>
    [Benchmark]
    public void Update_TenPercentActive()
    {
        _tick++;
        int active = Math.Max(1, CreatureCount / 10);
        int i = 0;
        foreach (var creature in _creatures.Values.Cast<Creature>())
        {
            if (i++ >= active) break;
            creature.Position = new Vector3(_tick, 0, _tick + i);
            creature.CurrentHealth = (uint)Math.Max(1, creature.CurrentHealth - 1);
        }
        _gameState.Update(_creatures, _characters, _worldObjects);
    }

    /// <summary>
    /// Every creature changes position and health each tick — worst-case active scenario.
    /// Useful for verifying the redesign doesn't regress on fully-dirty frames.
    /// </summary>
    [Benchmark]
    public void Update_AllActive()
    {
        _tick++;
        foreach (var creature in _creatures.Values.Cast<Creature>())
        {
            creature.Position = new Vector3(_tick, 0, _tick);
            creature.CurrentHealth = (uint)Math.Max(1, creature.CurrentHealth - 1);
        }
        _gameState.Update(_creatures, _characters, _worldObjects);
    }

    private static Dictionary<ObjectGuid, ICreature> BuildCreatures(int count)
    {
        var dict = new Dictionary<ObjectGuid, ICreature>(count);
        for (uint i = 1; i <= (uint)count; i++)
        {
            var creature = new Creature
            {
                Guid = new ObjectGuid(ObjectType.Creature, i),
                Name = $"Creature_{i}",
                Level = 1,
                Health = 1000,
                CurrentHealth = 1000,
                PowerType = PowerType.Mana,
                Power = 100,
                CurrentPower = 100,
                Position = new Vector3(i, 0, i),
                Velocity = Vector3.zero,
                Orientation = Vector3.zero,
                MoveState = MoveState.Idle,
            };
            dict[creature.Guid] = creature;
        }
        return dict;
    }
}
