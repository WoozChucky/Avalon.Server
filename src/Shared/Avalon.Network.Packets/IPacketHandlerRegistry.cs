using Avalon.Network.Abstractions;

namespace Avalon.Network.Packets;
public delegate Task PacketHandler<in T1>(T1 source, NetworkPacket packet);

public interface IPacketHandlerRegistry
{
    void RegisterHandler(NetworkPacketType packetType, PacketHandler<IRemoteSource> handler);

    PacketHandler<IRemoteSource> GetHandler(NetworkPacketType type);
}
