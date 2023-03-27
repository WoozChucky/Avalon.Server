namespace Avalon.Network;

public delegate void ClientConnectedHandler(object? sender, TcpClient client);

public interface IAvalonTcpServer : IAvalonNetworkServer
{
    event ClientConnectedHandler ClientConnected;
}
