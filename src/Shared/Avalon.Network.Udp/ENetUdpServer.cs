using Avalon.Network.Abstractions;
using Avalon.Network.Udp.Configuration;
using ENet;
using Microsoft.Extensions.Logging;

namespace Avalon.Network.Udp;

public class ENetUdpServer : IAvalonUdpServer
{
    private readonly ILogger<ENetUdpServer> _logger;
    private readonly AvalonUdpServerConfiguration _configuration;
    private readonly CancellationTokenSource _cts;

    private volatile bool _isRunning;
    
    private readonly Host _server;
    private readonly Address _address;
    
    public event UdpClientPacketHandler? OnPacketReceived;
    public bool IsRunning => _isRunning;
    
    public ENetUdpServer(ILogger<ENetUdpServer> logger, 
        AvalonUdpServerConfiguration configuration,
        CancellationTokenSource cts)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _cts = cts ?? throw new ArgumentNullException(nameof(cts));
        _isRunning = false;

        _server = new Host();
        _address = new Address
        {
            Port = (ushort) _configuration.ListenPort
        };
    }
    
    
    
    public async Task RunAsync()
    {
        if (_isRunning) throw new InvalidOperationException("Server is already running.");
        try
        {
            _isRunning = true;
            
            _logger.LogInformation("Listening at {EndPoint}", _configuration.ListenPort);
            
#pragma warning disable CS4014
            Task.Factory.StartNew(InternalServerLoop, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
#pragma warning restore CS4014
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Server stopped unexpectedly");
        }
    }

    private void InternalServerLoop()
    {
        try
        {
            _server.Create(_address, _configuration.Backlog);
            
            Event netEvent;
            
            while (!_cts.IsCancellationRequested)
            {
                var polled = false;

                while (!polled)
                {
                    if (_server.CheckEvents(out netEvent) <= 0) {
                        if (_server.Service(15, out netEvent) <= 0)
                            break;

                        polled = true;
                    }

                    switch (netEvent.Type)
                    {
                        case EventType.None:
                            break;
                        case EventType.Connect:
                            _logger.LogInformation("Client connected - ID: {PeerId}, IP: {PeerIP}", netEvent.Peer.ID, netEvent.Peer.IP);
                            break;
                        
                        case EventType.Disconnect:
                            _logger.LogInformation("Client disconnected - ID: {PeerId}, IP: {PeerIP}", netEvent.Peer.ID, netEvent.Peer.IP);
                            break;
                        
                        case EventType.Timeout:
                            _logger.LogInformation("Client timeout - ID: {PeerId}, IP: {PeerIP}", netEvent.Peer.ID, netEvent.Peer.IP);
                            break;
                        
                        case EventType.Receive:
                            // _logger.LogDebug("Packet received from - ID: {PeerId}, IP: {PeerIP}", netEvent.Peer.ID, netEvent.Peer.IP);
                            
                            var buffer = new byte[netEvent.Packet.Length];
                            netEvent.Packet.CopyTo(buffer);
                            
                            OnPacketReceived?.Invoke(this, new UdpClientPacket(netEvent.Peer, buffer));
                            
                            netEvent.Packet.Dispose();
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                    
                }
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
    
    public void Dispose()
    {
        _server.Dispose();
    }
}
