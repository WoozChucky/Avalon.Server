using Avalon.Network.Packets.Abstractions;
using Avalon.World.Public;

namespace Avalon.World.Filters;

public class MapSessionFilter : PacketFilter
{
    public MapSessionFilter(IWorldConnection connection) : base(connection) { }
    
    public override bool Process(NetworkPacket packet)
    {
        throw new NotImplementedException();
    }

    public override bool CanProcess(NetworkPacketType type)
    {
        return type switch
        {
            NetworkPacketType.CMSG_MOVEMENT => true,
            NetworkPacketType.CMSG_ATTACK => true,
            _ => false
        };
    }
}
