using Avalon.Network.Packets.Abstractions;

namespace Avalon.Network.Packets.Internal;

public delegate Task PacketHandler<in T1, in T2>(T1 source, T2 packet) where T2 : Packet;

public interface IPacketRegistry
{
    void RegisterHandler<T>(NetworkPacketType packetType, PacketHandler<IRemoteSource, T> handler) where T : Packet;

    IPacketHandler GetHandler(NetworkPacketType type);
}

public interface IPacketHandler
{
    Task Handle(IRemoteSource source, Packet packet);
}
