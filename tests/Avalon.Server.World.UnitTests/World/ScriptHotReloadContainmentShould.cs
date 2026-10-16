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
/// A hot reload is applied on the tick, so a script in it that cannot be built must cost nothing else (#930): not the
/// creatures it was meant for, not the other scripts of the reload, not the tick. Creature ids 930_1xx are unique to
/// this class.
/// </summary>
public class ScriptHotReloadContainmentShould
{
    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);
    private static readonly MapTemplateId s_townId = new(1);

    private sealed class ScriptBeforeReload(ICreature creature, ISimulationContext context) : AiScript(creature, context)
    {
        public override object State { get; set; } = 0;

        public override TimeSpan NextUpdateIn => TimeSpan.Zero;

        protected override bool ShouldRun() => false;
    }

    /// <summary>A reloaded script whose constructor throws.</summary>
    private sealed class UnbuildableReloadScript : AiScript
    {
        public UnbuildableReloadScript(ICreature creature, ISimulationContext context) : base(creature, context) =>
            throw new InvalidOperationException("broken script");

        public override object State { get; set; } = 0;

        public override TimeSpan NextUpdateIn => TimeSpan.Zero;

        protected override bool ShouldRun() => false;
    }

    /// <summary>
    /// Two reloaded types whose names differ in case only, one name to the world's lookup. (Two of one name in two
    /// namespaces would be the same to it, but would also be two built-in scripts of one name to every ScriptManager
    /// this test assembly loads.)
    /// </summary>
    private sealed class TwinScript(ICreature creature, ISimulationContext context) : AiScript(creature, context)
    {
        public override object State { get; set; } = 0;

        public override TimeSpan NextUpdateIn => TimeSpan.Zero;

        protected override bool ShouldRun() => false;
    }

    private sealed class Twinscript(ICreature creature, ISimulationContext context) : AiScript(creature, context)
    {
        public override object State { get; set; } = 0;

        public override TimeSpan NextUpdateIn => TimeSpan.Zero;

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

    private static Creature Running(uint id, string scriptName, MapInstance town)
    {
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Metadata = LootTestData.BoarTemplate(null),
            ScriptName = scriptName,
            Position = Vector3.zero,
            Health = 100,
            CurrentHealth = 100,
        };
        creature.Script = new ScriptBeforeReload(creature, town);
        town.AddCreature(creature);
        return creature;
    }

    [Fact]
    public async Task Leave_the_creatures_of_a_script_that_cannot_be_built_as_they_were_and_apply_the_rest()
    {
        using MapInstance town = TestMapInstances.Build(NewWorld(), mapType: MapType.Town);
        Creature broken = Running(930_101, nameof(UnbuildableReloadScript), town);
        Creature alsoBroken = Running(930_102, nameof(UnbuildableReloadScript), town);
        Creature twin = Running(930_103, nameof(TwinScript), town);
        AiScript brokenScript = broken.Script!;

        IAvalonMapManager mapManager = Substitute.For<IAvalonMapManager>();
        mapManager.Templates.Returns([new MapTemplate { Id = s_townId, MapType = MapType.Town }]);
        IChunkLayoutInstanceFactory factory = Substitute.For<IChunkLayoutInstanceFactory>();
        factory.BuildAsync(default!, default, default).ReturnsForAnyArgs(town);
        var log = new TestLog();
        Avalon.World.World world = await ScriptHotReloadPollingShould.BuildWorldAsync(
            new OneShotScriptHotReloader(typeof(UnbuildableReloadScript), typeof(TwinScript), typeof(Twinscript)),
            intervalSeconds: 1, mapManager, factory, loggerFactory: log);
        await world.InstanceRegistry.GetOrCreateTownInstanceAsync(s_townId, maxPlayers: 100).Published(world);

        world.Update(TimeSpan.FromSeconds(1)); // polls the reloader
        world.Update(s_tick);                    // applies the reload

        Assert.Same(brokenScript, broken.Script);
        Assert.IsType<ScriptBeforeReload>(alsoBroken.Script);
        Assert.Same(broken, town.Creatures[broken.Guid]);
        Assert.Same(alsoBroken, town.Creatures[alsoBroken.Guid]);
        Assert.IsType<Twinscript>(twin.Script); // the last of a name wins
        Assert.Single(log.Errors);                     // once for the script, not once per creature
    }
}
