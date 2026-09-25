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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Handlers;

public class RespawnAtTownHandlerShould
{
    private static (RespawnAtTownHandler handler, IWorldConnection conn, ICharacter ch, IWorld world,
        IRespawnTargetResolver resolver, IInstanceRegistry registry, IMapInstance townInstance)
        Build(bool isDead, ushort currentMapId = 2, ushort townMapId = 1)
    {
        var chunkLibrary = Substitute.For<IChunkLibrary>();
        var ch = Substitute.For<ICharacter>();
        ch.IsDead.Returns(isDead);
        ch.Map.Returns(new MapId(currentMapId));
        ch.InstanceId.Returns(Guid.NewGuid());
        ch.Health.Returns(100u);

        var conn = Substitute.For<IWorldConnection>();
        conn.Character.Returns(ch);
        conn.AccountId.Returns(new AccountId(42L));

        var resolver = Substitute.For<IRespawnTargetResolver>();
        resolver.ResolveTownAsync(Arg.Any<MapTemplateId>(), Arg.Any<CancellationToken>())
            .Returns(new MapTemplateId(townMapId));

        var registry = Substitute.For<IInstanceRegistry>();
        var townInstance = Substitute.For<IMapInstance>();
        registry.GetOrCreateTownInstanceAsync(Arg.Any<MapTemplateId>(), Arg.Any<ushort>())
            .Returns(Task.FromResult(townInstance));

        var world = Substitute.For<IWorld>();
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
        var (handler, conn, _, world, resolver, _, _) = Build(isDead: false);

        handler.Execute(conn, new CRespawnAtTownPacket());

        resolver.DidNotReceiveWithAnyArgs().ResolveTownAsync(default!, default);
        world.DidNotReceiveWithAnyArgs().TransferPlayer(default!, default!);
    }

    [Fact]
    public void Resolve_town_and_enqueue_transfer_when_dead()
    {
        var (handler, conn, _, _, resolver, _, _) = Build(isDead: true);

        handler.Execute(conn, new CRespawnAtTownPacket());

        resolver.Received(1).ResolveTownAsync(new MapTemplateId(2), Arg.Any<CancellationToken>());
        conn.ReceivedWithAnyArgs().EnqueueContinuation<MapTemplateId>(default!, default!);
    }

    [Fact]
    public void Drop_when_a_respawn_is_already_in_flight()
    {
        var (handler, conn, _, _, resolver, _, _) = Build(isDead: true);
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
        var (handler, conn, ch, world, _, _, townInstance) = Build(isDead: true);
        Action<MapTemplateId>? onTown = null;
        Action<IMapInstance>? onInstance = null;
        conn.When(c => c.EnqueueContinuation(Arg.Any<Task<MapTemplateId>>(), Arg.Any<Action<MapTemplateId>>()))
            .Do(call => onTown = call.Arg<Action<MapTemplateId>>());
        conn.When(c => c.EnqueueContinuation(Arg.Any<Task<IMapInstance>>(), Arg.Any<Action<IMapInstance>>()))
            .Do(call => onInstance = call.Arg<Action<IMapInstance>>());
        conn.CryptoSession.Returns(new FakeAvalonCryptoSession());

        handler.Execute(conn, new CRespawnAtTownPacket());
        onTown!(new MapTemplateId(1));
        conn.Character.Returns((ICharacter?)null);

        Exception? escaped = Record.Exception(() => onInstance!(townInstance));

        Assert.Null(escaped);
        world.DidNotReceiveWithAnyArgs().TransferPlayer(default!, default!);
        ch.DidNotReceive().Revive();
    }

    [Fact]
    public void Mark_RespawnInFlight_true_when_accepting_a_request()
    {
        var (handler, conn, _, _, _, _, _) = Build(isDead: true);

        handler.Execute(conn, new CRespawnAtTownPacket());

        conn.Received().RespawnInFlight = true;
    }
}
