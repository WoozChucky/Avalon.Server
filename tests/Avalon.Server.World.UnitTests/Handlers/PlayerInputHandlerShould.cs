using System.Diagnostics.Metrics;
using Avalon.Common.Mathematics;
using Avalon.Common.Telemetry;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Movement;
using Avalon.Server.World.UnitTests.Auras;
using Avalon.Server.World.UnitTests.Creatures;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World;
using Avalon.World.Auras;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Maps.Navigation;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Handlers;

public class PlayerInputHandlerShould
{
    private const float TickDt = 1f / 60f;

    [Theory]
    [InlineData(1f, 1u, 0.05f, false)]
    [InlineData(1f, 1u, 0f, true)]
    [InlineData(0f, 1u, 0f, true)]
    [InlineData(1f, 0u, 0.05f, true)]
    public void End_arrival_protection_only_when_accepted_input_actually_moves_the_character(
        float direction, uint sequence, float allowedX, bool protectedAfter)
    {
        (PlayerInputHandler handler, IWorldConnection conn, ICharacter placeholder, IMapNavigator nav) = Setup();
        CharacterEntity character = TestCharacters.New();
        character.InstanceId = placeholder.InstanceId;
        conn.Character.Returns(character);
        nav.RaycastWalkable(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns(new Vector3(allowedX, 0, 0));
        conn.When(c => c.Send(Arg.Any<global::Avalon.Network.Packets.Abstractions.OutboundPacket>()))
            .Do(call => call.Arg<global::Avalon.Network.Packets.Abstractions.OutboundPacket>().Release());
        ArrivalProtectionTestData.Apply(character);

        handler.Execute(conn, new CPlayerInputPacket { Seq = sequence, DirX = direction, YawDeg = 90 });

        Assert.Equal(protectedAfter, ArrivalProtection.IsProtected(character));
        Assert.Equal(sequence > 0 ? allowedX : 0f, character.Position.x);
    }

    [Theory]
    [InlineData(1f, 0f, 0f, false)]
    [InlineData(0f, 0f, 1f, false)]
    [InlineData(0f, 1f, 0f, true)]
    [InlineData(0f, 0f, 0f, true)]
    public void End_arrival_protection_for_horizontal_relocation_but_keep_it_for_grounding(
        float x, float y, float z, bool protectedAfter)
    {
        CharacterEntity character = TestCharacters.New();
        ArrivalProtectionTestData.Apply(character);

        // Relocation through ICharacter is also how movement abilities change position.
        ((ICharacter)character).Position = new Vector3(x, y, z);

        Assert.Equal(protectedAfter, ArrivalProtection.IsProtected(character));
    }

    private static (PlayerInputHandler handler, IWorldConnection conn, ICharacter ch, IMapNavigator nav) Setup(
        float speed = 5f, Vector3 startPos = default, IMapNavigator? navigator = null, FakeTimeProvider? clock = null,
        ILogger<PlayerInputHandler>? logger = null)
    {
        var instanceId = Guid.NewGuid();

        ICharacter ch = Substitute.For<ICharacter>();
        ch.Position.Returns(startPos);
        ch.GetMovementSpeed().Returns(speed);
        ch.InstanceId.Returns(instanceId);

        IWorldConnection conn = Substitute.For<IWorldConnection>();
        conn.Character.Returns(ch);
        conn.LastInputSeq.Returns(0u);

        IMapNavigator nav = navigator ?? Substitute.For<IMapNavigator>();
        if (navigator is null)
        {
            // Default: clear path — RaycastWalkable returns whatever the desired endpoint was.
            nav.RaycastWalkable(Arg.Any<Vector3>(), Arg.Any<Vector3>())
                .Returns(call => call.ArgAt<Vector3>(1));   // returns 'to'
            nav.SampleGroundHeight(Arg.Any<float>(), Arg.Any<float>(), Arg.Any<float>()).Returns(0f);
        }

        IMapInstance instance = Substitute.For<IMapInstance>();
        instance.GetNavigatorForPosition(Arg.Any<Vector3>()).Returns(nav);

        IInstanceRegistry registry = Substitute.For<IInstanceRegistry>();
        registry.GetInstanceById(instanceId).Returns(instance);

        IWorld world = Substitute.For<IWorld>();
        world.InstanceRegistry.Returns(registry);

        var handler = new PlayerInputHandler(logger ?? NullLogger<PlayerInputHandler>.Instance, world,
            clock ?? new FakeTimeProvider(), Options.Create(new GameConfiguration()));
        return (handler, conn, ch, nav);
    }

    /// <summary>
    /// #1267: each input that may move spends one step of the character's movement budget, which server time refills at
    /// one step per 1/60 s (plus the 0.5 % clock drift allowance) and holds at most 15 (Game:Movement:InputCatchUp,
    /// 250 ms). One over it is answered standing: its sequence advances and its ack carries the unchanged position and
    /// zero velocity. Each row is a list of (advance in microseconds, then a burst of inputs) pairs, run
    /// <paramref name="repeat" /> times, after <paramref name="heldFirst" /> inputs sent stunned (held, spending nothing).
    /// The budget starts full; a burst past it moves 15; one step's time earns one more; ten idle seconds refill it to 15,
    /// never more; and stunned inputs leave the budget whole for after the stun.
    /// </summary>
    [Theory]
    [InlineData(new[] { 0, 15 }, 1, 0, 15, 0)]
    [InlineData(new[] { 0, 16 }, 1, 0, 15, 1)]
    [InlineData(new[] { 0, 40, 16_667, 2 }, 1, 0, 16, 26)]
    [InlineData(new[] { 0, 15, 10_000_000, 40 }, 1, 0, 30, 25)]
    [InlineData(new[] { 0, 15 }, 1, 30, 15, 30)]
    public void Answer_moving_inputs_beyond_the_time_budget_standing(
        int[] steps, int repeat, int heldFirst, int movedExpected, int standingExpected)
    {
        var clock = new FakeTimeProvider();
        (PlayerInputHandler handler, IWorldConnection conn, ICharacter placeholder, IMapNavigator _) = Setup(clock: clock);
        CharacterEntity character = TestCharacters.New(time: clock);
        character.InstanceId = placeholder.InstanceId;
        conn.Character.Returns(character);
        List<SPlayerStateAckPacket> acks = RecordAcks(conn);
        var packet = new CPlayerInputPacket { DirX = 1, YawDeg = 90 };

        if (heldFirst > 0)
        {
            ActiveAura stun = AuraTestData.HeldControl(CrowdControl.Stun);
            character.Auras.Add(stun, clock.GetUtcNow());
            for (int i = 0; i < heldFirst; i++)
                Send(handler, conn, packet);
            character.Auras.Remove(stun, clock.GetUtcNow());
        }

        for (int r = 0; r < repeat; r++)
        {
            for (int s = 0; s < steps.Length; s += 2)
            {
                clock.Advance(TimeSpan.FromMicroseconds(steps[s]));
                for (int i = 0; i < steps[s + 1]; i++)
                    Send(handler, conn, packet);
            }
        }

        int moved = 0, standing = 0;
        var before = new Vector3(0, 0, 0);
        foreach (SPlayerStateAckPacket ack in acks)
        {
            var at = new Vector3(ack.X, ack.Y, ack.Z);
            if (at.x != before.x || at.z != before.z)
            {
                moved++;
            }
            else
            {
                Assert.Equal((0f, 0f), (ack.VelX, ack.VelZ));
                standing++;
            }

            before = at;
        }

        Assert.Equal((movedExpected, standingExpected), (moved, standing));
        Assert.Equal(Enumerable.Range(1, acks.Count).Select(n => (uint)n), acks.Select(a => a.Seq));
        Assert.Equal(packet.Seq, conn.LastInputSeq);
    }

    /// <summary>
    /// #1267: a client sends one input per 1/60 s of its own clock, which runs at its own rate. The budget refills
    /// 0.5 % faster than server time (Game:Movement:ClockDriftAllowancePercent), so a client whose clock runs a little
    /// fast, 50 parts per million here, is never answered standing however long it plays (two hours of server time),
    /// and neither is one whose inputs arrive with jitter around an exact 1/60 s. One sending 1 % fast, past the
    /// allowance, moves at the 0.5 % ceiling: of its first 6,000 inputs, 15 + 5,999 x 0.99 x 1.005 move and the rest
    /// stand. Input <c>i</c> arrives at <c>i x interval</c> of server time, plus or minus <paramref name="jitterMs" />
    /// alternately, where the interval is 1/60 s x (1 - <paramref name="fast" />).
    /// </summary>
    [Theory]
    [InlineData(0d, 2d, 432_000, 0)]
    [InlineData(50e-6, 0d, 432_000, 0)]
    [InlineData(0.01, 0d, 6_000, 17)]
    public void Keep_a_client_moving_at_its_own_clock_within_the_drift_allowance(
        double fast, double jitterMs, int inputs, int standingExpected)
    {
        var clock = new FakeTimeProvider();
        CharacterEntity character = TestCharacters.New(time: clock);
        // Its own substitutes, cleared as it goes, and a plain connection (a substitute's property set 432,000 times
        // slows every read of it): the long rows make millions of calls.
        IMapNavigator nav = Substitute.For<IMapNavigator>();
        nav.RaycastWalkable(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns(call => call.ArgAt<Vector3>(1));
        IMapInstance instance = Substitute.For<IMapInstance>();
        instance.GetNavigatorForPosition(Arg.Any<Vector3>()).Returns(nav);
        IWorld world = Substitute.For<IWorld>();
        world.InstanceRegistry.GetInstanceById(character.InstanceId).Returns(instance);
        var conn = new QuietConnection(character);
        var handler = new PlayerInputHandler(NullLogger<PlayerInputHandler>.Instance, world, clock,
            Options.Create(new GameConfiguration()));
        var packet = new CPlayerInputPacket { YawDeg = 90 };
        double interval = TimeSpan.TicksPerSecond / 60d * (1d - fast);
        long start = clock.GetUtcNow().UtcTicks;

        int standing = 0;
        for (int i = 1; i <= inputs; i++)
        {
            double jitter = (i % 2 == 0 ? jitterMs : -jitterMs) * TimeSpan.TicksPerMillisecond;
            clock.SetUtcNow(new DateTimeOffset(start + (long)Math.Round(i * interval + jitter), TimeSpan.Zero));
            packet.DirX = i % 2 == 0 ? 1 : -1; // back and forth, so the position stays small and exact
            Vector3 before = character.Position;
            Send(handler, conn, packet);
            if (character.Position == before)
            {
                Assert.Equal(Vector3.zero, character.Velocity);
                standing++;
            }

            if (i % 10_000 == 0)
            {
                world.ClearReceivedCalls();
                world.InstanceRegistry.ClearReceivedCalls();
                instance.ClearReceivedCalls();
                nav.ClearReceivedCalls();
            }
        }

        Assert.Equal(standingExpected, standing);
        Assert.Equal(packet.Seq, conn.LastInputSeq);
    }

    /// <summary>
    /// #1267: an input over the budget takes no step, so the arrival protection a character entered with stays, as it
    /// does for a held one.
    /// </summary>
    [Fact]
    public void Keep_arrival_protection_on_an_input_over_the_budget()
    {
        var clock = new FakeTimeProvider();
        (PlayerInputHandler handler, IWorldConnection conn, ICharacter placeholder, IMapNavigator _) = Setup(clock: clock);
        CharacterEntity character = TestCharacters.New(time: clock);
        character.InstanceId = placeholder.InstanceId;
        conn.Character.Returns(character);
        RecordAcks(conn);
        var packet = new CPlayerInputPacket { DirX = 1, YawDeg = 90 };
        for (int i = 0; i < 15; i++)
            Send(handler, conn, packet);
        Vector3 spent = character.Position;
        ArrivalProtectionTestData.Apply(character);

        Send(handler, conn, packet);

        Assert.True(ArrivalProtection.IsProtected(character));
        Assert.Equal(spent, character.Position);
    }

    /// <summary>
    /// #1267: an input over the budget is counted every time in <c>world.movement.inputs_over_budget</c> and logged once
    /// per character, a Warning; the connection is not closed.
    /// </summary>
    [Fact]
    public void Log_once_and_count_every_input_over_the_budget()
    {
        var log = new TestLog();
        (PlayerInputHandler handler, IWorldConnection conn, ICharacter placeholder, IMapNavigator _) =
            Setup(logger: new Logger<PlayerInputHandler>(log));
        CharacterEntity character = TestCharacters.New();
        character.InstanceId = placeholder.InstanceId;
        conn.Character.Returns(character);
        RecordAcks(conn);
        using var counted = new OverBudgetProbe();
        var packet = new CPlayerInputPacket { DirX = 1, YawDeg = 90 };

        for (int i = 0; i < 18; i++)
            Send(handler, conn, packet);

        Assert.Single(log.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Equal(3, counted.Total);
        conn.DidNotReceiveWithAnyArgs().Close();
    }

    private static void Send(PlayerInputHandler handler, IWorldConnection conn, CPlayerInputPacket packet)
    {
        packet.Seq++;
        handler.Execute(conn, packet);
    }

    /// <summary>Every ack the connection is sent, read and its pooled payload released.</summary>
    private static List<SPlayerStateAckPacket> RecordAcks(IWorldConnection conn)
    {
        var acks = new List<SPlayerStateAckPacket>();
        conn.When(c => c.Send(Arg.Any<OutboundPacket>())).Do(call =>
        {
            OutboundPacket sent = call.Arg<OutboundPacket>();
            acks.AddRange(TestTown.Read<SPlayerStateAckPacket>([sent], NetworkPacketType.SMSG_PLAYER_STATE_ACK));
            sent.Release();
        });
        return acks;
    }

    /// <summary>
    /// The inputs counted in <c>world.movement.inputs_over_budget</c> on this test's own flow: the counter is static and
    /// shared, and other tests move characters in parallel.
    /// </summary>
    private sealed class OverBudgetProbe : IDisposable
    {
        private static readonly AsyncLocal<OverBudgetProbe?> s_current = new();
        private readonly MeterListener _listener = new();

        public OverBudgetProbe()
        {
            s_current.Value = this;
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter == DiagnosticsConfig.World.Meter && instrument.Name == "world.movement.inputs_over_budget")
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((_, value, _, _) =>
            {
                if (s_current.Value == this)
                    Total += value;
            });
            _listener.Start();
        }

        public long Total { get; private set; }

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>
    /// An accepted input steps the character speed x 1/60 s along its direction, clamped to unit length, and puts it
    /// at the ground height the navmesh samples. Velocity is metres per second and horizontal (#424): other clients
    /// extrapolate the character as position + Velocity x seconds, and a ground-height change is a snap, not motion.
    /// </summary>
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1f, 5f)]
    [InlineData(10f, 5f)]
    public void Step_along_the_input_onto_the_ground_and_publish_the_velocity(float dirX, float expectedSpeed)
    {
        (PlayerInputHandler? handler, IWorldConnection? conn, ICharacter? ch, IMapNavigator? nav) = Setup(speed: 5f);
        nav.SampleGroundHeight(Arg.Any<float>(), Arg.Any<float>(), Arg.Any<float>()).Returns(2.5f);

        handler.Execute(conn, new CPlayerInputPacket { Seq = 1, DirX = dirX, DirZ = 0, YawDeg = 90 });

        ch.Received(1).Position = Arg.Is<Vector3>(p =>
            Math.Abs(p.x - (expectedSpeed * TickDt)) < 1e-4 && Math.Abs(p.y - 2.5f) < 1e-4 && Math.Abs(p.z) < 1e-4);
        ch.Received(1).Velocity = Arg.Is<Vector3>(v =>
            Math.Abs(v.x - expectedSpeed) < 1e-3 && Math.Abs(v.y) < 1e-6 && Math.Abs(v.z) < 1e-6);
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

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1f, 5f)]
    public void Snap_and_acknowledge_input_through_the_real_navigator(float direction, float expectedSpeed)
    {
        var nav = new MapNavigator(NullLoggerFactory.Instance);
        nav.LoadFromNavMesh(CrowdLocomotionShould.s_flatNavMesh.Value);
        (PlayerInputHandler handler, IWorldConnection conn, ICharacter ch, IMapNavigator _) =
            Setup(startPos: new Vector3(0, 0.5f, 0), navigator: nav);
        // This test does not retain the ACK's pooled payload.
        conn.When(c => c.Send(Arg.Any<global::Avalon.Network.Packets.Abstractions.OutboundPacket>()))
            .Do(call => call.Arg<global::Avalon.Network.Packets.Abstractions.OutboundPacket>().Release());

        handler.Execute(conn, new CPlayerInputPacket { Seq = 7, DirX = direction, YawDeg = 90 });

        ch.Received(1).Position = Arg.Is<Vector3>(p =>
            Math.Abs(p.x - expectedSpeed * TickDt) < 1e-4 && Math.Abs(p.y - 0.2f) < 1e-4 && p.z == 0);
        ch.Received(1).Velocity = Arg.Is<Vector3>(v => Math.Abs(v.x - expectedSpeed) < 1e-3 && v.y == 0 && v.z == 0);
        ch.Received(1).Orientation = new Vector3(0, 90, 0);
        conn.Received(1).LastInputSeq = 7u;
        conn.Received(1).Send(Arg.Any<global::Avalon.Network.Packets.Abstractions.OutboundPacket>());
    }

    /// <summary>An accepted input is acknowledged with the authoritative state and advances the last sequence seen.</summary>
    [Fact]
    public void Acknowledge_an_accepted_input_and_advance_the_sequence()
    {
        (PlayerInputHandler? handler, IWorldConnection? conn, ICharacter _, IMapNavigator _) = Setup(speed: 5f);

        handler.Execute(conn, new CPlayerInputPacket { Seq = 7, DirX = 1, DirZ = 0, YawDeg = 90 });

        conn.Received(1).Send(Arg.Any<global::Avalon.Network.Packets.Abstractions.OutboundPacket>());
        conn.Received(1).LastInputSeq = 7u;
    }

    /// <summary>
    /// A stunned or rooted character takes no step (#770), but its input is still taken: the sequence advances and the
    /// ack carries the unchanged position and zero velocity, so the client reconciles without drift, and the velocity
    /// it had stops replicating. A rooted character still turns to the input's yaw; a stunned one keeps its own.
    /// </summary>
    [Theory]
    [InlineData(CrowdControl.Stun, 30)]
    [InlineData(CrowdControl.Root, 90)]
    public void Hold_a_stunned_or_rooted_character_in_place_and_ack_it(CrowdControl control, ushort yawAfter)
    {
        (PlayerInputHandler handler, IWorldConnection conn, ICharacter placeholder, IMapNavigator _) = Setup();
        CharacterEntity character = TestCharacters.New();
        character.InstanceId = placeholder.InstanceId;
        var start = new Vector3(1f, 0.5f, 2f);
        character.Position = start;
        character.Velocity = new Vector3(5f, 0f, 0f);
        character.Orientation = new Vector3(0f, 30f, 0f);
        character.Auras.Add(AuraTestData.HeldControl(control), DateTimeOffset.UnixEpoch);
        conn.Character.Returns(character);
        var sent = new List<OutboundPacket>();
        conn.When(c => c.Send(Arg.Any<OutboundPacket>())).Do(call => sent.Add(call.Arg<OutboundPacket>()));

        handler.Execute(conn, new CPlayerInputPacket { Seq = 7, DirX = 1, DirZ = 0, YawDeg = 90 });

        Assert.Equal((start, Vector3.zero, yawAfter),
            (character.Position, character.Velocity, (ushort)character.Orientation.y));
        conn.Received(1).LastInputSeq = 7u;
        SPlayerStateAckPacket ack =
            Assert.Single(TestTown.Read<SPlayerStateAckPacket>(sent, NetworkPacketType.SMSG_PLAYER_STATE_ACK));
        Assert.Equal((7u, start.x, start.y, start.z, 0f, 0f, yawAfter),
            (ack.Seq, ack.X, ack.Y, ack.Z, ack.VelX, ack.VelZ, ack.YawDeg));
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
        conn.DidNotReceive().Send(Arg.Any<global::Avalon.Network.Packets.Abstractions.OutboundPacket>());
    }
}
