using Avalon.Network.Packets.Abstractions;
using Avalon.World.Public;

namespace Avalon.World.Filters;

public class MapSessionFilter : PacketFilter
{
    private readonly IWorldConnection _connection;
    public MapSessionFilter(IWorldConnection connection) : base(connection)
    {
        _connection = connection;
    }

    public override bool Process(NetworkPacket packet)
    {
        throw new NotImplementedException();
    }

    public override bool CanProcess(NetworkPacketType type)
    {
        if (_connection.Character == null) return false;
        if (_connection.Character.Map < 1) return false;

        return type switch
        {
            NetworkPacketType.CMSG_MOVEMENT => true,
            NetworkPacketType.CMSG_ATTACK => true,
            NetworkPacketType.CMSG_CHARACTER_RUN_WALK => true,
            _ => false
        };
    }
}
