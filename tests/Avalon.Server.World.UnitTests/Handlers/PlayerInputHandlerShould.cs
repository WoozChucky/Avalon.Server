using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Movement;
using Avalon.World;
using Avalon.World.Handlers;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Handlers;

public class PlayerInputHandlerShould
{
    private const float TickDt = 1f / 60f;

    private static (PlayerInputHandler handler, IWorldConnection conn, ICharacter ch, IMapNavigator nav) Setup(float speed = 5f, Vector3 startPos = default)
    {
        var instanceId = Guid.NewGuid();

        ICharacter ch = Substitute.For<ICharacter>();
        ch.Position.Returns(startPos);
        ch.GetMovementSpeed().Returns(speed);
        ch.InstanceId.Returns(instanceId);

        IWorldConnection conn = Substitute.For<IWorldConnection>();
        conn.Character.Returns(ch);
        conn.LastInputSeq.Returns(0u);
        conn.CryptoSession.Returns(new FakeAvalonCryptoSession());

        IMapNavigator nav = Substitute.For<IMapNavigator>();
        // Default: clear path — RaycastWalkable returns whatever the desired endpoint was.
        nav.RaycastWalkable(Arg.Any<Vector3>(), Arg.Any<Vector3>())
            .Returns(call => call.ArgAt<Vector3>(1));   // returns 'to'
        nav.SampleGroundHeight(Arg.Any<float>(), Arg.Any<float>(), Arg.Any<float>()).Returns(0f);

        IMapInstance instance = Substitute.For<IMapInstance>();
        instance.GetNavigatorForPosition(Arg.Any<Vector3>()).Returns(nav);

        IInstanceRegistry registry = Substitute.For<IInstanceRegistry>();
        registry.GetInstanceById(instanceId).Returns(instance);

        IWorld world = Substitute.For<IWorld>();
        world.InstanceRegistry.Returns(registry);

        var handler = new PlayerInputHandler(NullLogger<PlayerInputHandler>.Instance, world);
        return (handler, conn, ch, nav);
    }

    /// <summary>
    /// An accepted input steps the character speed x 1/60 s along its direction, clamped to unit length, and puts it
    /// at the ground height the navmesh samples. Velocity is metres per second and horizontal (#424): other clients
    /// extrapolate the character as position + Velocity x seconds, and a ground-height change is a snap, not motion.
    /// </summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(10f)]
    public void Step_along_the_input_onto_the_ground_and_publish_the_velocity(float dirX)
    {
        (PlayerInputHandler? handler, IWorldConnection? conn, ICharacter? ch, IMapNavigator? nav) = Setup(speed: 5f);
        nav.SampleGroundHeight(Arg.Any<float>(), Arg.Any<float>(), Arg.Any<float>()).Returns(2.5f);

        handler.Execute(conn, new CPlayerInputPacket { Seq = 1, DirX = dirX, DirZ = 0, YawDeg = 90 });

        ch.Received(1).Position = Arg.Is<Vector3>(p =>
            Math.Abs(p.x - (5f * TickDt)) < 1e-4 && Math.Abs(p.y - 2.5f) < 1e-4 && Math.Abs(p.z) < 1e-4);
        ch.Received(1).Velocity = Arg.Is<Vector3>(v =>
            Math.Abs(v.x - 5f) < 1e-3 && Math.Abs(v.y) < 1e-6 && Math.Abs(v.z) < 1e-6);
    }

    /// <summary>
    /// The navmesh clamps the step, and the velocity is the step actually taken (#424): 0.05 m in one 1/60 s step is
    /// 3 m/s, not the 5 m/s the input asked for, and pressing into a wall (no step at all) publishes zero, so the
    /// character is not extrapolated into the wall.
    /// </summary>
    [Theory]
    [InlineData(0.05f)]
    [InlineData(0f)]
    public void Publish_the_step_the_navmesh_allowed_as_velocity(float allowedX)
    {
        (PlayerInputHandler? handler, IWorldConnection? conn, ICharacter? ch, IMapNavigator? nav) = Setup(speed: 5f);
        nav.RaycastWalkable(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns(new Vector3(allowedX, 0, 0));

        handler.Execute(conn, new CPlayerInputPacket { Seq = 1, DirX = 1, DirZ = 0 });

        ch.Received(1).Position = Arg.Is<Vector3>(p => Math.Abs(p.x - allowedX) < 1e-4);
        ch.Received(1).Velocity = Arg.Is<Vector3>(v => Math.Abs(v.x - allowedX / TickDt) < 1e-2 && Math.Abs(v.z) < 1e-6);
    }

    /// <summary>An accepted input is acknowledged with the authoritative state and advances the last sequence seen.</summary>
    [Fact]
    public void Acknowledge_an_accepted_input_and_advance_the_sequence()
    {
        (PlayerInputHandler? handler, IWorldConnection? conn, ICharacter _, IMapNavigator _) = Setup(speed: 5f);

        handler.Execute(conn, new CPlayerInputPacket { Seq = 7, DirX = 1, DirZ = 0, YawDeg = 90 });

        conn.Received(1).Send(Arg.Any<global::Avalon.Network.Packets.Abstractions.NetworkPacket>());
        conn.Received(1).LastInputSeq = 7u;
    }

    /// <summary>An input from a dead character, or one out of order (TCP keeps order, so it should never come), moves nothing and is not answered.</summary>
    [Theory]
    [InlineData(true, 1u)]
    [InlineData(false, 4u)]
    public void Drop_an_input_without_moving_or_answering(bool dead, uint seq)
    {
        (PlayerInputHandler? handler, IWorldConnection? conn, ICharacter? ch, IMapNavigator _) = Setup();
        ch.IsDead.Returns(dead);
        conn.LastInputSeq.Returns(dead ? 0u : 5u);

        handler.Execute(conn, new CPlayerInputPacket { Seq = seq, DirX = 1, DirZ = 0 });

        ch.DidNotReceive().Position = Arg.Any<Vector3>();
        conn.DidNotReceive().Send(Arg.Any<global::Avalon.Network.Packets.Abstractions.NetworkPacket>());
    }
}
