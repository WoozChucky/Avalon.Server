namespace Avalon.Network;

public delegate void UdpClientPacketHandler(object? sender, UdpClientPacket clientPacket);

public interface IAvalonUdpServer : IAvalonNetworkServer
{
    event UdpClientPacketHandler OnPacketReceived;
}
