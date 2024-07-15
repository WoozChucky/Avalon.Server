using Avalon.Network.Packets.Abstractions;
using Avalon.World.Public;

namespace Avalon.World.Filters;

public class WorldSessionFilter : PacketFilter
{
    public WorldSessionFilter(IWorldConnection connection) : base(connection) { }
    
    public override bool Process(NetworkPacket packet)
    {
        throw new NotImplementedException();
    }

    public override bool CanProcess(NetworkPacketType type)
    {
        return type switch
        {
            NetworkPacketType.CMSG_CHARACTER_LIST => true,
            NetworkPacketType.CMSG_CHARACTER_CREATE => true,
            NetworkPacketType.CMSG_CHARACTER_DELETE => true,
            
            NetworkPacketType.CMSG_CHARACTER_SELECTED => true, // requires confirmation...
            _ => false
        };
    }
}
