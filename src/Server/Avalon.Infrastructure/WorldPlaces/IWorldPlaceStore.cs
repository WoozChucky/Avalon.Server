namespace Avalon.Infrastructure.WorldPlaces;

/// <summary>What a request for a place in a world came to (#967).</summary>
public enum PlaceOutcome { Admitted = 0, Queued = 1, WorldFull = 2 }

/// <summary>Where an admitted account's place came from: newly taken, already held, held after a drop, or a turn from the line.</summary>
public enum PlaceSource { Taken = 0, Kept = 1, Returned = 2, Turn = 3 }

/// <summary>A waiter's state at a check-in.</summary>
public enum QueueState { NotQueued = 0, Queued = 1, Paused = 2, YourTurn = 3 }

/// <summary>What a session's end does to its place: removes it, or holds it for the grace.</summary>
public enum PlaceEnd { Remove = 0, Hold = 1 }

/// <summary>What the sweep that starts an operation removed: turns that expired unused, waiters that stopped checking in.</summary>
public readonly record struct PlaceSweep(int TurnsExpired, int WaitersTimedOut);

/// <summary>A request for a place: the place expires at <paramref name="Until"/>; a null <paramref name="Limit"/> is no limit.</summary>
public readonly record struct PlaceRequest(ushort WorldId, long AccountId, DateTime Until, int? Limit, bool Staff, int QueueCapacity);

/// <summary>The answer to a <see cref="PlaceRequest"/>: <paramref name="Position"/> from 1 when queued, <paramref name="Waited"/> for a turn.</summary>
public readonly record struct PlaceDecision(PlaceOutcome Outcome, PlaceSource Source, int Position, TimeSpan Waited, PlaceSweep Sweep);

/// <summary>The answer to a waiter's check-in: <paramref name="Position"/> from 1 when queued or paused.</summary>
public readonly record struct QueueCheckIn(QueueState State, int Position, PlaceSweep Sweep);

/// <summary>A world's places taken, waiters and held places, after the sweep.</summary>
public readonly record struct WorldPlaceCounts(int Taken, int Queued, int Held, PlaceSweep Sweep);

/// <summary>
/// The places a world has given out and the line of a full world (#967), changed only by atomic operations, written by
/// identity alone. Ids are primitives: the store is at the edge of the server.
/// </summary>
public interface IWorldPlaceStore
{
    /// <summary>Asked before a join ticket is issued: a place (kept, returned, taken, or a turn), the line, or full.</summary>
    Task<PlaceDecision> EnterAsync(PlaceRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The session holding the place was redeemed, activated or renewed: the place expires at <paramref name="until"/>,
    /// put back when it was lost, over the limit if need be. With <paramref name="leaveOtherWorlds"/> (activation) the
    /// account's place in every other world is removed: a session is one per account.
    /// </summary>
    Task RenewAsync(ushort worldId, long accountId, DateTime until, bool leaveOtherWorlds, CancellationToken cancellationToken);

    /// <summary>The session ended: its place is removed, or held until <paramref name="heldUntil"/>.</summary>
    Task EndAsync(ushort worldId, long accountId, PlaceEnd end, DateTime heldUntil, CancellationToken cancellationToken);

    /// <summary>A waiter checks in: its position, or its turn when the world is available and a place is free for it.</summary>
    Task<QueueCheckIn> CheckInAsync(ushort worldId, long accountId, int? limit, bool available, CancellationToken cancellationToken);

    /// <summary>Leaves the line, giving back a turn not yet used. Idempotent; true when something was removed.</summary>
    Task<bool> LeaveAsync(ushort worldId, long accountId, CancellationToken cancellationToken);

    /// <summary>Places taken, waiters and held places, after removing what expired.</summary>
    Task<WorldPlaceCounts> CountAsync(ushort worldId, CancellationToken cancellationToken);
}
