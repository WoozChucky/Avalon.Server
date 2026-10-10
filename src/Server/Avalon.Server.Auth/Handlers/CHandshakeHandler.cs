using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Handshake;
using Avalon.Network.Packets.Serialization;

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

        OutboundPacket result = SHandshakeResultPacket.Create(true, PacketEncoder.Shared);

        ctx.Connection.Send(result);

        return Task.CompletedTask;
    }
}
