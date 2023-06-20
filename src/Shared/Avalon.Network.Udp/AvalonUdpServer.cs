using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalon.Network.Abstractions;
using Avalon.Network.Udp.Configuration;
using Microsoft.Extensions.Logging;

namespace Avalon.Network.Udp;

public class AvalonUdpServer : IAvalonUdpServer
{
    private readonly ILogger<AvalonUdpServer> _logger;
    private readonly AvalonUdpServerConfiguration _configuration;
    private readonly CancellationTokenSource _cts;
    private readonly Socket _socket;
    
    private volatile bool _isRunning;
    
    public event UdpClientPacketHandler OnPacketReceived;

    public AvalonUdpServer(
        ILogger<AvalonUdpServer> logger, 
        AvalonUdpServerConfiguration configuration,
        CancellationTokenSource cts)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _cts = cts ?? throw new ArgumentNullException(nameof(cts));
        _isRunning = false;

        if (string.IsNullOrEmpty(_configuration.CertificatePath))
            throw new ArgumentNullException(nameof(configuration.CertificatePath));
     
        // TODO: Figure out how-to DTLS instead of raw udp comms
        //using var fs = new FileStream(_configuration.CertificatePath, FileMode.Open);
        //fs.Dispose();
        
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.Bind(new IPEndPoint(IPAddress.Any, _configuration.ListenPort));
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
    
    public async Task RunAsync()
    {
        if (_isRunning) throw new InvalidOperationException("Server is already running.");
        try
        {
            _isRunning = true;
            
            _logger.LogInformation("Listening at {EndPoint}", _socket.LocalEndPoint);
            
#pragma warning disable CS4014
            Task.Factory.StartNew(InternalServerLoop, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
#pragma warning restore CS4014
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Server stopped unexpectedly");
        }
        
    }
    
    private async Task InternalServerLoop()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var readBuffer = new byte[1024];
                var endpoint = new IPEndPoint(IPAddress.Any, 0);
                var result = await _socket.ReceiveFromAsync(readBuffer, SocketFlags.None, endpoint);
                var packetBuffer = new byte[result.ReceivedBytes];
                
                Array.Copy(readBuffer, packetBuffer, result.ReceivedBytes);

                OnPacketReceived?.Invoke(endpoint, new UdpClientPacket(result.RemoteEndPoint, packetBuffer, _socket.SendToAsync));
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Server loop cancelled");
        }
    }

    public Task StopAsync()
    {
        if (!_isRunning) throw new InvalidOperationException("Server is not running.");
        
        _isRunning = false;
        
        //TODO(Nuno): Close all connections.
        //TODO(Nuno): Send the connection disconnect packet to all clients.
        _logger.LogInformation("Server stopped");

        return Task.CompletedTask;
    }

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            _socket.Dispose();
            _logger.LogTrace("Disposed Socket");
        }
    }
}
