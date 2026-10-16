using Avalon.Api.Contract;
using Avalon.Common.GameAuth;
using Avalon.Configuration;
using Avalon.Infrastructure.WorldPlaces;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Identity.Services;

/// <summary>A waiter's check-in and leave (#967): Redis and the cached view of the world only.</summary>
public sealed class GameQueueService(IWorldPlaceStore places, GameWorldAvailability worlds, IOptions<WorldPlacesConfiguration> options,
    WorldPlaceMetrics metrics)
{
    public async Task<GameQueueStatusReply> StatusAsync(long accountId, ushort worldId, CancellationToken cancellationToken)
    {
        WorldAvailability world = await worlds.ReadAsync(worldId, cancellationToken);
        QueueCheckIn checkIn = await places.CheckInAsync(worldId, accountId, world.PlayerLimit, world.Available, cancellationToken);
        metrics.Record(worldId, checkIn.Sweep);
        return checkIn.State switch
        {
            QueueState.Queued or QueueState.Paused => new GameQueueStatusReply
            {
                State = checkIn.State == QueueState.Queued ? GameQueueStates.Queued : GameQueueStates.Paused,
                Position = checkIn.Position,
                CheckInAfterSeconds = (int)options.Value.CheckInAfter(checkIn.Position).TotalSeconds,
            },
            QueueState.YourTurn => new GameQueueStatusReply { State = GameQueueStates.YourTurn },
            _ => new GameQueueStatusReply { State = GameQueueStates.NotQueued },
        };
    }

    /// <summary>Leaves the line, giving back a turn not yet used; safe to repeat.</summary>
    public async Task<GameQueueStatusReply> LeaveAsync(long accountId, ushort worldId, CancellationToken cancellationToken)
    {
        if (await places.LeaveAsync(worldId, accountId, cancellationToken)) metrics.Cancelled(worldId);
        return new GameQueueStatusReply { State = GameQueueStates.NotQueued };
    }
}
