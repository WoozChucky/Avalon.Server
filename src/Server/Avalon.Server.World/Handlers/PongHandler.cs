using Avalon.Network.Packets.Generic;
using Avalon.World;
using Microsoft.Extensions.Logging;

namespace Avalon.Server.World.Handlers;

public class PongHandler : IWorldPacketHandler<CPongPacket>
{
    private readonly ILogger<PongHandler> _logger;
    
    public PongHandler(ILogger<PongHandler> logger)
    {
        _logger = logger;
    }
    
    public Task ExecuteAsync(WorldPacketContext<CPongPacket> ctx, CancellationToken token = default)
    {
        var packet = ctx.Packet;
        
        ctx.Connection.OnPongReceived(packet.LastServerTimestamp, packet.ClientReceivedTimestamp, packet.ClientSentTimestamp);
        
        return Task.CompletedTask;
    }
}
