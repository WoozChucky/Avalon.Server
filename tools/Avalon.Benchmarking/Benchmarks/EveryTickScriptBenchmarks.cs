using System.Reflection;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// What the creature scheduler (#979) costs a script that needs every tick: 11,000 creatures whose script only counts its
/// Updates and answers <see cref="AiScript.NextUpdateIn" /> zero, through the instance's own script step
/// (<c>MapInstance.UpdateCreatureScripts</c>, bound by name), scheduled or with every creature walked every tick as before
/// #979 (<c>MapInstance.UpdateEveryCreatureEveryTick</c>, bound by name). The difference is the scheduler's own work per
/// such creature: its place in the bit set, and reading its answer after each Update.
/// </summary>
[MemoryDiagnoser]
public class EveryTickScriptBenchmarks
{
    private const int Population = 11000;
    private const int Ticks = 60;
    private ScenarioWorld _world = null!;
    private Action<TimeSpan> _step = null!;

    [Params(true, false)]
    public bool Scheduled { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = new ScenarioWorld();
        MapInstance map = _world.AddInstance();
        _world.Join(map, _world.NewCharacter(1), new Vector3(-100f, 0f, -100f));
        var metadata = new CreatureTemplate { Id = new CreatureTemplateId(1) };
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
            };
            creature.Script = new CountingScript(creature, map);
            map.AddCreature(creature);
        }

        if (!Scheduled)
            typeof(MapInstance).GetMethod("UpdateEveryCreatureEveryTick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(map, null);

        _step = typeof(MapInstance).GetMethod("UpdateCreatureScripts", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<TimeSpan>>(map);
        for (int i = 0; i < 600; i++)
            _step(ScenarioWorld.Dt);
    }

    // Per tick, for all 11,000 creatures.
    [Benchmark(OperationsPerInvoke = Ticks)]
    public void UpdateScripts()
    {
        for (int tick = 0; tick < Ticks; tick++)
            _step(ScenarioWorld.Dt);
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();

    /// <summary>A script that needs every tick and does nearly nothing in it, so the step's own cost shows.</summary>
    private sealed class CountingScript(ICreature creature, ISimulationContext context) : AiScript(creature, context)
    {
        public int Updates;

        public override object State { get; set; } = 0;

        public override TimeSpan NextUpdateIn => TimeSpan.Zero;

        public override void Update(TimeSpan deltaTime) => Updates++;

        protected override bool ShouldRun() => false;
    }
}
