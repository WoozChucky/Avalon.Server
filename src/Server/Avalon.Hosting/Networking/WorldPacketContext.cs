namespace Avalon.Hosting.Network;

public struct WorldPacketContext<TPacket>
{
    public TPacket Packet {get; set;}
    public IWorldConnection Connection {get; set;}
}