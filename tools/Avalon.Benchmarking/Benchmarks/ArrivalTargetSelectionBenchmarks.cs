using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Auras;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public.Units;
using Avalon.World.Scripts.Creatures;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>Real creature target selection, with the fallback past a protected top-threat player (#1088).</summary>
[MemoryDiagnoser]
public class ArrivalTargetSelectionBenchmarks
{
    private ScenarioWorld _world = null!;
    private CreatureCombatScript _script = null!;

    [Params(false, true)]
    public bool ProtectedTop { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = ScenarioWorld.CreateWithReferenceData();
        MapInstance map = _world.AddInstance();
        CharacterEntity highest = _world.NewCharacter(1);
        CharacterEntity eligible = _world.NewCharacter(2);
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 1),
            Metadata = new CreatureTemplate { Id = new CreatureTemplateId(1) },
            Health = 100,
            CurrentHealth = 100,
        };
        _script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, map, _world.Clock);
        map.CombatService.EnterCombat(creature, highest);
        map.CombatService.EnterCombat(creature, eligible);
        var encounter = (Encounter)map.CombatService.GetEncounterFor(creature)!;
        encounter.AddThreat(creature, highest, 100);
        if (ProtectedTop)
            ArrivalProtection.Start(highest, map.Auras);

        // The scenario clock stands still during measurement, keeping protection active for the entire job.
        if (!ReferenceEquals(_script.PickTarget(), ProtectedTop ? eligible : highest))
            throw new InvalidOperationException("The benchmark did not select its expected target.");
    }

    [Benchmark]
    public IUnit? PickTarget() => _script.PickTarget();

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();
}
