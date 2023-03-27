using System.Net;
using System.Text;
using Avalon.Network.Udp.Configuration;
using Microsoft.Extensions.Logging;
using DtlsServer = Avalon.Network.Udp.Security.Server;

namespace Avalon.Network.Udp;

public class AvalonUdpServer : IAvalonUdpServer
{
    private readonly ILogger<AvalonUdpServer> _logger;
    private readonly AvalonUdpServerConfiguration _configuration;
    private readonly DtlsServer _server;
    
    private volatile bool _isRunning;
    private CancellationTokenSource _cts;
    
    
    public AvalonUdpServer(
        ILogger<AvalonUdpServer> logger, 
        AvalonUdpServerConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _cts = new CancellationTokenSource();
        _isRunning = false;

        using var fs = new FileStream(_configuration.CertificatePath ?? throw new ArgumentNullException(nameof(configuration.CertificatePath)), FileMode.Open);
        _server = new DtlsServer(new IPEndPoint(IPAddress.Any, _configuration.ListenPort));
        _server.LoadCertificateFromPem(fs);
        fs.Dispose();
        
        _server.DataReceived += ServerOnDataReceived;
    }

    ~AvalonUdpServer()
    {
        Dispose(false);
    }
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
        _logger.LogDebug("Disposed AvalonUdpServer");
    }

    public bool IsRunning => _isRunning;
    
    public async Task RunAsync(bool blocking = true)
    {
        if (_isRunning) throw new InvalidOperationException("Server is already running.");
        try
        {
            _cts = _cts.Token.IsCancellationRequested ? new CancellationTokenSource() : _cts;
            
            _server.Start();
            _isRunning = true;
            
            _logger.LogInformation("Server started at {@EndPoint}", _server.LocalEndPoint);

            if (blocking)
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(50).ConfigureAwait(false);
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Server stopped unexpectedly");
        }
        
    }

    public Task StopAsync()
    {
        if (!_isRunning) throw new InvalidOperationException("Server is not running.");
        
        _cts.Cancel();
        _server.Stop();
        _isRunning = false;
        
        _logger.LogInformation("Server stopped");

        return Task.CompletedTask;
    }
    
    private void ServerOnDataReceived(EndPoint endpoint, byte[] data)
    {
        _logger.LogInformation("Received data from {@EndPoint}", endpoint);
        _logger.LogInformation("Data: {Data}", Encoding.UTF8.GetString(data));
    }
    
    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            _server.Stop();
            _cts.Dispose();
        }
    }
}
