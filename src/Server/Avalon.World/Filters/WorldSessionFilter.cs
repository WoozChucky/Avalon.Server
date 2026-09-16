using Avalon.Network.Packets.Abstractions;
using Avalon.World.Public;

namespace Avalon.World.Filters;

public class WorldSessionFilter(IWorldConnection connection) : PacketFilter
{
    public override bool Process(NetworkPacket packet) => throw new NotImplementedException();

    public override bool CanProcess(NetworkPacketType type)
    {
        // Pong is always valid regardless of character state
        if (type == NetworkPacketType.CMSG_PONG)
        {
            return true;
        }

        // The load report releases the readiness barrier, so it arrives while the character is
        // still pending and Character is null. Accepted whatever the character state, not only
        // while it is null: the barrier can expire and spawn between the packet being queued and
        // the pass that dispatches it, and a packet neither filter will take stays at the head of
        // the queue and blocks every packet behind it.
        if (type == NetworkPacketType.CMSG_CHARACTER_LOADED)
        {
            return true;
        }

        if (connection.Character != null)
        {
            return false;
        }

        return type switch
        {
            NetworkPacketType.CMSG_CHARACTER_LIST => true,
            NetworkPacketType.CMSG_CHARACTER_CREATE => true,
            NetworkPacketType.CMSG_CHARACTER_DELETE => true,
            NetworkPacketType.CMSG_CHARACTER_SELECTED => true,
            _ => false
        };
    }
}
