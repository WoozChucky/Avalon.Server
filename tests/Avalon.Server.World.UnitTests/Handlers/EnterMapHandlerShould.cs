using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.World;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using Avalon.World.Scripts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Handlers;

public class EnterMapHandlerShould
{
    private const ushort TownMap = 1;
    private const ushort TargetMap = 2;

    /// <summary>
    /// A connection kicked by a select of its character on another connection has released it
    /// before the target instance is ready. The continuation still runs, and must not dereference
    /// the character it no longer holds, nor transfer an entity that has already left.
    /// </summary>
    [Fact]
    public void Do_nothing_when_the_character_has_left_the_connection_before_the_target_instance_is_ready()
    {
        CharacterEntity character = New(7);
        MapInstance source = Source();
        character.InstanceId = source.InstanceId;
        character.Position = Vector3.zero;

        var registry = Substitute.For<IInstanceRegistry>();
        registry.GetInstanceById(source.InstanceId).Returns(source);
        var target = Substitute.For<IMapInstance>();
        registry.GetOrCreateTownInstanceAsync(Arg.Any<MapTemplateId>(), Arg.Any<ushort>()).Returns(target);

        var world = Substitute.For<IWorld>();
        world.InstanceRegistry.Returns(registry);
        world.MapTemplates.Returns(new List<MapTemplate>
        {
            new() { Id = new MapTemplateId(TargetMap), MapType = MapType.Town, Name = "target", Description = "" }
        });

        var connection = Substitute.For<IWorldConnection>();
        connection.InGame.Returns(true);
        connection.Character.Returns(character);
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        Action<IMapInstance>? onInstance = null;
        connection.When(c => c.EnqueueContinuation(Arg.Any<Task<IMapInstance>>(), Arg.Any<Action<IMapInstance>>()))
            .Do(call => onInstance = call.Arg<Action<IMapInstance>>());

        var saver = Substitute.For<ICharacterSaver>();
        var handler = new EnterMapHandler(NullLogger<EnterMapHandler>.Instance, saver, Substitute.For<IChunkLibrary>(), world);

        handler.Execute(connection, new CEnterMapPacket { TargetMapId = TargetMap });
        Assert.NotNull(onInstance);

        connection.Character.Returns((ICharacter?)null);

        Exception? escaped = Record.Exception(() => onInstance!(target));

        Assert.Null(escaped);
        world.DidNotReceiveWithAnyArgs().TransferPlayer(default!, default!);
        saver.DidNotReceiveWithAnyArgs().Save(default!, default!);
    }

    /// <summary>A real instance with a layout and a portal to the target map at the origin, so Execute reaches the continuation.</summary>
    private static MapInstance Source()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IScriptManager)).Returns(Substitute.For<IScriptManager>());
        serviceProvider.GetService(typeof(CombatConfig)).Returns(new CombatConfig());

        var world = Substitute.For<IWorld>();
        world.Configuration.Returns(new GameConfiguration());

        var entryChunk = new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero);
        var layout = new ChunkLayout(Seed: 0, Chunks: [entryChunk], EntryChunk: entryChunk, BossChunk: null,
            Portals: [], EntrySpawnWorldPos: Vector3.zero, CellSize: 30f, Config: null);

        var source = new MapInstance(NullLoggerFactory.Instance, serviceProvider, world, new MapTemplateId(TownMap),
            ownerCharacterId: null, layout, Substitute.For<IMapNavigator>(), seed: 0);
        source.Dispose();   // detach the static entity events; this test raises none
        source.AddPortal(new PortalInstance(new ObjectGuid(ObjectType.Portal, 1), Vector3.zero, 5f, TargetMap, 1));
        return source;
    }
}
