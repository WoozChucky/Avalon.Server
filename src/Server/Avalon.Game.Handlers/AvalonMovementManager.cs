using Avalon.Network;
using Avalon.Network.Abstractions;
using Avalon.Network.Packets;
using Microsoft.Extensions.Logging;

namespace Avalon.Game.Handlers;

public interface IAvalonMovementManager
{
    Task HandleJumpPacket(IRemoteSource source, NetworkPacket packet);
}

public class AvalonMovementManager : IAvalonMovementManager
{
    private readonly ILogger<AvalonMovementManager> _logger;

    public AvalonMovementManager(ILogger<AvalonMovementManager> logger)
    {
        _logger = logger;
    }
    
    public Task HandleMovePacket(IRemoteSource source, NetworkPacket packet)
    {
        return Task.CompletedTask;
    }

    public async Task HandleJumpPacket(IRemoteSource source, NetworkPacket packet)
    {
        var client = (UdpClientPacket) source;
        
        _logger.LogDebug("Handling jump packet from {EndPoint}", client.EndPoint);

        var bytesSent = await client.SendResponseAsync(client.Buffer);
        
        _logger.LogDebug("Sent {BytesSent} bytes to {EndPoint}", bytesSent, client.EndPoint);
    }
}
