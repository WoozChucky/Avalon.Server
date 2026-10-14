using System.Reflection;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Scripts.Creatures;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// The creature scripts' part of an instance's tick (<c>MapInstance.UpdateCreatureScripts</c>, bound by name), over a
/// population of real <see cref="AggroDefendScript" />s, including the existing periodic search and real kitless combat
/// decisions.
/// </summary>
[MemoryDiagnoser]
public class CreatureDispatchBenchmarks
{
    private const int Population = 11000;
    private const int Ticks = 61; // One complete detector interval at the existing float timer's 60 Hz step.
    private ScenarioWorld _world = null!;
    private AiScript[] _scripts = null!;
    private Action<TimeSpan> _step = null!;

    [Params(0, 5, 100)]
    public int ActivePercent { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = new ScenarioWorld();
        MapInstance map = _world.AddInstance();
        // Three out-of-range characters: searches do real dictionary/distance work but never change the mix.
        for (uint i = 1; i <= 3; i++)
            _world.Join(map, _world.NewCharacter(i), new Vector3(-100f, 0f, -100f));
        CharacterEntity target = _world.NewCharacter(4);
        target.Position = TownNavmesh.EntrySpawn + new Vector3(1f, 0f, 0f);
        var metadata = new CreatureTemplate { Id = new CreatureTemplateId(1), DetectionRange = 10f };
        _scripts = new AiScript[Population];
        for (int i = 0; i < Population; i++)
        {
            var creature = new Creature
            {
                Guid = new ObjectGuid(ObjectType.Creature, (uint)(i + 1)),
                TemplateId = metadata.Id,
                Metadata = metadata,
                Position = TownNavmesh.EntrySpawn,
                Health = 100,
                CurrentHealth = 100,
                Speed = 4f,
            };
            var script = new AggroDefendScript(NullLoggerFactory.Instance, creature, map, _world.Clock);
            creature.Script = script;
            map.AddCreature(creature);
            if (i % 100 < ActivePercent)
                script.OnEnteredRange(target);
            _scripts[i] = script;
        }

        // The instance's own script step, as its tick runs it: private, so bound by name, the same before and after.
        _step = typeof(MapInstance).GetMethod("UpdateCreatureScripts", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<TimeSpan>>(map);

        // Settle paths and JIT tiering outside measurement, without evolving a fight or changing the population.
        var warmup = System.Diagnostics.Stopwatch.StartNew();
        while (warmup.Elapsed < TimeSpan.FromSeconds(15)) UpdateScripts();
        RequireActiveMix();
    }

    // Per-tick cost for all 11,000 creatures. Excludes locomotion integration, casts, replication and other tick stages.
    [Benchmark(OperationsPerInvoke = Ticks)]
    public void UpdateScripts()
    {
        for (int tick = 0; tick < Ticks; tick++)
            _step(ScenarioWorld.Dt);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            RequireActiveMix();
        }
        finally
        {
            _world.Dispose();
        }
    }

    private void RequireActiveMix()
    {
        // Setup/cleanup only: do not let a lost target quietly turn the active benchmark into an idle one.
        FieldInfo combatField = typeof(AggroDefendScript).GetField("_combat", BindingFlags.Instance | BindingFlags.NonPublic)!;
        int active = _scripts.Count(s => ((AiScript)combatField.GetValue(s)!).State is CreatureCombatScript.CombatState.Combat);
        if (active != Population * ActivePercent / 100)
            throw new InvalidOperationException($"Expected {ActivePercent}% active, found {active}/{Population}");
    }
}
