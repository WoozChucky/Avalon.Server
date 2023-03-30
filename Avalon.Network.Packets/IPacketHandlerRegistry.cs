namespace Avalon.Network.Packets;

public interface IPacketHandlerRegistry
{
    void RegisterHandler(NetworkPacketType packetType, Func<TcpClient, NetworkPacket, Task> handler);
    
    Func<TcpClient, NetworkPacket, Task> GetHandler(NetworkPacketType type);
}
