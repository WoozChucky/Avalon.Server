using Avalon.World;

namespace Avalon.Server.World;

public struct WorldPacketContext<TPacket>
{
    public TPacket Packet {get; set;}
    public IWorldConnection Connection {get; set;}
}
