namespace Avalon.Hosting.Networking;

public struct WorldPacketContext<TPacket>
{
    public TPacket Packet {get; set;}
    public IWorldConnection Connection {get; set;}
}