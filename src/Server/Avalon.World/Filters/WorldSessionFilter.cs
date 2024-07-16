using Avalon.Network.Packets.Abstractions;
using Avalon.World.Public;

namespace Avalon.World.Filters;

public class WorldSessionFilter : PacketFilter
{
    private readonly IWorldConnection _connection;
    public WorldSessionFilter(IWorldConnection connection) : base(connection)
    {
        _connection = connection;
    }
    
    public override bool Process(NetworkPacket packet)
    {
        throw new NotImplementedException();
    }

    public override bool CanProcess(NetworkPacketType type)
    {
        if (_connection.Character != null) return false;
        
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
