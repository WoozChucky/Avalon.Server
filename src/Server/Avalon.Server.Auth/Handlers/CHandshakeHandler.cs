using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Handshake;

namespace Avalon.Server.Auth.Handlers;

public class CHandshakeHandler : IAuthPacketHandler<CHandshakePacket>
{
    private readonly ILogger<CHandshakeHandler> _logger;
    
    public CHandshakeHandler(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<CHandshakeHandler>();
    }
    
    public Task ExecuteAsync(AuthPacketContext<CHandshakePacket> ctx, CancellationToken token = default)
    {
        if (!ctx.Connection.VerifyHandshakeData(ctx.Packet.HandshakeData))
        {
            _logger.LogWarning("Client {EndPoint} sent an invalid handshake data", ctx.Connection.Id);
            ctx.Connection.Close();
            return Task.CompletedTask;
        }

        var result = SHandshakeResultPacket.Create(true, ctx.Connection.CryptoSession.Encrypt);
        
        ctx.Connection.Send(result);
        
        return Task.CompletedTask;
    }
}
