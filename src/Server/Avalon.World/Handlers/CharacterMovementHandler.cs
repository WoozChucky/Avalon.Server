using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Movement;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_MOVEMENT)]
public class CharacterMovementHandler(IWorld world) : WorldPacketHandler<CPlayerMovementPacket>
{
    public override void Execute(WorldConnection connection, CPlayerMovementPacket packet)
    {
        connection.Character!.Position = new Vector3(packet.X, packet.Y, packet.Z);
        connection.Character.Velocity = new Vector3(packet.VelocityX, packet.VelocityY, packet.VelocityZ);
        connection.Character.Orientation = new Vector3(0, packet.Rotation, 0);
        
        world.Grid.OnPlayerMoved(connection);
    }
}
