namespace Avalon.Network;

public delegate void UdpClientPacketHandler(object? sender, UdpClientPacket clientPacket);

[Obsolete("Use IAvalonTcpServer instead")]
public interface IAvalonUdpServer : IAvalonNetworkServer
{
    event UdpClientPacketHandler OnPacketReceived;
    event UdpClientPacketHandler OnClientDisconnected;
    event UdpClientPacketHandler OnClientTimeout;
}
