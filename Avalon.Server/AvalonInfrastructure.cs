using Avalon.Network;
using Avalon.Network.Tcp;
using Avalon.Network.Tcp.Configuration;

namespace Avalon.Server;

public class AvalonInfrastructure : IDisposable
{
    private readonly CancellationTokenSource _cts;
    
    private readonly IAvalonNetworkServer _tcpServer;

    private volatile bool _isRunning;
    
    public AvalonInfrastructure()
    {
        _cts = new CancellationTokenSource();
        _tcpServer = new AvalonTcpServer(
            new AvalonTcpServerConfiguration
        {
            Backlog = 10,
            CertificatePassword = "avalon",
            CertificatePath = "cert-tcp.pfx",
            ListenPort = 21000
        });
    }
    
    public async Task Run()
    {
        await _tcpServer.RunAsync(true);
    }

    public void Dispose()
    {
        _cts.Dispose();
        _tcpServer.Dispose();
    }
}
