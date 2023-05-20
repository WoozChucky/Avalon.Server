using Avalon.Network.Packets.Exceptions;

namespace Avalon.Network.Packets;



public class PacketHandlerRegistry : IPacketHandlerRegistry
{
    private readonly Dictionary<NetworkPacketType, PacketHandler<IRemoteSource>> _packetHandlers;
    
    public PacketHandlerRegistry()
    {
        _packetHandlers = new Dictionary<NetworkPacketType, PacketHandler<IRemoteSource>>();
    }
    
    public void RegisterHandler(NetworkPacketType packetType, PacketHandler<IRemoteSource> handler)
    {
        if (_packetHandlers.ContainsKey(packetType))
        {
            throw new PacketHandlerException("Handler already registered for packet type: " + packetType + "");
        }
        
        _packetHandlers.Add(packetType, handler);
    }

    public PacketHandler<IRemoteSource> GetHandler(NetworkPacketType type)
    {
        if (!_packetHandlers.ContainsKey(type))
        {
            throw new PacketHandlerException("No handler registered for packet type: " + type + "");
        }
        return _packetHandlers[type];
    }
}
