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

/// <summary>
/// The answer to a <see cref="PlaceRequest"/>: <paramref name="Position"/> from 1 when queued, <paramref name="Waited"/> for a
/// turn. <paramref name="LockedOut"/> marks a <see cref="PlaceOutcome.WorldFull"/> answered for the account's lockout (its
/// unclaimed place lapsed and was refused a moment ago), not for the line at its cap.
/// </summary>
public readonly record struct PlaceDecision(PlaceOutcome Outcome, PlaceSource Source, int Position, TimeSpan Waited, PlaceSweep Sweep,
    bool LockedOut = false);

/// <summary>The answer to a waiter's check-in: <paramref name="Position"/> from 1 when queued or paused.</summary>
public readonly record struct QueueCheckIn(QueueState State, int Position, PlaceSweep Sweep);

/// <summary>A world's places taken, waiters and held places, after the sweep.</summary>
public readonly record struct WorldPlaceCounts(int Taken, int Queued, int Held, PlaceSweep Sweep);

/// <summary>
/// The places a world has given out and the line of a full world (#967), changed only by atomic operations, written by
/// identity alone. Ids are primitives: the store is at the edge of the server.
/// <para>
/// A place records who holds it: the last join ticket to enter for it, until its hold ends (by the ticket's id, the
/// session id it will become), and the session that owns its lease, or that ended there (by the session's fencing token,
/// which only grows for an account). A session's end, and an activation's removal of the account's place in another
/// world, leave a tombstone of the ended session for one lease, kept when the place goes: a late renewal of that session
/// or an older one changes nothing, so it can neither shorten a place another holder has nor bring back one given to
/// another. A session's renewal or end changes the place only while no newer session owns it, and a ticket's withdrawal
/// and an activation touch only that ticket's own hold. What a stale call can still do is keep a place counted a little
/// longer (a replay's extended hold, at most 45 s), never count it out.
/// </para>
/// </summary>
public interface IWorldPlaceStore
{
    /// <summary>
    /// Asked before a join ticket is issued: a place (kept, returned, taken, or a turn), the line, or full. An admitted
    /// account's place is held by <see cref="PlaceRequest.Ticket"/>, the last ticket to enter, until the later of
    /// <see cref="PlaceRequest.Until"/> and any hold already there; no place is ever shortened.
    /// </summary>
    Task<PlaceDecision> EnterAsync(PlaceRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The session <paramref name="sessionId"/> (fencing token <paramref name="fence"/>) was redeemed, activated or renewed:
    /// it owns the place, which expires at <paramref name="until"/> (or later, while a ticket's hold does), put back when
    /// it was lost, over the limit if need be. Nothing changes when a newer session owns the place, or when this session
    /// or a newer one has ended there. With <paramref name="activation"/> the session spends its own ticket's hold, and the account's place
    /// in every other world goes (a session is one per account), but for a ticket's hold still running there.
    /// </summary>
    Task RenewAsync(ushort worldId, long accountId, long fence, Guid sessionId, DateTime until, bool activation,
        CancellationToken cancellationToken);

    /// <summary>
    /// The session with fencing token <paramref name="fence"/> ended: its place is removed, or held until
    /// <paramref name="heldUntil"/>, and kept while a ticket's hold still runs; the place is never shortened, and never
    /// re-created when it no longer exists. In every case but a newer session owning the place (which changes nothing),
    /// the end leaves a tombstone of the session for one lease, place or not, so its late renewals change nothing.
    /// </summary>
    Task EndAsync(ushort worldId, long accountId, long fence, PlaceEnd end, DateTime heldUntil, CancellationToken cancellationToken);

    /// <summary>
    /// The ticket <paramref name="ticket"/>, which took a place, is known not to be issued: its hold is given back, and the
    /// place with it unless a live session owns it. A no-op once another ticket has entered since.
    /// </summary>
    Task WithdrawAsync(ushort worldId, long accountId, Guid ticket, CancellationToken cancellationToken);

    /// <summary>
    /// A replayed ticket, not yet redeemed: its place, if it still has one, is extended (never shortened) to
    /// <paramref name="until"/> and held by <paramref name="ticket"/> again. Never creates a place: a replay asks no limit.
    /// </summary>
    Task RejoinAsync(ushort worldId, long accountId, Guid ticket, DateTime until, CancellationToken cancellationToken);

    /// <summary>
    /// The world stopped or reached a maintenance cutoff: after it everyone is equal, so every place held after a drop is
    /// released (its tombstone stays). A place a ticket has taken since (a return within the grace) is a ticket's and
    /// stays. At most once in <see cref="HeldReleaseWindow"/> for a world, so the burst of a stop's session ends releases
    /// once; a later call after the window releases what dropped since.
    /// </summary>
    Task ReleaseHeldAsync(ushort worldId, CancellationToken cancellationToken);

    /// <summary>How long after one release of a world's held places another is a no-op.</summary>
    static readonly TimeSpan HeldReleaseWindow = TimeSpan.FromSeconds(10);

    /// <summary>
    /// A waiter checks in: its position, or its turn when the world is available and a place is free for it. An account
    /// holding a place of its own there (one its join keeps: a turn taken, a place held after a drop, its session's, a
    /// ticket's hold not lapsed) is told its turn at once, and leaves the line if it is in it; a place that is not its own
    /// (released by another world's ticket, or lapsed and kept only for its last ticket's hold) is counted but is no turn:
    /// it waits its rank. While the world is not available an unused turn is kept (paused at position 1), its window
    /// starting again. A turn given releases what the account holds in other worlds with no session (a ticket-only place,
    /// an unused turn), as a ticket admitted does.
    /// </summary>
    Task<QueueCheckIn> CheckInAsync(ushort worldId, long accountId, int? limit, bool available, CancellationToken cancellationToken);

    /// <summary>
    /// Leaves the line, giving back a turn not yet used: a turn given over a place the account still had counted returns
    /// that place to what it was (counted to its own end, released) rather than removing it. Idempotent; true when
    /// something was removed.
    /// </summary>
    Task<bool> LeaveAsync(ushort worldId, long accountId, CancellationToken cancellationToken);

    /// <summary>Places taken, waiters and held places, after removing what expired.</summary>
    Task<WorldPlaceCounts> CountAsync(ushort worldId, CancellationToken cancellationToken);
}
