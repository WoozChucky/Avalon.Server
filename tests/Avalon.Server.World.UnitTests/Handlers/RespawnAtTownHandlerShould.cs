using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Handlers;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Respawn;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Handlers;

public class RespawnAtTownHandlerShould
{
    private static (RespawnAtTownHandler handler, IWorldConnection conn, ICharacter ch, IWorld world,
        IRespawnTargetResolver resolver, IInstanceRegistry registry, IMapInstance townInstance)
        Build(bool isDead, ushort currentMapId = 2, ushort townMapId = 1)
    {
        IChunkLibrary chunkLibrary = Substitute.For<IChunkLibrary>();
        ICharacter ch = Substitute.For<ICharacter>();
        ch.IsDead.Returns(isDead);
        ch.Map.Returns(new MapId(currentMapId));
        ch.InstanceId.Returns(Guid.NewGuid());
        ch.Health.Returns(100u);

        IWorldConnection conn = Substitute.For<IWorldConnection>();
        conn.Character.Returns(ch);
        conn.AccountId.Returns(new AccountId(42L));

        IRespawnTargetResolver resolver = Substitute.For<IRespawnTargetResolver>();
        resolver.ResolveTownAsync(Arg.Any<MapTemplateId>(), Arg.Any<CancellationToken>())
            .Returns(new MapTemplateId(townMapId));

        IInstanceRegistry registry = Substitute.For<IInstanceRegistry>();
        IMapInstance townInstance = Substitute.For<IMapInstance>();
        registry.GetOrCreateTownInstanceAsync(Arg.Any<MapTemplateId>(), Arg.Any<ushort>())
            .Returns(Task.FromResult(townInstance));

        IWorld world = Substitute.For<IWorld>();
        world.InstanceRegistry.Returns(registry);
        world.MapTemplates.Returns(new List<MapTemplate>
        {
            new() { Id = new MapTemplateId(townMapId), MapType = MapType.Town, Name = "town", Description = "" }
        });

        var handler = new RespawnAtTownHandler(
            NullLogger<RespawnAtTownHandler>.Instance,
            world, resolver, chunkLibrary);

        return (handler, conn, ch, world, resolver, registry, townInstance);
    }

    [Fact]
    public void Drop_when_character_is_not_dead()
    {
        (RespawnAtTownHandler? handler, IWorldConnection? conn, ICharacter _, IWorld? world, IRespawnTargetResolver? resolver, IInstanceRegistry _, IMapInstance _) = Build(isDead: false);

        handler.Execute(conn, new CRespawnAtTownPacket());

        resolver.DidNotReceiveWithAnyArgs().ResolveTownAsync(default!, default);
        world.DidNotReceiveWithAnyArgs().TransferPlayer(default!, default!);
    }

    [Fact]
    public void Resolve_town_and_enqueue_transfer_when_dead()
    {
        (RespawnAtTownHandler? handler, IWorldConnection? conn, ICharacter _, IWorld _, IRespawnTargetResolver? resolver, IInstanceRegistry _, IMapInstance _) = Build(isDead: true);

        handler.Execute(conn, new CRespawnAtTownPacket());

        resolver.Received(1).ResolveTownAsync(new MapTemplateId(2), Arg.Any<CancellationToken>());
        conn.Received(1).EnqueueContinuation(Arg.Any<Task>(), Arg.Any<Action>());
    }

    [Fact]
    public void Drop_when_a_respawn_is_already_in_flight()
    {
        (RespawnAtTownHandler? handler, IWorldConnection? conn, ICharacter _, IWorld _, IRespawnTargetResolver? resolver, IInstanceRegistry _, IMapInstance _) = Build(isDead: true);
        conn.RespawnInFlight.Returns(true);

        handler.Execute(conn, new CRespawnAtTownPacket());

        resolver.DidNotReceiveWithAnyArgs().ResolveTownAsync(default!, default);
    }

    /// <summary>
    /// A connection kicked by a select of its character on another connection has released it
    /// before the town loads. The transfer would dereference a character that is no longer there,
    /// or revive and move an entity that has already left.
    /// </summary>
    [Fact]
    public void Do_nothing_when_the_character_has_left_the_connection_before_the_town_is_ready()
    {
        (RespawnAtTownHandler? handler, IWorldConnection? conn, ICharacter? ch, IWorld? world, IRespawnTargetResolver _, IInstanceRegistry _, IMapInstance _) = Build(isDead: true);
        var continuations = new List<Action>();
        conn.When(c => c.EnqueueContinuation(Arg.Any<Task>(), Arg.Any<Action>()))
            .Do(call => continuations.Add(call.Arg<Action>()));
        conn.CryptoSession.Returns(new FakeAvalonCryptoSession());

        handler.Execute(conn, new CRespawnAtTownPacket());
        continuations[0]();   // the town is resolved: the instance is asked for
        conn.Character.Returns((ICharacter?)null);

        Exception? escaped = Record.Exception(() => continuations[1]());   // the town instance is ready

        Assert.Null(escaped);
        world.DidNotReceiveWithAnyArgs().TransferPlayer(default!, default!);
        ch.DidNotReceive().Revive();
    }

    [Fact]
    public void Mark_RespawnInFlight_true_when_accepting_a_request()
    {
        (RespawnAtTownHandler? handler, IWorldConnection? conn, ICharacter _, IWorld _, IRespawnTargetResolver _, IInstanceRegistry _, IMapInstance _) = Build(isDead: true);

        handler.Execute(conn, new CRespawnAtTownPacket());

        conn.Received().RespawnInFlight = true;
    }
}
