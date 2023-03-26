using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Avalon.Network.Tcp.Configuration;

namespace Avalon.Network.Tcp;

public class AvalonTcpServer : IAvalonNetworkServer
{
    private readonly AvalonTcpServerConfiguration _configuration;
    private readonly X509Certificate2 _certificate;
    private readonly Socket _socket;
    private readonly CancellationTokenSource _cts;
    
    private volatile bool _isRunning;

    public AvalonTcpServer(AvalonTcpServerConfiguration configuration, CancellationTokenSource? cts = default)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _cts = cts ?? new CancellationTokenSource();
        _isRunning = false;
    }
    
    ~AvalonTcpServer()
    {
        Dispose(false);
    }
    
    public Task RunAsync(bool blocking = true)
    {
        if (_isRunning)
        {
            throw new InvalidOperationException("Server is already running.");
        }
    }

    public Task StopAsync()
    {
        if (!_isRunning)
        {
            throw new InvalidOperationException("Server is not running.");
        }

        _isRunning = false;
        _cts.Cancel();

        //TODO(Nuno): Close all connections.
        //TODO(Nuno): Send the connection disconnect packet to all clients.
        
        return Task.CompletedTask;
    }

    public bool IsRunning => _isRunning;

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            _certificate.Dispose();
            _socket.Dispose();
            _cts.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}