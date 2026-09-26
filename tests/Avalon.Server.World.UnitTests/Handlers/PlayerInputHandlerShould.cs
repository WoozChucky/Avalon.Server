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
using Xunit;

namespace Avalon.Server.World.UnitTests.Handlers;

public class PlayerInputHandlerShould
{
    private const float TickDt = 1f / 60f;

    private static (PlayerInputHandler handler, IWorldConnection conn, ICharacter ch, IMapNavigator nav) Setup(float speed = 5f, Vector3 startPos = default)
    {
        var instanceId = Guid.NewGuid();

        var ch = Substitute.For<ICharacter>();
        ch.Position.Returns(startPos);
        ch.GetMovementSpeed().Returns(speed);
        ch.InstanceId.Returns(instanceId);

        var conn = Substitute.For<IWorldConnection>();
        conn.Character.Returns(ch);
        conn.LastInputSeq.Returns(0u);
        conn.CryptoSession.Returns(new FakeAvalonCryptoSession());

        var nav = Substitute.For<IMapNavigator>();
        // Default: clear path — RaycastWalkable returns whatever the desired endpoint was.
        nav.RaycastWalkable(Arg.Any<Vector3>(), Arg.Any<Vector3>())
            .Returns(call => call.ArgAt<Vector3>(1));   // returns 'to'
        nav.SampleGroundHeight(Arg.Any<float>(), Arg.Any<float>(), Arg.Any<float>()).Returns(0f);

        var instance = Substitute.For<IMapInstance>();
        instance.GetNavigatorForPosition(Arg.Any<Vector3>()).Returns(nav);

        var registry = Substitute.For<IInstanceRegistry>();
        registry.GetInstanceById(instanceId).Returns(instance);

        var world = Substitute.For<IWorld>();
        world.InstanceRegistry.Returns(registry);

        var handler = new PlayerInputHandler(NullLogger<PlayerInputHandler>.Instance, world);
        return (handler, conn, ch, nav);
    }

    [Fact]
    public void Integrate_position_by_dir_speed_dt()
    {
        var (handler, conn, ch, _) = Setup(speed: 5f, startPos: new Vector3(0, 0, 0));
        handler.Execute(conn, new CPlayerInputPacket { Seq = 1, DirX = 1, DirZ = 0, YawDeg = 90 });

        // Position should advance by 5 * (1/60) on X.
        ch.Received(1).Position = Arg.Is<Vector3>(p =>
            Math.Abs(p.x - (5f * TickDt)) < 1e-4 &&
            Math.Abs(p.y - 0f) < 1e-4 &&
            Math.Abs(p.z - 0f) < 1e-4);
    }

    [Fact]
    public void Clamp_direction_magnitude_to_unit()
    {
        var (handler, conn, ch, _) = Setup(speed: 5f);
        // Inflated input (mag = 10): should be normalised to unit length.
        handler.Execute(conn, new CPlayerInputPacket { Seq = 1, DirX = 10, DirZ = 0 });

        ch.Received(1).Position = Arg.Is<Vector3>(p => Math.Abs(p.x - (5f * TickDt)) < 1e-4);
    }

    [Fact]
    public void Drop_out_of_order_seq()
    {
        var (handler, conn, ch, _) = Setup();
        conn.LastInputSeq.Returns(5u);
        handler.Execute(conn, new CPlayerInputPacket { Seq = 4, DirX = 1, DirZ = 0 });

        ch.DidNotReceive().Position = Arg.Any<Vector3>();
        conn.DidNotReceive().Send(Arg.Any<global::Avalon.Network.Packets.Abstractions.NetworkPacket>());
    }

    [Fact]
    public void Clamp_to_navmesh_when_obstructed()
    {
        var (handler, conn, ch, nav) = Setup(speed: 5f, startPos: new Vector3(0, 0, 0));
        var clampedPoint = new Vector3(0.05f, 0, 0);
        nav.RaycastWalkable(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns(clampedPoint);

        handler.Execute(conn, new CPlayerInputPacket { Seq = 1, DirX = 1, DirZ = 0 });

        ch.Received(1).Position = Arg.Is<Vector3>(p => Math.Abs(p.x - 0.05f) < 1e-4);
    }

    // --- #424: Velocity is metres per second and describes the step actually taken ---------------
    //
    // Replicated to every other client, which extrapolates this character as position + Velocity *
    // seconds between broadcasts. So Velocity must be the movement that happened, in m/s: the input's
    // intent is not it when the navmesh stopped the character short.

    [Fact]
    public void Publish_velocity_in_metres_per_second_when_moving_freely()
    {
        var (handler, conn, ch, _) = Setup(speed: 5f, startPos: new Vector3(0, 0, 0));
        handler.Execute(conn, new CPlayerInputPacket { Seq = 1, DirX = 1, DirZ = 0 });

        ch.Received(1).Velocity = Arg.Is<Vector3>(v =>
            Math.Abs(v.x - 5f) < 1e-3 && Math.Abs(v.y) < 1e-6 && Math.Abs(v.z) < 1e-6);
    }

    [Fact]
    public void Publish_zero_velocity_when_there_is_no_input()
    {
        var (handler, conn, ch, _) = Setup(speed: 5f);
        handler.Execute(conn, new CPlayerInputPacket { Seq = 1, DirX = 0, DirZ = 0 });

        ch.Received(1).Velocity = Vector3.zero;
    }

    /// <summary>Pressing into a wall: the character does not move, so it must not be extrapolated into the wall.</summary>
    [Fact]
    public void Publish_zero_velocity_when_the_navmesh_blocks_the_whole_step()
    {
        var (handler, conn, ch, nav) = Setup(speed: 5f, startPos: new Vector3(1, 0, 1));
        nav.RaycastWalkable(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns(new Vector3(1, 0, 1));

        handler.Execute(conn, new CPlayerInputPacket { Seq = 1, DirX = 1, DirZ = 0 });

        ch.Received(1).Velocity = Vector3.zero;
    }

    [Fact]
    public void Publish_the_clamped_step_as_velocity_when_partly_obstructed()
    {
        var (handler, conn, ch, nav) = Setup(speed: 5f, startPos: new Vector3(0, 0, 0));
        nav.RaycastWalkable(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns(new Vector3(0.05f, 0, 0));

        handler.Execute(conn, new CPlayerInputPacket { Seq = 1, DirX = 1, DirZ = 0 });

        // 0.05 m in one 1/60 s step is 3 m/s, not the 5 m/s the input asked for.
        ch.Received(1).Velocity = Arg.Is<Vector3>(v => Math.Abs(v.x - 0.05f / TickDt) < 1e-2 && Math.Abs(v.z) < 1e-6);
    }

    /// <summary>A ground-height change is a snap, not motion: velocity stays horizontal as before.</summary>
    [Fact]
    public void Keep_velocity_horizontal_when_the_ground_height_changes()
    {
        var (handler, conn, ch, nav) = Setup(speed: 5f);
        nav.SampleGroundHeight(Arg.Any<float>(), Arg.Any<float>(), Arg.Any<float>()).Returns(2.5f);

        handler.Execute(conn, new CPlayerInputPacket { Seq = 1, DirX = 1, DirZ = 0 });

        ch.Received(1).Velocity = Arg.Is<Vector3>(v => Math.Abs(v.y) < 1e-6 && Math.Abs(v.x - 5f) < 1e-3);
    }

    [Fact]
    public void Y_from_ground_sample_not_input()
    {
        var (handler, conn, ch, nav) = Setup(speed: 5f);
        nav.SampleGroundHeight(Arg.Any<float>(), Arg.Any<float>(), Arg.Any<float>()).Returns(2.5f);

        handler.Execute(conn, new CPlayerInputPacket { Seq = 1, DirX = 1, DirZ = 0 });

        ch.Received(1).Position = Arg.Is<Vector3>(p => Math.Abs(p.y - 2.5f) < 1e-4);
    }

    [Fact]
    public void Send_state_ack_when_input_accepted()
    {
        var (handler, conn, _, _) = Setup(speed: 5f);
        handler.Execute(conn, new CPlayerInputPacket { Seq = 42, DirX = 1, DirZ = 0, YawDeg = 90 });
        // Detailed packet inspection is awkward through NetworkPacket; assert Send was called once.
        conn.Received(1).Send(Arg.Any<global::Avalon.Network.Packets.Abstractions.NetworkPacket>());
    }

    [Fact]
    public void Advance_LastInputSeq_on_accepted_input()
    {
        var (handler, conn, _, _) = Setup();
        handler.Execute(conn, new CPlayerInputPacket { Seq = 7, DirX = 1, DirZ = 0 });
        conn.Received(1).LastInputSeq = 7u;
    }

    [Fact]
    public void Drop_input_packet_when_character_is_dead()
    {
        var character = Substitute.For<ICharacter>();
        character.IsDead.Returns(true);
        character.Position.Returns(Vector3.zero);
        character.InstanceId.Returns(Guid.NewGuid());

        var connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        connection.LastInputSeq.Returns(0u);

        var instance = Substitute.For<IMapInstance>();
        var nav = Substitute.For<IMapNavigator>();
        instance.GetNavigatorForPosition(Arg.Any<Vector3>()).Returns(nav);

        var world = Substitute.For<IWorld>();
        world.InstanceRegistry.GetInstanceById(Arg.Any<Guid>()).Returns(instance);

        var handler = new PlayerInputHandler(NullLogger<PlayerInputHandler>.Instance, world);

        handler.Execute(connection, new CPlayerInputPacket { Seq = 1, DirX = 1f, DirZ = 0f });

        // Dead char => no nav lookup, no position write.
        instance.DidNotReceive().GetNavigatorForPosition(Arg.Any<Vector3>());
        character.DidNotReceiveWithAnyArgs().Position = default;
    }
}
