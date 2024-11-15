using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Movement;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_MOVEMENT)]
public class CharacterMovementHandler(ILogger<CharacterMovementHandler> logger, IWorld world)
    : WorldPacketHandler<CPlayerMovementPacket>
{
    private const int DefaultGravity = -2;
    private const float MaxDistanceDiffCheck = 1.0f;

    public override void Execute(WorldConnection connection, CPlayerMovementPacket packet)
    {
        TimeSpan deltaTime = GameTime.GetDeltaTime();

        float currentMovementSpeed = connection.Character!.GetMovementSpeed();

        double timeSinceLastPacket = packet.Timestamp - connection.LastMovementTime;

        double timeSinceLastPacketSeconds = timeSinceLastPacket / 1000;

        // Due to natural gravity, the client sends a velocity of -2 when not falling or jumping
        float yVelovity = Mathf.Approximately(packet.VelocityY, DefaultGravity) ? 0 : packet.VelocityY;

        // Interpolate the new position based on the velocity and time since the last movement packet was received
        Vector3 direction = new Vector3(packet.VelocityX, yVelovity, packet.VelocityZ);
        Vector3 interpolatedPosition = connection.Character!.Position +
                                       direction * currentMovementSpeed * (float)timeSinceLastPacketSeconds;

        // Calculate the nwe position based on the server's delta time
        // var newPosition = connection.Character!.Position + direction * currentMovementSpeed * (float) deltaTime.TotalSeconds;

        float serverCalculatedDistance = Vector3.Distance(connection.Character.Position, interpolatedPosition);

        Vector3 clientSentPosition = new Vector3(packet.X, packet.Y, packet.Z);
        float clientCalculatedDistance = Vector3.Distance(connection.Character.Position, clientSentPosition);

        logger.LogTrace(
            "Client {ClientSentPosition} vs {InterpolatedPosition}. Client distance: {ClientDistance} vs {ServerCalculatedDistance}",
            clientSentPosition, interpolatedPosition, clientCalculatedDistance, serverCalculatedDistance);
        //TODO: Differences may happen specifically because the server still doesnt handle collisions for characters
        float differenceDistances = Mathf.Abs(serverCalculatedDistance - clientCalculatedDistance);
        if (differenceDistances >= MaxDistanceDiffCheck)
        {
            logger.LogWarning(
                "{Name} desync detected. Server calculated distance: {ServerCalculatedDistance} vs client: {ClientDistance}",
                connection.Character.Name, serverCalculatedDistance, clientCalculatedDistance);
        }

        connection.Character!.Position = clientSentPosition;
        connection.Character.Velocity = direction;
        connection.Character.Orientation = new Vector3(0, packet.Rotation, 0);
        connection.LastMovementTime = packet.Timestamp;


        world.Grid.OnPlayerMoved(connection);
    }
}
