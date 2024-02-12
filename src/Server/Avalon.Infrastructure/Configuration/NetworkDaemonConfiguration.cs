using Avalon.Network.Tcp.Configuration;

namespace Avalon.Infrastructure.Configuration;

public class NetworkDaemonConfiguration
{
    public AvalonTcpServerConfiguration? Tcp { get; set; }
}
