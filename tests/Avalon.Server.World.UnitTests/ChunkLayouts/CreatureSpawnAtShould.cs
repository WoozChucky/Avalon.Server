using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.World.ChunkLayouts;
using Avalon.World.Entities;
using Avalon.World.Maps.Navigation;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using Avalon.World.Scripts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Avalon.Server.World.UnitTests.ChunkLayouts;

/// <summary>One creature placed at runtime (item use), on the ground, with its script, in the given instance only.</summary>
public class CreatureSpawnAtShould
{
    private readonly ICreatureSpawner _spawner = Substitute.For<ICreatureSpawner>();
    private readonly IMapInstance _instance = Substitute.For<IMapInstance>();
    private readonly ICreature _creature = Substitute.For<ICreature>();
    private readonly IScriptManager _scripts = Substitute.For<IScriptManager>();

    private CreaturePlacementService Service() => new(_spawner, Substitute.For<IChunkLibrary>(),
        Substitute.For<ISpawnTableRepository>(), Substitute.For<IMapCreatureSpawnRepository>(),
        _scripts, Substitute.For<IServiceProvider>(), NullLoggerFactory.Instance);

    public CreatureSpawnAtShould()
    {
        _creature.ScriptName.Returns((string?)null);
        _spawner.Spawn(Arg.Any<CreatureInfo>()).Returns(_creature);
    }

    [Fact]
    public void Spawn_on_the_ground_under_the_point_and_add_it_to_the_instance()
    {
        var navigator = Substitute.For<IMapNavigator>();
        navigator.SampleGroundHeight(4f, 9f, 5f).Returns(1.5f);
        _instance.GetNavigatorForPosition(Arg.Any<Vector3>()).Returns(navigator);

        ICreature? spawned = Service().SpawnAt(_instance, new CreatureTemplateId(4), new Vector3(4, 9, 5));

        Assert.Same(_creature, spawned);
        _spawner.Received(1).Spawn(Arg.Is<CreatureInfo>(i => i.PrototypeIndex == 4 && i.Position == new Vector3(4, 1.5f, 5)));
        _instance.Received(1).AddCreature(_creature);
    }

    [Fact]
    public void Place_nothing_where_the_navmesh_has_no_ground()
    {
        var navigator = Substitute.For<IMapNavigator, IGroundNavigator>();
        ((IGroundNavigator)navigator).FindGround(Arg.Any<Vector3>(), out Arg.Any<Vector3>()).Returns(NavmeshGroundKind.None);
        _instance.GetNavigatorForPosition(Arg.Any<Vector3>()).Returns(navigator);

        Assert.Null(Service().SpawnAt(_instance, new CreatureTemplateId(4), Vector3.zero));
        _spawner.DidNotReceiveWithAnyArgs().Spawn(default!);
        _instance.DidNotReceiveWithAnyArgs().AddCreature(default!);
    }

    [Fact]
    public void Answer_null_when_the_spawn_throws()
    {
        _instance.GetNavigatorForPosition(Arg.Any<Vector3>()).Returns(Substitute.For<IMapNavigator>());
        _spawner.Spawn(Arg.Any<CreatureInfo>()).Throws(new KeyNotFoundException("no template 999"));

        Assert.Null(Service().SpawnAt(_instance, new CreatureTemplateId(999), Vector3.zero));
        _instance.DidNotReceiveWithAnyArgs().AddCreature(default!);
    }

    [Theory]
    [InlineData(NavmeshGroundKind.Under)]
    [InlineData(NavmeshGroundKind.Nearest)]
    public void Stand_exactly_where_the_ground_was_found(NavmeshGroundKind found)
    {
        var ground = new Vector3(3.25f, 0.75f, 6.5f);
        var navigator = Substitute.For<IMapNavigator, IGroundNavigator>();
        ((IGroundNavigator)navigator).FindGround(new Vector3(3, 2, 6), out Arg.Any<Vector3>())
            .Returns(call =>
            {
                call[1] = ground;
                return found;
            });
        _instance.GetNavigatorForPosition(Arg.Any<Vector3>()).Returns(navigator);

        Assert.Same(_creature, Service().SpawnAt(_instance, new CreatureTemplateId(4), new Vector3(3, 2, 6)));
        _spawner.Received(1).Spawn(Arg.Is<CreatureInfo>(i => i.Position == ground));
    }

    [Fact]
    public void Attach_the_creatures_named_script()
    {
        var instance = Substitute.For<IMapInstance, Avalon.World.Public.Instances.ISimulationContext>();
        instance.GetNavigatorForPosition(Arg.Any<Vector3>()).Returns(Substitute.For<IMapNavigator>());
        _creature.ScriptName.Returns(nameof(Avalon.World.Scripts.Creatures.TownNpcScript));
        _scripts.GetAiScript(nameof(Avalon.World.Scripts.Creatures.TownNpcScript))
            .Returns(typeof(Avalon.World.Scripts.Creatures.TownNpcScript));

        Assert.Same(_creature, Service().SpawnAt(instance, new CreatureTemplateId(4), Vector3.zero));

        Assert.IsType<Avalon.World.Scripts.Creatures.TownNpcScript>(_creature.Script);
        instance.Received(1).AddCreature(_creature);
    }
}
