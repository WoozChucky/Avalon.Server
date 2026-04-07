using Avalon.Network.Packets.Abstractions;
using Avalon.World.Public;

namespace Avalon.World.Filters;

public class MapSessionFilter(IWorldConnection connection) : PacketFilter
{
    public override bool Process(NetworkPacket packet) => throw new NotImplementedException();

    public override bool CanProcess(NetworkPacketType type)
    {
        if (connection.Character == null)
        {
            return false;
        }

        if (connection.Character.Map < 1)
        {
            return false;
        }

        return type switch
        {
            NetworkPacketType.CMSG_MOVEMENT => true,
            NetworkPacketType.CMSG_ATTACK => true,
            NetworkPacketType.CMSG_CHARACTER_RUN_WALK => true,
            NetworkPacketType.CMSG_ENTER_MAP => true,
            NetworkPacketType.CMSG_CHAT_MESSAGE => true,
            _ => false
        };
    }
}
