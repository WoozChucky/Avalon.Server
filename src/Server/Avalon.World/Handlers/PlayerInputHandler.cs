using System.Diagnostics.Metrics;
using Avalon.Common.Mathematics;
using Avalon.Common.Telemetry;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.Serialization;
using Avalon.World.Auras;
using Avalon.World.Characters;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Maps.Navigation;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_PLAYER_INPUT)]
public class PlayerInputHandler(
    ILogger<PlayerInputHandler> logger,
    IWorld world,
    TimeProvider clock,
    IOptions<GameConfiguration> game)
    : WorldPacketHandler<CPlayerInputPacket>
{
    private const float TickDt = MovementBudget.StepSeconds;

    private static readonly Counter<long> s_overBudget = DiagnosticsConfig.World.Meter.CreateCounter<long>(
        "world.movement.inputs_over_budget", "{inputs}",
        "Player inputs over the movement budget (#1267): sent faster than server time allows, answered standing");

    // The steps a character's movement budget holds, from Game:Movement:InputCatchUp, read once.
    private readonly int _budgetCap = MovementBudget.CapFor(game.Value.Movement.InputCatchUp);

    // The steps a second of server time earns, from Game:Movement:ClockDriftAllowancePercent, read once.
    private readonly double _refillRate = MovementBudget.RefillRateFor(game.Value.Movement.ClockDriftAllowancePercent);

    public override void Execute(IWorldConnection connection, CPlayerInputPacket packet)
    {
        ICharacter? ch = connection.Character;
        if (ch == null) return;

        if (ch.IsDead)
        {
            HighRatePacketLog.DroppedFromDeadCharacter(logger, NetworkPacketType.CMSG_PLAYER_INPUT, connection.Id, ch.Name);
            return;
        }

        // Defensive: reject duplicate / out-of-order seq. TCP guarantees order so this should never fire.
        if (packet.Seq <= connection.LastInputSeq) return;

        // #770: stunned or rooted, the input is taken but no step is: its sequence advances and the ack carries the
        // position unchanged and zero velocity, so the client reconciles without drifting and the velocity it had
        // stops replicating. A rooted character still turns to the input's yaw; a stunned one keeps its own. Field
        // reads only.
        if (!ControlGates.CanMove(ch))
        {
            Hold(connection, ch, packet);
            return;
        }

        // #1267: every input that may move spends one step of the character's movement budget, which server time
        // refills (plus the clock drift allowance), so a client sending inputs faster than time passes moves no faster
        // than one sending at the tick rate. One over the budget is answered standing, as a held one is, and keeps
        // arrival protection, since it takes no step. Counted every time, logged once per character. The time is the
        // tick's, read once per tick by the lease check; a connection that is not a WorldConnection (a scenario's, a
        // test's) reads the clock. A character that is not an entity (a test double) is not budgeted.
        if (ch is CharacterEntity entity && !entity.Movement.TrySpend(
                connection is WorldConnection ticked ? ticked.TickTimestamp(clock) : clock.GetTimestamp(),
                clock, _budgetCap, _refillRate))
        {
            s_overBudget.Add(1);
            if (!entity.Movement.Warned)
            {
                entity.Movement.Warned = true;
                HighRatePacketLog.MovementOverBudget(logger, entity.Name, connection.Id);
            }

            Hold(connection, ch, packet);
            return;
        }

        // Resolve the per-instance navmesh navigator. IMapNavigator is not a global DI service —
        // each MapInstance owns its own (per-region) navigators. Look up via the instance registry.
        IMapInstance? instance = world.InstanceRegistry.GetInstanceById(ch.InstanceId);
        if (instance == null)
        {
            HighRatePacketLog.InstanceNotFound(logger, NetworkPacketType.CMSG_PLAYER_INPUT, connection.Id, ch.InstanceId);
            return;
        }
        IMapNavigator navigator = instance.GetNavigatorForPosition(ch.Position);

        // Clamp direction magnitude to unit length.
        var dir = new Vector3(packet.DirX, 0f, packet.DirZ);
        float sqMag = dir.x * dir.x + dir.z * dir.z;
        if (sqMag > 1f)
        {
            float inv = 1f / MathF.Sqrt(sqMag);
            dir = new Vector3(dir.x * inv, 0f, dir.z * inv);
        }

        float speed = ch.GetMovementSpeed();
        Vector3 step = dir * speed * TickDt;
        var desired = new Vector3(ch.Position.x + step.x, ch.Position.y, ch.Position.z + step.z);

        Vector3 newPosition;
        if (navigator is MapNavigator mapNavigator)
        {
            newPosition = mapNavigator.MoveOnGround(ch.Position, desired);
        }
        else
        {
            // Other IMapNavigator implementations retain the separate queries. Current Y selects
            // the correct floor and is retained when the ground lookup fails.
            Vector3 clamped = navigator.RaycastWalkable(ch.Position, desired);
            float groundY = navigator.SampleGroundHeight(clamped.x, ch.Position.y, clamped.z);
            newPosition = new Vector3(clamped.x, groundY, clamped.z);
        }

        // Metres per second, and the step actually taken rather than the one asked for (#424): other
        // clients extrapolate this character as position + Velocity * seconds, so a character pressed
        // into a wall must publish zero, not full speed into the wall. Horizontal only, as before: the
        // ground snap is not motion to extrapolate.
        var velocity = new Vector3((newPosition.x - ch.Position.x) / TickDt, 0f, (newPosition.z - ch.Position.z) / TickDt);

        // Setting these properties marks the dirty fields automatically on CharacterEntity.
        ch.Position = newPosition;
        ch.Velocity = velocity;
        ch.Orientation = new Vector3(0f, packet.YawDeg, 0f);

        connection.LastInputSeq = packet.Seq;

        // Reply with authoritative state, tagged with the seq we just processed.
        connection.Send(SPlayerStateAckPacket.Create(
            packet.Seq,
            newPosition.x, newPosition.y, newPosition.z,
            ch.Velocity.x, ch.Velocity.z,
            packet.YawDeg,
            PacketEncoder.Shared));
    }

    /// <summary>A held character's input (#770): no step, no velocity, its yaw only if it may turn, the ack.</summary>
    private static void Hold(IWorldConnection connection, ICharacter ch, CPlayerInputPacket packet)
    {
        bool turns = ControlGates.CanTurn(ch);
        ch.Velocity = Vector3.zero;
        if (turns)
            ch.Orientation = new Vector3(0f, packet.YawDeg, 0f);

        connection.LastInputSeq = packet.Seq;

        Vector3 at = ch.Position;
        connection.Send(SPlayerStateAckPacket.Create(
            packet.Seq,
            at.x, at.y, at.z,
            0f, 0f,
            turns ? packet.YawDeg : (ushort)ch.Orientation.y,
            PacketEncoder.Shared));
    }
}
