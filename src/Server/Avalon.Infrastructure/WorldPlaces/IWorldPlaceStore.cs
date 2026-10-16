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

/// <summary>
/// A request for a place: the place expires at <paramref name="Until"/>; a null <paramref name="Limit"/> is no limit.
/// <paramref name="Ticket"/> is the join ticket asking, by the id of the session it will become (its grant's
/// <c>GameSessionId</c>): an admitted account's place is that ticket's to hold until <paramref name="Until"/>.
/// </summary>
public readonly record struct PlaceRequest(ushort WorldId, long AccountId, DateTime Until, int? Limit, bool Staff, int QueueCapacity,
    Guid Ticket = default);

/// <summary>The answer to a <see cref="PlaceRequest"/>: <paramref name="Position"/> from 1 when queued, <paramref name="Waited"/> for a turn.</summary>
public readonly record struct PlaceDecision(PlaceOutcome Outcome, PlaceSource Source, int Position, TimeSpan Waited, PlaceSweep Sweep);

/// <summary>The answer to a waiter's check-in: <paramref name="Position"/> from 1 when queued or paused.</summary>
public readonly record struct QueueCheckIn(QueueState State, int Position, PlaceSweep Sweep);

/// <summary>A world's places taken, waiters and held places, after the sweep.</summary>
public readonly record struct WorldPlaceCounts(int Taken, int Queued, int Held, PlaceSweep Sweep);

/// <summary>
/// The places a world has given out and the line of a full world (#967), changed only by atomic operations, written by
/// identity alone. Ids are primitives: the store is at the edge of the server.
/// <para>
/// A place records who holds it: the join ticket issued for it, until its hold ends (by the ticket's id, the session id
/// it will become), and the session that owns its lease (by the session's fencing token, which only grows for an
/// account). A session's renewal or end changes the place only while no newer session owns it, and a ticket's
/// withdrawal and an activation touch only that ticket's own hold, so no late or stale call ever removes or shortens
/// a place another holder still has.
/// </para>
/// </summary>
public interface IWorldPlaceStore
{
    /// <summary>
    /// Asked before a join ticket is issued: a place (kept, returned, taken, or a turn), the line, or full. An admitted
    /// account's place is held by <see cref="PlaceRequest.Ticket"/> until <see cref="PlaceRequest.Until"/> (a later hold
    /// already there stays); a kept place is never shortened.
    /// </summary>
    Task<PlaceDecision> EnterAsync(PlaceRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The session <paramref name="sessionId"/> (fencing token <paramref name="fence"/>) was redeemed, activated or renewed:
    /// it owns the place, which expires at <paramref name="until"/> (or later, while a ticket's hold does), put back when
    /// it was lost, over the limit if need be. Nothing changes when a newer session owns the place, or when it is held
    /// after a drop. With <paramref name="activation"/> the session spends its own ticket's hold, and the account's place
    /// in every other world goes (a session is one per account), but for a ticket's hold still running there.
    /// </summary>
    Task RenewAsync(ushort worldId, long accountId, long fence, Guid sessionId, DateTime until, bool activation,
        CancellationToken cancellationToken);

    /// <summary>
    /// The session with fencing token <paramref name="fence"/> ended: its place is removed, or held until
    /// <paramref name="heldUntil"/>, and kept while a ticket's hold still runs. Nothing changes when a newer session owns
    /// the place; the place is never shortened.
    /// </summary>
    Task EndAsync(ushort worldId, long accountId, long fence, PlaceEnd end, DateTime heldUntil, CancellationToken cancellationToken);

    /// <summary>
    /// The ticket <paramref name="ticket"/>, which took a place until <paramref name="until"/>, was not issued after all:
    /// its hold is given back, and the place with it unless a session owns it. A no-op once another ticket holds it.
    /// </summary>
    Task WithdrawAsync(ushort worldId, long accountId, Guid ticket, DateTime until, CancellationToken cancellationToken);

    /// <summary>A waiter checks in: its position, or its turn when the world is available and a place is free for it.</summary>
    Task<QueueCheckIn> CheckInAsync(ushort worldId, long accountId, int? limit, bool available, CancellationToken cancellationToken);

    /// <summary>Leaves the line, giving back a turn not yet used. Idempotent; true when something was removed.</summary>
    Task<bool> LeaveAsync(ushort worldId, long accountId, CancellationToken cancellationToken);

    /// <summary>Places taken, waiters and held places, after removing what expired.</summary>
    Task<WorldPlaceCounts> CountAsync(ushort worldId, CancellationToken cancellationToken);
}
