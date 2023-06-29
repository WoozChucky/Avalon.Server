namespace Avalon.Network;

public delegate void TcpClientConnectedHandler(object? sender, TcpClient client);

public interface IAvalonTcpServer : IAvalonNetworkServer
{
    event TcpClientConnectedHandler ClientConnected;
}
