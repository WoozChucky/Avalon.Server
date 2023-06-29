using Avalon.Network.Packets.Abstractions;

namespace Avalon.Network;

public delegate void UdpClientPacketHandler(object? sender, UdpClientPacket clientPacket);

public interface IAvalonUdpServer : IAvalonNetworkServer
{
    event UdpClientPacketHandler OnPacketReceived;
    event UdpClientPacketHandler OnClientDisconnected;
    event UdpClientPacketHandler OnClientTimeout;
}
