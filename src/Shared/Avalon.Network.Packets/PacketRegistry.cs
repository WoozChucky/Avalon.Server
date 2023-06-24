using Avalon.Network.Abstractions;
using Avalon.Network.Packets.Exceptions;

namespace Avalon.Network.Packets;

public class PacketRegistry : IPacketRegistry
{
    private readonly Dictionary<NetworkPacketType, IPacketHandler> _handlers;
    
    public PacketRegistry()
    {
        _handlers = new Dictionary<NetworkPacketType, IPacketHandler>();
    }
    
    public void RegisterHandler<T>(NetworkPacketType packetType, PacketHandler<IRemoteSource, T> handler) where T : Packet
    {
        if (_handlers.ContainsKey(packetType))
        {
            throw new PacketHandlerException("Handler already registered for packet type: " + packetType + "");
        }
        
        _handlers.Add(packetType, new PacketHandlerWrapper<T>(handler));
    }

    public IPacketHandler GetHandler(NetworkPacketType type)
    {
        if (!_handlers.ContainsKey(type))
        {
            throw new PacketHandlerException("No handler registered for packet type: " + type + "");
        }
        return _handlers[type];
    }
    
    private class PacketHandlerWrapper<T> : IPacketHandler where T : Packet
    {
        private readonly PacketHandler<IRemoteSource, T> _handler;

        public PacketHandlerWrapper(PacketHandler<IRemoteSource, T> handler)
        {
            _handler = handler;
        }

        public Task Handle(IRemoteSource source, Packet packet)
        {
            return _handler(source, (T)packet);
        }
    }
}
