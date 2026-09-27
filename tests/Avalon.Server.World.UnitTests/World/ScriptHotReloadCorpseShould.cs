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
using Avalon.World.Public.Enums;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Units;
using Avalon.World.Scripts.Abstractions;
using Avalon.World.Scripts.Creatures;
using NSubstitute;
using Xunit;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.World;

/// <summary>
/// #607: an AI script hot reload builds a fresh script for every living creature in each instance,
/// and none for a corpse, which stays in the instance until its body is removed. A fresh script
/// does not know its creature is dead, so a corpse given one could chase and swing at a character
/// who walks near it. Creature ids 607_0xx and character ids 607_1xx are unique to this class.
/// </summary>
public class ScriptHotReloadCorpseShould
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1d / 60d);
    private static readonly MapTemplateId TownId = new(1);

    /// <summary>Reports the given script types on its first poll, and nothing after.</summary>
    private sealed class OneShotScriptHotReloader(params Type[] types) : IScriptHotReloader
    {
        private bool _reported;

        public void Update(out List<Type> scriptTypes)
        {
            scriptTypes = _reported ? [] : [.. types];
            _reported = true;
        }

        // The world tick drives reloads by polling Update; the watcher's own lifecycle is not used.
        public event ScriptsHotReloadedEventHandler? ScriptsHotReloaded;

        public void Start() => ScriptsHotReloaded?.Invoke([]);

        public void Stop() { }
    }

    /// <summary>A loaded world whose one town instance is <paramref name="instance" />.</summary>
    private static async Task<Avalon.World.World> WorldHolding(MapInstance instance)
    {
        var mapManager = Substitute.For<IAvalonMapManager>();
        mapManager.Templates.Returns([new MapTemplate { Id = TownId, MapType = MapType.Town }]);
        var factory = Substitute.For<IChunkLayoutInstanceFactory>();
        factory.BuildAsync(default!, default, default).ReturnsForAnyArgs(instance);

        Avalon.World.World world = await ScriptHotReloadPollingShould.BuildWorldAsync(
            new OneShotScriptHotReloader(typeof(AggroDefendScript)), intervalSeconds: 1, mapManager, factory);
        await world.InstanceRegistry.GetOrCreateTownInstanceAsync(TownId, maxPlayers: 100);
        return world;
    }

    /// <summary>The first update polls the reloader; the second applies what it reported.</summary>
    private static void HotReload(Avalon.World.World world)
    {
        world.Update(TimeSpan.FromSeconds(1));
        world.Update(Tick);
    }

    private static Creature AggroCreature(uint id, Vector3 position) => new()
    {
        Guid = new ObjectGuid(ObjectType.Creature, id),
        Metadata = LootTestData.BoarTemplate(null),
        ScriptName = nameof(AggroDefendScript),
        Position = position,
        Speed = 4f,
        Health = 100,
        CurrentHealth = 100,
    };

    /// <summary>Kills <paramref name="creature" /> through the instance's kill handling.</summary>
    private static void Kill(MapInstance instance, Creature creature)
    {
        creature.CurrentHealth = 0;
        instance.ReportKill(creature, Substitute.For<IUnit>());
    }

    [Fact]
    public async Task Give_a_corpse_no_script()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        Creature corpse = AggroCreature(607_001, Vector3.zero);
        instance.AddCreature(corpse);
        Kill(instance, corpse);
        Assert.Null(corpse.Script);   // fixture check: the kill took its script

        Avalon.World.World world = await WorldHolding(instance);
        HotReload(world);

        Assert.Null(corpse.Script);
    }

    [Fact]
    public async Task Give_a_living_creature_in_the_same_instance_the_fresh_script()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        Creature corpse = AggroCreature(607_002, Vector3.zero);
        instance.AddCreature(corpse);
        Kill(instance, corpse);
        Creature living = AggroCreature(607_003, new Vector3(50f, 0f, 50f));
        instance.AddCreature(living);
        var old = new AggroDefendScript(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            living, instance);
        living.Script = old;

        Avalon.World.World world = await WorldHolding(instance);
        HotReload(world);

        Assert.IsType<AggroDefendScript>(living.Script);
        Assert.NotSame(old, living.Script);
        Assert.Null(corpse.Script);
    }

    /// <summary>
    /// The risk the issue names: a character stands inside the corpse's aggro range through the
    /// reload and for five seconds after, and the corpse neither paths toward it nor hits it.
    /// </summary>
    [Fact]
    public async Task Leave_a_corpse_still_with_a_character_inside_its_aggro_range()
    {
        var navigator = Substitute.For<IMapNavigator>();
        navigator.FindPath(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns(ci => [ci.ArgAt<Vector3>(1)]);
        navigator.RaycastWalkable(default, default).ReturnsForAnyArgs(ci => ci.ArgAt<Vector3>(1));
        navigator.HasVisibility(default, default).ReturnsForAnyArgs(true);   // the detector sees the character
        using MapInstance instance = TestMapInstances.Build(NewWorld(), navigator: navigator);
        Creature corpse = AggroCreature(607_004, Vector3.zero);
        instance.AddCreature(corpse);
        Kill(instance, corpse);
        MapInstanceClient near = Join(instance, 607_101);
        near.Character.Position = new Vector3(3f, 0f, 0f);
        near.Character.Health = 100;
        near.Character.CurrentHealth = 100;   // alive, so a script that ignores death would engage it
        uint health = near.Character.CurrentHealth;
        navigator.ClearReceivedCalls();

        Avalon.World.World world = await WorldHolding(instance);
        HotReload(world);
        for (int i = 0; i < 300; i++)
            world.Update(Tick);

        Assert.Equal(Vector3.zero, corpse.Position);
        navigator.DidNotReceive().FindPath(Arg.Any<Vector3>(), Arg.Any<Vector3>());
        Assert.Equal(health, near.Character.CurrentHealth);
        Assert.Null(instance.CombatService.GetEncounterFor(corpse));
    }
}
