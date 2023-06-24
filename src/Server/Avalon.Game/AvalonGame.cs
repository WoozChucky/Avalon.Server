using System.Reflection;
using Avalon.Network.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Serialization;
using Microsoft.Extensions.Logging;

namespace Avalon.Game;

public interface IAvalonGame
{
    void Start();
    void Stop();
    Task HandleServerVersionPacket(IRemoteSource source, CRequestServerVersionPacket packet);
    Task HandlePingPacket(IRemoteSource source, CPingPacket packet);
}

public class AvalonGame : IAvalonGame
{
    private readonly ILogger<AvalonGame> _logger;
    private readonly CancellationTokenSource _cts;
    private readonly IPacketSerializer _packetSerializer;

    public AvalonGame(ILogger<AvalonGame> logger, IPacketSerializer packetSerializer)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cts = new CancellationTokenSource();
        _packetSerializer = packetSerializer;
    }

    public async void Start()
    {
        _logger.LogInformation("Starting game loop");
        
        var previousTime = DateTime.UtcNow;

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var currentTime = DateTime.UtcNow;
                var deltaTime = currentTime - previousTime;
                previousTime = currentTime;
        
                Update(deltaTime);
                
                await BroadcastGameState();
            
                await Task.Delay(50);
            }
        }
        catch (OperationCanceledException e)
        {
            _logger.LogInformation("Game loop cancelled");
        }
    }

    public void Stop()
    {
        _logger.LogInformation("Stopping game loop");
        _cts.Cancel();
    }

    private void Update(TimeSpan deltaTime)
    {
        
    }
    
    private async Task BroadcastGameState()
    {
        
    }

    public async Task HandleServerVersionPacket(IRemoteSource source, CRequestServerVersionPacket packet)
    {
        var client = (TcpClient) source;
        
        _logger.LogDebug("Handling server version packet from {EndPoint}", client.Socket.RemoteEndPoint);
        
        var result = SServerVersionPacket.Create(
            Assembly.GetExecutingAssembly().GetName().Version?.Major ?? 0,
            Assembly.GetExecutingAssembly().GetName().Version?.Minor ?? 0,
            Assembly.GetExecutingAssembly().GetName().Version?.Build ?? 0,
            Assembly.GetExecutingAssembly().GetName().Version?.Revision ?? 0
        );
        
        await _packetSerializer.SerializeToNetwork(client.Stream, result);
    }

    public async Task HandlePingPacket(IRemoteSource source, CPingPacket packet)
    {
        var client = source.AsUdpClient();

        var response = SPongPacket.Create(packet.Ticks);
        
        await using var ms = new MemoryStream();
        await _packetSerializer.SerializeToNetwork(ms, response);
        
        await client.SendResponseAsync(ms.ToArray());
    }
}
