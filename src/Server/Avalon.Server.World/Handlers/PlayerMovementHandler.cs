using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Movement;
using Avalon.World;
using Microsoft.Extensions.Logging;

namespace Avalon.Server.World.Handlers;

public class PlayerMovementHandler : IWorldPacketHandler<CPlayerMovementPacket>
{
    private readonly IWorldServer _worldServer;
    private readonly ILogger<PlayerMovementHandler> _logger;
    
    private const float AllowedDeviation = 0.1f;
    
    public PlayerMovementHandler(ILoggerFactory loggerFactory, IWorldServer worldServer)
    {
        _worldServer = worldServer;
        _logger = loggerFactory.CreateLogger<PlayerMovementHandler>();
    }
    
    public Task ExecuteAsync(WorldPacketContext<CPlayerMovementPacket> ctx, CancellationToken token = default)
    {
        if (!ctx.Connection.InMap)
        {
            _logger.LogWarning("Account {AccountId} is not in a map", ctx.Connection.AccountId);
            ctx.Connection.Close();
            return Task.CompletedTask;
        }
        
        /*
        var clientMillis = ctx.Packet.ElapsedGameTime - ctx.Connection.Latency;
        var intendedPosition = new Vector2(ctx.Packet.X, ctx.Packet.Y);
        var direction = new Vector2(ctx.Packet.VelocityX, ctx.Packet.VelocityY);
        
        var currentPosition = ctx.Connection.Character!.Movement.Position;
        var serverMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        
        // Calculate elapsed time since the client sent the packet
        var elapsedMillis = serverMillis - clientMillis;
        var elapsedSeconds = (long) elapsedMillis / 1000f;

        var velocity = direction;
        
        var expectedPosition = currentPosition + velocity * elapsedSeconds;
        
        var distance = Vector2.Distance(currentPosition, expectedPosition);
        
        //_logger.LogDebug("{ElapsedMillis} - {ElapsedSeconds} - {VelocityLenght}", elapsedMillis, elapsedSeconds, velocity.Length());
        //_logger.LogDebug("{MaxMovementDistance} - {ActualMovementDistance}", maxMovementDistance, actualMovementDistance);
        if (distance > AllowedDeviation)
        {
            _logger.LogInformation("Player {PlayerId} is cheating. Expected position: {ExpectedPosition}, Actual position: {ActualPosition}", ctx.Connection.Character.Id, expectedPosition, intendedPosition);
            //TODO: Send correction packet to the client
        }
        else
        {
            ctx.Connection.Character.Movement.Position = new Vector2(intendedPosition.X, intendedPosition.Y);
            ctx.Connection.Character.Movement.Velocity = new Vector2(direction.X, direction.Y);
        }
        */
        
        ctx.Connection.Character!.Movement.Position = new Vector3(ctx.Packet.X, ctx.Packet.Y, ctx.Packet.Z);
        ctx.Connection.Character.Movement.Velocity = new Vector3(ctx.Packet.VelocityX, ctx.Packet.VelocityY, ctx.Packet.VelocityZ);
        
        return Task.CompletedTask;
    }
}
