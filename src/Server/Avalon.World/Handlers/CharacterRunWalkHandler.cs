using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Movement;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_CHARACTER_RUN_WALK)]
public class CharacterRunWalkHandler : WorldPacketHandler<CCharacterRunWalkPacket>
{
    public override void Execute(WorldConnection connection, CCharacterRunWalkPacket packet)
    {
        connection.Character!.SetRunning(packet.Running);
    }
}
