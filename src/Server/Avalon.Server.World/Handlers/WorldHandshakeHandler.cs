using Avalon.Network.Packets.Auth;
using Avalon.World;
using Microsoft.Extensions.Logging;

namespace Avalon.Server.World.Handlers;

public class WorldHandshakeHandler : IWorldPacketHandler<CWorldHandshakePacket>
{
    private readonly ILogger<WorldHandshakeHandler> _logger;
    private readonly IWorld _world;

    public WorldHandshakeHandler(ILogger<WorldHandshakeHandler> logger, IWorld world)
    {
        _logger = logger;
        _logger = logger;
        _world = world;
    }
    
    public Task ExecuteAsync(WorldPacketContext<CWorldHandshakePacket> ctx, CancellationToken token = default)
    {
        var clientVersion = ctx.Packet.Version;
        var minSupportedVersion = _world.MinVersion;
        var maxSupportedVersion = _world.CurrentVersion;
        
        /*
        if (!Version.TryParse(clientVersion, out var clientSemVer))
        {
            _logger.LogWarning("Client {EndPoint} sent an invalid version", ctx.Connection.RemoteEndPoint);
            return Task.CompletedTask;
        }
        
        if (!Version.TryParse(minSupportedVersion, out var minSemVer))
        {
            _logger.LogWarning("Invalid min supported version");
            return Task.CompletedTask;
        }
        
        if (!Version.TryParse(maxSupportedVersion, out var maxSemVer))
        {
            _logger.LogWarning("Invalid max supported version");
            return Task.CompletedTask;
        }
        
        var allowed = clientSemVer >= minSemVer && clientSemVer <= maxSemVer;
        */
        
        var allowed = true;
        
        var result = SWorldHandshakePacket.Create(
            ctx.Connection.AccountId ?? throw new InvalidOperationException("Invalid connection state, missing account id"),
            allowed,
            ctx.Connection.CryptoSession.Encrypt
        );

        ctx.Connection.Send(result);

        if (allowed)
        {
            ctx.Connection.EnableTimeSyncWorker();
        }
        
        return Task.CompletedTask;
    }
}
