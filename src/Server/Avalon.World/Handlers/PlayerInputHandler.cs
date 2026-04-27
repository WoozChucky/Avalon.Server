using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Movement;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_PLAYER_INPUT)]
public class PlayerInputHandler(
    ILogger<PlayerInputHandler> logger,
    IWorld world)
    : WorldPacketHandler<CPlayerInputPacket>
{
    private const float TickDt = 1f / 60f;
    private static int _debugCounter;

    public override void Execute(IWorldConnection connection, CPlayerInputPacket packet)
    {
        var ch = connection.Character;
        if (ch == null) return;

        // Defensive: reject duplicate / out-of-order seq. TCP guarantees order so this should never fire.
        if (packet.Seq <= connection.LastInputSeq) return;

        // Resolve the per-instance navmesh navigator. IMapNavigator is not a global DI service —
        // each MapInstance owns its own (per-region) navigators. Look up via the instance registry.
        var instance = world.InstanceRegistry.GetInstanceById(ch.InstanceId);
        if (instance == null)
        {
            logger.LogError("[NavDebug] PlayerInputHandler: instance lookup failed for InstanceId={InstanceId}", ch.InstanceId);
            return;
        }
        var navigator = instance.GetNavigatorForPosition(ch.Position);

        // TEMP DEBUG: throttled entry log — every 60 inputs (~1s) confirms handler is being hit
        // and which navigator the lookup returned.
        if ((_debugCounter++ % 60) == 0)
        {
            logger.LogError("[NavDebug] handler tick: pos={Pos} navigator.Mesh={Mesh} navigator.Type={Type}",
                ch.Position, navigator.Mesh != null ? "loaded" : "NULL", navigator.GetType().Name);
        }

        // Clamp direction magnitude to unit length.
        var dir = new Vector3(packet.DirX, 0f, packet.DirZ);
        var sqMag = dir.x * dir.x + dir.z * dir.z;
        if (sqMag > 1f)
        {
            var inv = 1f / MathF.Sqrt(sqMag);
            dir = new Vector3(dir.x * inv, 0f, dir.z * inv);
        }

        var speed = ch.GetMovementSpeed();
        var step = dir * speed * TickDt;
        var desired = new Vector3(ch.Position.x + step.x, ch.Position.y, ch.Position.z + step.z);

        // Horizontal collision via navmesh raycast.
        var clamped = navigator.RaycastWalkable(ch.Position, desired);
        // Y from ground sample — pass current Y so the navmesh search box finds the correct
        // floor on multi-storey maps. Returns ch.Position.y unchanged on lookup failure.
        var groundY = navigator.SampleGroundHeight(clamped.x, ch.Position.y, clamped.z);
        var newPosition = new Vector3(clamped.x, groundY, clamped.z);

        // Setting these properties marks the dirty fields automatically on CharacterEntity.
        ch.Position = newPosition;
        ch.Velocity = new Vector3(dir.x * speed, 0f, dir.z * speed);
        ch.Orientation = new Vector3(0f, packet.YawDeg, 0f);

        connection.LastInputSeq = packet.Seq;

        // Reply with authoritative state, tagged with the seq we just processed.
        connection.Send(SPlayerStateAckPacket.Create(
            packet.Seq,
            newPosition.x, newPosition.y, newPosition.z,
            ch.Velocity.x, ch.Velocity.z,
            packet.YawDeg,
            connection.CryptoSession.Encrypt));
    }
}
