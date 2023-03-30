namespace Avalon.Network.Packets;

public class PacketHandlerRegistry : IPacketHandlerRegistry
{
    private readonly Dictionary<NetworkPacketType, Func<TcpClient, NetworkPacket, Task>> _packetHandlers;

    public PacketHandlerRegistry()
    {
        _packetHandlers = new Dictionary<NetworkPacketType, Func<TcpClient, NetworkPacket, Task>>();
    }
    
    public void RegisterHandler(NetworkPacketType packetType, Func<TcpClient, NetworkPacket, Task> handler)
    {
        if (_packetHandlers.ContainsKey(packetType))
        {
            throw new ApplicationException("Handler already registered for packet type: " + packetType + "");
        }
        
        _packetHandlers.Add(packetType, handler);
    }
    
    public Func<TcpClient, NetworkPacket, Task> GetHandler(NetworkPacketType type)
    {
        if (!_packetHandlers.ContainsKey(type))
        {
            throw new ApplicationException("No handler registered for packet type: " + type + "");
        }
        return _packetHandlers[type];
    }
}
