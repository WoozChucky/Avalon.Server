using Avalon.Network.Tcp.Configuration;
using Avalon.Network.Udp.Configuration;

namespace Avalon.Infrastructure.Configuration;

public class NetworkDaemonConfiguration
{
    public AvalonUdpServerConfiguration? Udp { get; set; }
    public AvalonTcpServerConfiguration? Tcp { get; set; }
}
