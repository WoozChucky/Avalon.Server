using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World.ChunkLayouts;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Maps;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Scripts.Abstractions;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.World;

/// <summary>
/// #639: an instance is registered on the tick, so an AI script hot reload reaches every registered instance. One whose
/// build was in flight while a reload was applied attached its scripts from the types of before; it is brought up to
/// date as it is published. Creature ids 639_2xx are unique to this class.
/// </summary>
public class HotReloadPublishedInstanceShould
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1d / 60d);
    private static readonly MapTemplateId TownId = new(1);

    /// <summary>What a script was before the reload.</summary>
    private sealed class BeforeReloadScript(ICreature creature, ISimulationContext context) : AiScript(creature, context)
    {
        public override object State { get; set; } = 0;

        protected override bool ShouldRun() => false;
    }

    /// <summary>The hot-reloaded type, named as the creatures' script name.</summary>
    private sealed class ReloadedScript(ICreature creature, ISimulationContext context) : AiScript(creature, context)
    {
        public override object State { get; set; } = 0;

        protected override bool ShouldRun() => false;
    }

    /// <summary>Reports the given script types on its first poll, and nothing after.</summary>
    private sealed class OneShotScriptHotReloader(params Type[] types) : IScriptHotReloader
    {
        private bool _reported;

        public void Update(out List<Type> scriptTypes)
        {
            scriptTypes = _reported ? [] : [.. types];
            _reported = true;
        }

        public event ScriptsHotReloadedEventHandler? ScriptsHotReloaded;

        public void Start() => ScriptsHotReloaded?.Invoke([]);

        public void Stop() { }
    }

    private static Creature CreatureRunning(uint id, MapInstance instance, AiScript? script = null)
    {
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Metadata = LootTestData.BoarTemplate(null),
            ScriptName = nameof(ReloadedScript),
            Position = Vector3.zero,
            Health = 100,
            CurrentHealth = 100,
        };
        creature.Script = script ?? new BeforeReloadScript(creature, instance);
        instance.AddCreature(creature);
        return creature;
    }

    [Fact]
    public async Task Give_an_instance_built_during_a_reload_the_reloaded_scripts_when_it_is_published()
    {
        var build = new TaskCompletionSource<MapInstance>();
        IAvalonMapManager mapManager = Substitute.For<IAvalonMapManager>();
        mapManager.Templates.Returns([new MapTemplate { Id = TownId, MapType = MapType.Town }]);
        IChunkLayoutInstanceFactory factory = Substitute.For<IChunkLayoutInstanceFactory>();
        factory.BuildAsync(default!, default, default).ReturnsForAnyArgs(build.Task);
        Avalon.World.World world = await ScriptHotReloadPollingShould.BuildWorldAsync(
            new OneShotScriptHotReloader(typeof(ReloadedScript)), intervalSeconds: 1, mapManager, factory);

        Task<IMapInstance> requested = world.InstanceRegistry.GetOrCreateTownInstanceAsync(TownId, maxPlayers: 100);
        world.Update(TimeSpan.FromSeconds(1)); // polls the reloader
        world.Update(Tick);                    // applies the reload, while the town is still building

        using MapInstance town = TestMapInstances.Build(NewWorld(), mapType: MapType.Town);
        Creature outdated = CreatureRunning(639_201, town);
        Creature corpse = CreatureRunning(639_202, town);
        corpse.CurrentHealth = 0;
        AiScript corpseScript = corpse.Script!;
        build.SetResult(town); // the build attached its scripts from the types of before the reload

        Assert.Empty(world.InstanceRegistry.ActiveInstances);
        Assert.IsType<BeforeReloadScript>(outdated.Script);

        world.Update(Tick);

        Assert.Same(town, await requested.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Same(town, Assert.Single(world.InstanceRegistry.ActiveInstances));
        Assert.IsType<ReloadedScript>(outdated.Script);
        Assert.Same(corpseScript, corpse.Script); // a corpse gets no fresh script (#607)
    }

    /// <summary>A creature that already runs the reloaded type keeps its script: the build saw the new types.</summary>
    [Fact]
    public async Task Keep_a_script_already_of_the_reloaded_type()
    {
        var build = new TaskCompletionSource<MapInstance>();
        IAvalonMapManager mapManager = Substitute.For<IAvalonMapManager>();
        mapManager.Templates.Returns([new MapTemplate { Id = TownId, MapType = MapType.Town }]);
        IChunkLayoutInstanceFactory factory = Substitute.For<IChunkLayoutInstanceFactory>();
        factory.BuildAsync(default!, default, default).ReturnsForAnyArgs(build.Task);
        Avalon.World.World world = await ScriptHotReloadPollingShould.BuildWorldAsync(
            new OneShotScriptHotReloader(typeof(ReloadedScript)), intervalSeconds: 1, mapManager, factory);

        _ = world.InstanceRegistry.GetOrCreateTownInstanceAsync(TownId, maxPlayers: 100);
        world.Update(TimeSpan.FromSeconds(1));
        world.Update(Tick);

        using MapInstance town = TestMapInstances.Build(NewWorld(), mapType: MapType.Town);
        Creature current = CreatureRunning(639_203, town);
        var already = new ReloadedScript(current, town);
        current.Script = already;
        build.SetResult(town);

        world.Update(Tick);

        Assert.Same(already, current.Script);
    }
}
