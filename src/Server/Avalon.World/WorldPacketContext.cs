using Avalon.World.Public;

namespace Avalon.World;

public struct WorldPacketContext<TPacket>
{
    public TPacket Packet { get; set; }
    public IWorldConnection Connection { get; set; }
}
