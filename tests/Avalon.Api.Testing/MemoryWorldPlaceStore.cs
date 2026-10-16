using System.Globalization;
using Avalon.Common.GameAuth;
using Avalon.Configuration;
using Avalon.Infrastructure.WorldPlaces;

namespace Avalon.Api.Testing;

/// <summary>
/// <see cref="IWorldPlaceStore"/> in memory, every operation under one lock: the reference the Redis scripts are held to by
/// <c>WorldPlaceScenarios</c>. The same steps, in the same order, as <c>RedisWorldPlaceStore</c>'s scripts.
/// <paramref name="configuredWorlds"/> are the worlds an activation or a ticket reaches beyond its own, as the Redis store's
/// configured workloads are; none given, every world the store has seen.
/// </summary>
public sealed class MemoryWorldPlaceStore(TimeProvider clock, WorldPlacesConfiguration config,
    IReadOnlyCollection<ushort>? configuredWorlds = null) : IWorldPlaceStore
{
    private const string Held = "h";
    private const string TurnPrefix = "t:";
    private const string Released = "r";

    private readonly Lock _gate = new();
    private readonly Dictionary<ushort, WorldState> _worlds = [];
    private readonly Dictionary<long, (ushort World, long EnqueuedAt)> _waiting = [];

    private sealed class WorldState
    {
        public readonly Dictionary<long, long> Places = [];      // account -> expiry
        public readonly Dictionary<long, string> Marks = [];     // account -> "h", "t:{enqueuedAt}" or a ticket's hold mark
        // account -> the owning session, or the one that ended there (a tombstone, until its expiry)
        public readonly Dictionary<long, (long Fence, long? TombstoneUntil)> Owners = [];
        // account -> since when no session claims its place (the field {account}:f)
        public readonly Dictionary<long, long> Unclaimed = [];
        // account -> when its lockout (the field {account}:l) expires
        public readonly Dictionary<long, long> Lockouts = [];
        // account -> when the pending marker of a session redeemed and not yet activated (the field {account}:p) expires
        public readonly Dictionary<long, long> Pending = [];
        public readonly SortedList<long, long> Line = [];        // arrival -> account
        public readonly Dictionary<long, long> Arrival = [];     // account -> arrival
        public readonly Dictionary<long, long> Deadline = [];    // account -> removal deadline
        public long Arrivals;
        public long? Released;                                   // the last release of the held places

        public int Rank(long account) => Arrival.TryGetValue(account, out long arrival) ? Line.IndexOfKey(arrival) : -1;

        /// <summary>ZADD GT: the place's end, never moved earlier; a missing place is added.</summary>
        public void Extend(long account, long expiry) =>
            Places[account] = Places.TryGetValue(account, out long current) ? Math.Max(current, expiry) : expiry;

        /// <summary>The place and its fields, but an ended session's tombstone, which expires on its own.</summary>
        public void Forget(long account)
        {
            Places.Remove(account);
            Marks.Remove(account);
            Unclaimed.Remove(account);
            Pending.Remove(account);
            if (Owners.TryGetValue(account, out (long Fence, long? TombstoneUntil) owner) && owner.TombstoneUntil is null)
                Owners.Remove(account);
        }

        /// <summary>Since when no session claims the place, recorded once: the earliest moment stands until a claim.</summary>
        public void MarkUnclaimed(long account, long now) => Unclaimed.TryAdd(account, now);

        /// <summary>Whether a session redeemed and not yet activated is pending on the place.</summary>
        public bool IsPending(long account, long now)
        {
            if (!Pending.TryGetValue(account, out long until)) return false;
            if (until > now) return true;
            Pending.Remove(account);
            return false;
        }

        /// <summary>Since when no session claims the place, if none has since it was taken or left.</summary>
        public long? UnclaimedSince(long account) => Unclaimed.TryGetValue(account, out long since) ? since : null;

        /// <summary>Whether the account's lockout is still running.</summary>
        public bool LockedOut(long account, long now)
        {
            if (!Lockouts.TryGetValue(account, out long until)) return false;
            if (until > now) return true;
            Lockouts.Remove(account);
            return false;
        }

        /// <summary>The owner field as the scripts read it: absent once a tombstone has expired.</summary>
        public (long Fence, bool Ended)? Owner(long account, long now)
        {
            if (!Owners.TryGetValue(account, out (long Fence, long? TombstoneUntil) owner)) return null;
            if (owner.TombstoneUntil <= now)
            {
                Owners.Remove(account);
                return null;
            }

            return (owner.Fence, owner.TombstoneUntil is not null);
        }

        /// <summary>The session <paramref name="fence"/> ended here: its tombstone, expiring one lease from now.</summary>
        public void Tombstone(long account, long fence, long now) =>
            Owners[account] = (fence, now + (long)GameAuthPolicy.SessionLeaseLifetime.TotalMilliseconds);

        /// <summary>
        /// The ticket's hold on the account's place, its end, its id, and whether the place was held after a drop when
        /// the ticket took it, when the mark is one.
        /// </summary>
        public (long Until, string Id, bool WasHeld)? Ticket(long account)
        {
            if (!Marks.TryGetValue(account, out string? mark) || !mark.StartsWith("j:", StringComparison.Ordinal)) return null;
            int colon = mark.IndexOf(':', 2);
            string id = mark[(colon + 1)..];
            bool wasHeld = id.EndsWith(":h", StringComparison.Ordinal);
            return (long.Parse(mark[2..colon], CultureInfo.InvariantCulture), wasHeld ? id[..^2] : id, wasHeld);
        }
    }

    private long Now => clock.GetUtcNow().ToUnixTimeMilliseconds();
    private static long Ms(DateTime utc) => RedisWorldPlaceStore.Ms(utc);
    private WorldState World(ushort id) => _worlds.TryGetValue(id, out WorldState? w) ? w : _worlds[id] = new WorldState();

    public Task<PlaceDecision> EnterAsync(PlaceRequest request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            long now = Now, until = Ms(request.Until), account = request.AccountId;
            string hold = RedisWorldPlaceStore.TicketMark(until, request.Ticket);
            WorldState w = World(request.WorldId);
            PlaceSweep sweep = Sweep(request.WorldId, w, now);
            PlaceDecision Admit(PlaceSource source, long waited, bool wasHeld = false)
            {
                // Every place a ticket is admitted to is never shortened, and is the ticket's to hold. The owner is left
                // as it is: on a new place it can only be an ended session's tombstone.
                w.Extend(account, until);
                w.Marks[account] = wasHeld ? RedisWorldPlaceStore.TicketMark(until, request.Ticket, true) : hold;
                w.MarkUnclaimed(account, now);
                ReleaseElsewhere(request.WorldId, account, now);
                return new(PlaceOutcome.Admitted, source, 0, TimeSpan.FromMilliseconds(waited), sweep);
            }

            PlaceDecision Queued(int position) => new(PlaceOutcome.Queued, PlaceSource.Taken, position, TimeSpan.Zero, sweep);

            // A place no session has claimed for the horizon is not kept again: the request is a newcomer's, its place
            // still counted; admitted, the place is its own again with a new horizon; refused or queued, it is released.
            // A place another world's ticket released is counted but not the account's to keep.
            (bool present, bool lapsed) = Standing(w, account, now);
            long? HoldEnd() => w.Ticket(account) is { } t && t.Until > now ? t.Until : null;
            PlaceDecision Readmit(PlaceSource source, long waited)
            {
                if (lapsed) w.Unclaimed.Remove(account);
                return Admit(source, waited);
            }

            // Not while its last ticket's hold runs: that ticket may still be redeemed.
            void Release()
            {
                if (lapsed && HoldEnd() is null) w.Forget(account);
            }

            if (present && !lapsed)
            {
                // A place of its own needs no position: an account also in the line leaves it.
                if (w.Rank(account) >= 0) RemoveFromLine(request.WorldId, w, account);
                w.Marks.TryGetValue(account, out string? mark);
                if (mark == Held) return Task.FromResult(Admit(PlaceSource.Returned, 0, wasHeld: true));
                if (Turn(mark) is { } turn) return Task.FromResult(Admit(PlaceSource.Turn, now - turn.Enqueued));
                // Kept: never shortened, and held by the last ticket to enter, to the later of its end and the hold there.
                w.Extend(account, until);
                (long Until, string Id, bool WasHeld)? kept = w.Ticket(account);
                w.Marks[account] = RedisWorldPlaceStore.TicketMark(Math.Max(until, kept?.Until ?? 0), request.Ticket,
                    kept?.WasHeld == true);
                w.MarkUnclaimed(account, now);
                ReleaseElsewhere(request.WorldId, account, now);
                return Task.FromResult(new PlaceDecision(PlaceOutcome.Admitted, PlaceSource.Kept, 0, TimeSpan.Zero, sweep));
            }

            int rank = w.Rank(account);
            if (request.Staff || request.Limit is null)
            {
                if (rank >= 0) RemoveFromLine(request.WorldId, w, account);
                return Task.FromResult(Readmit(PlaceSource.Taken, 0));
            }

            int limit = request.Limit.Value;
            if (rank >= 0)
            {
                if (rank < limit - w.Places.Count)
                {
                    long waited = now - _waiting[account].EnqueuedAt;
                    RemoveFromLine(request.WorldId, w, account);
                    return Task.FromResult(Readmit(PlaceSource.Turn, waited));
                }

                Release();
                w.Deadline[account] = now + (long)config.TimeoutAt(rank + 1).TotalMilliseconds;
                return Task.FromResult(Queued(rank + 1));
            }

            // Refused at its lapse a moment ago: refused again until the lockout expires.
            if (w.LockedOut(account, now))
                return Task.FromResult(new PlaceDecision(PlaceOutcome.WorldFull, PlaceSource.Taken, 0, TimeSpan.Zero, sweep, LockedOut: true));
            if (w.Line.Count == 0 && w.Places.Count < limit) return Task.FromResult(Readmit(PlaceSource.Taken, 0));
            Release();
            if (w.Line.Count >= request.QueueCapacity)
            {
                // Counted from when its place is free: the place's own end, when it is kept for its last ticket's hold.
                long free = w.Places.TryGetValue(account, out long kept) && kept > now ? kept : now;
                if (lapsed) w.Lockouts[account] = free + (long)GameAuthPolicy.WorldFullRetryAfterMin.TotalMilliseconds;
                return Task.FromResult(new PlaceDecision(PlaceOutcome.WorldFull, PlaceSource.Taken, 0, TimeSpan.Zero, sweep));
            }

            if (_waiting.TryGetValue(account, out (ushort World, long EnqueuedAt) other) && other.World != request.WorldId)
                RemoveFromLine(other.World, World(other.World), account);
            long arrival = ++w.Arrivals;
            w.Line.Add(arrival, account);
            w.Arrival[account] = arrival;
            int position = w.Rank(account) + 1;
            w.Deadline[account] = now + (long)config.TimeoutAt(position).TotalMilliseconds;
            _waiting[account] = (request.WorldId, now);
            return Task.FromResult(Queued(position));
        }
    }

    public Task RenewAsync(ushort worldId, long accountId, long fence, Guid sessionId, DateTime until, bool activation,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            long now = Now, expiry = Ms(until);
            WorldState w = World(worldId);
            // A session older than the owner renews nothing, nor does one that ended there, or older than one that did
            // (whether the place was kept or removed: the tombstone outlives the place by one lease).
            if (w.Owner(accountId, now) is { } current && (current.Fence > fence || (current.Ended && current.Fence >= fence)))
                return Task.CompletedTask;

            // Claimed at an activation, and at each heartbeat of the session that owns the place; not at a redemption alone,
            // which marks the session pending for one lease and keeps the place's unclaimed time for good.
            if (activation || w.Owner(accountId, now) is { Ended: false } live && live.Fence == fence)
            {
                w.Unclaimed.Remove(accountId);
                w.Pending.Remove(accountId);
                if (activation) w.Lockouts.Remove(accountId); // the account is playing
            }
            else
            {
                w.Pending[accountId] = now + (long)GameAuthPolicy.SessionLeaseLifetime.TotalMilliseconds;
            }

            w.Marks.TryGetValue(accountId, out string? mark);
            (long Until, string Id, bool WasHeld)? held = w.Ticket(accountId);
            if (held is { } own && activation && own.Id == sessionId.ToString("N"))
            {
                w.Marks.Remove(accountId); // the activation spends its own ticket's hold, and only its own
                held = null;
            }
            else if (mark is not null && held is null)
            {
                w.Marks.Remove(accountId);
            }

            if (held is { } hold && hold.Until > now && hold.Until > expiry) expiry = hold.Until;
            w.Places[accountId] = expiry;
            w.Owners[accountId] = (fence, null);
            if (!activation) return Task.CompletedTask;
            foreach ((ushort id, WorldState other) in OtherWorlds(worldId))
            {
                // The older sessions there are ended, place or not (lost, swept); a place there goes, but for a running hold.
                if (id == worldId || (other.Owner(accountId, now) is { } owner && owner.Fence > fence)) continue;
                if (other.Places.ContainsKey(accountId))
                {
                    if (other.Ticket(accountId) is { } running && running.Until > now)
                    {
                        other.Extend(accountId, running.Until);
                    }
                    else
                    {
                        other.Places.Remove(accountId);
                        other.Marks.Remove(accountId);
                        other.Unclaimed.Remove(accountId);
                        other.Pending.Remove(accountId);
                    }
                }

                other.Tombstone(accountId, fence, now);
            }
        }

        return Task.CompletedTask;
    }

    public Task EndAsync(ushort worldId, long accountId, long fence, PlaceEnd end, DateTime heldUntil,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            long now = Now;
            WorldState w = World(worldId);
            if (w.Owner(accountId, now) is { } current && (current.Fence > fence || (current.Ended && current.Fence == fence)))
                return Task.CompletedTask; // a newer session owns it, or this session's end was already taken (a retry)
            w.Tombstone(accountId, fence, now); // whatever the end does to the place, the session is ended there
            w.Pending.Remove(accountId);
            if (!w.Places.TryGetValue(accountId, out long expiry)) return Task.CompletedTask; // an end never creates a place
            if (expiry <= now)
            {
                w.Forget(accountId); // nor brings back one whose time has passed, swept or not
                return Task.CompletedTask;
            }

            (long Until, string Id, bool WasHeld)? ticket = w.Ticket(accountId);
            long? held = ticket?.Until;
            if (held <= now) held = null;
            if (end == PlaceEnd.Hold && !w.LockedOut(accountId, now)) // no held grace while a lockout lives
            {
                w.Extend(accountId, Ms(heldUntil));
                // A running ticket's hold keeps its mark, marked as taken over a drop: the place is held after a drop.
                w.Marks[accountId] = held is { } hold
                    ? RedisWorldPlaceStore.TicketMark(hold, Guid.ParseExact(ticket!.Value.Id, "N"), true)
                    : Held;
                w.MarkUnclaimed(accountId, now);
            }
            else if (held is { } running)
            {
                w.Extend(accountId, running);
                w.MarkUnclaimed(accountId, now);
            }
            else
            {
                w.Forget(accountId);
            }
        }

        return Task.CompletedTask;
    }

    public Task WithdrawAsync(ushort worldId, long accountId, Guid ticket, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            long now = Now;
            WorldState w = World(worldId);
            if (w.Ticket(accountId) is not { } withdrawn || withdrawn.Id != ticket.ToString("N")) return Task.CompletedTask;
            (long Fence, bool Ended)? owner = w.Owner(accountId, now);
            if (withdrawn.WasHeld && owner is null or { Ended: true })
            {
                w.Marks[accountId] = Held; // the place held after a drop is held again, to the end it has
                return Task.CompletedTask;
            }

            w.Marks.Remove(accountId);
            if (owner is null or { Ended: true })
            {
                w.Places.Remove(accountId);
                w.Unclaimed.Remove(accountId);
            }
            if (owner is { Ended: true } ended) w.Tombstone(accountId, ended.Fence, now); // rearmed: the place is gone
        }

        return Task.CompletedTask;
    }

    public Task ReleaseHeldAsync(ushort worldId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            long now = Now;
            WorldState w = World(worldId);
            if (w.Released is { } last && now >= last && now - last < (long)IWorldPlaceStore.HeldReleaseWindow.TotalMilliseconds)
                return Task.CompletedTask;
            w.Released = now;
            foreach (long account in w.Places.Keys.Where(a => w.Marks.TryGetValue(a, out string? m) && m == Held).ToList())
                w.Forget(account);
        }

        return Task.CompletedTask;
    }

    public Task RejoinAsync(ushort worldId, long accountId, Guid ticket, DateTime until, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            WorldState w = World(worldId);
            if (!w.Places.ContainsKey(accountId)) return Task.CompletedTask; // a replay never creates a place
            w.Marks.TryGetValue(accountId, out string? mark);
            if (mark == Released) return Task.CompletedTask; // released by another world's ticket: no ticket's to extend
            long expiry = Ms(until);
            w.Extend(accountId, expiry);
            if (mark == Held || mark?.StartsWith(TurnPrefix, StringComparison.Ordinal) == true) return Task.CompletedTask;
            (long Until, string Id, bool WasHeld)? held = w.Ticket(accountId);
            w.Marks[accountId] = RedisWorldPlaceStore.TicketMark(Math.Max(expiry, held?.Until ?? 0), ticket, held?.WasHeld == true);
        }

        return Task.CompletedTask;
    }

    public Task<QueueCheckIn> CheckInAsync(ushort worldId, long accountId, int? limit, bool available, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            long now = Now;
            WorldState w = World(worldId);
            PlaceSweep sweep = Sweep(worldId, w, now);
            // Its turn at once only for a place of its own, which its join keeps; a released or lapsed place is no turn.
            (bool present, bool lapsed) = Standing(w, accountId, now);
            int rank = w.Rank(accountId);
            long turnEnd = now + (long)config.TurnWindow.TotalMilliseconds;
            if (present && !lapsed)
            {
                // A place of its own needs no position; an unused turn is kept while the world is not available.
                if (rank >= 0) RemoveFromLine(worldId, w, accountId);
                if (!available && w.Marks.TryGetValue(accountId, out string? own) && Turn(own) is { } paused)
                {
                    // Never past the paused bound from when the turn was given.
                    w.Extend(accountId, Math.Min(turnEnd, paused.Given + (long)config.PausedTurn.TotalMilliseconds));
                    return Task.FromResult(new QueueCheckIn(QueueState.Paused, 1, sweep));
                }

                return Task.FromResult(new QueueCheckIn(QueueState.YourTurn, 0, sweep));
            }

            if (rank < 0) return Task.FromResult(new QueueCheckIn(QueueState.NotQueued, 0, sweep));
            if (available && (limit is null || rank < limit.Value - w.Places.Count))
            {
                long enqueuedAt = _waiting[accountId].EnqueuedAt;
                RemoveFromLine(worldId, w, accountId);
                // A place still counted is never shortened, and its end and unclaimed time are recorded so a turn given
                // back returns it; the turn is its own: no longer lapsed.
                string mark = string.Create(CultureInfo.InvariantCulture, $"{TurnPrefix}{enqueuedAt}:{now}");
                if (w.Places.TryGetValue(accountId, out long kept))
                    mark += string.Create(CultureInfo.InvariantCulture, $":{kept}:{w.UnclaimedSince(accountId) ?? 0}");
                w.Extend(accountId, turnEnd);
                w.Marks[accountId] = mark;
                w.Unclaimed.Remove(accountId);
                ReleaseElsewhere(worldId, accountId, now);
                return Task.FromResult(new QueueCheckIn(QueueState.YourTurn, 0, sweep));
            }

            w.Deadline[accountId] = now + (long)config.TimeoutAt(rank + 1).TotalMilliseconds;
            return Task.FromResult(new QueueCheckIn(available ? QueueState.Queued : QueueState.Paused, rank + 1, sweep));
        }
    }

    public Task<bool> LeaveAsync(ushort worldId, long accountId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            WorldState w = World(worldId);
            bool removed = w.Rank(accountId) >= 0;
            if (removed) RemoveFromLine(worldId, w, accountId);
            if (w.Marks.TryGetValue(accountId, out string? mark) && Turn(mark) is not null)
            {
                GiveBack(w, accountId, Now);
                removed = true;
            }

            return Task.FromResult(removed);
        }
    }

    public Task<WorldPlaceCounts> CountAsync(ushort worldId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            WorldState w = World(worldId);
            PlaceSweep sweep = Sweep(worldId, w, Now);
            return Task.FromResult(new WorldPlaceCounts(w.Places.Count, w.Line.Count, w.Marks.Values.Count(m => m == Held), sweep));
        }
    }

    /// <summary>
    /// A turn's mark: when the account joined the line, when the turn was given and, for a turn given over a place it
    /// still had counted, that place's end and unclaimed time (0 for none); null for any other mark.
    /// </summary>
    private static (long Enqueued, long Given, long? Ends, long Since)? Turn(string? mark)
    {
        if (mark is null || !mark.StartsWith(TurnPrefix, StringComparison.Ordinal)) return null;
        long[] parts = [.. mark[2..].Split(':').Select(part => long.Parse(part, CultureInfo.InvariantCulture))];
        return parts.Length == 4 ? (parts[0], parts[1], parts[2], parts[3]) : (parts[0], parts[1], null, 0);
    }

    /// <summary>
    /// An unused turn given back: the place it was given over, while its own end is ahead, is counted again to that end
    /// as released, with its unclaimed time; otherwise the place goes.
    /// </summary>
    private static void GiveBack(WorldState w, long account, long now)
    {
        if (w.Marks.TryGetValue(account, out string? mark) && Turn(mark) is { Ends: { } ends } turn && ends > now)
        {
            w.Places[account] = ends;
            w.Marks[account] = Released;
            if (turn.Since > 0) w.Unclaimed[account] = turn.Since;
            else w.Unclaimed.Remove(account);
            return;
        }

        w.Forget(account);
    }

    /// <summary>
    /// One reservation per account across worlds: what the account holds in other worlds with no live session goes, a
    /// place only a ticket holds (released while its hold runs) and an unused turn (given back). Held places, a place a
    /// ticket returned to from a drop, and a live session's stay.
    /// </summary>
    private void ReleaseElsewhere(ushort worldId, long account, long now)
    {
        foreach ((_, WorldState other) in OtherWorlds(worldId))
        {
            if (other.Owner(account, now) is { Ended: false }) continue;
            if (other.Marks.TryGetValue(account, out string? mark) && Turn(mark) is not null)
            {
                GiveBack(other, account, now);
                continue;
            }

            if (other.Ticket(account) is not { WasHeld: false } hold) continue;
            if (hold.Until > now)
            {
                // Its ticket may still be redeemed: counted to the hold's end, released, kept by no further ticket.
                other.Places[account] = Math.Max(other.Places.GetValueOrDefault(account), hold.Until); // never shortened
                other.Marks[account] = Released;
            }
            else
            {
                other.Forget(account);
            }
        }
    }

    /// <summary>
    /// Whether the account has a place (not one another world's ticket released) and whether it lapsed: unclaimed past
    /// the horizon, kept by tickets alone, with no session pending there (or one pending under a lockout).
    /// </summary>
    private (bool Present, bool Lapsed) Standing(WorldState w, long account, long now)
    {
        bool present = w.Places.ContainsKey(account) && !(w.Marks.TryGetValue(account, out string? kind) && kind == Released);
        bool pending = w.IsPending(account, now) && !w.LockedOut(account, now);
        bool lapsed = present && w.UnclaimedSince(account) is { } since &&
            now - since >= (long)config.UnclaimedPlace.TotalMilliseconds && !pending;
        return (present, lapsed);
    }

    /// <summary>The worlds an activation or a ticket reaches beyond <paramref name="worldId"/>.</summary>
    private List<(ushort Id, WorldState World)> OtherWorlds(ushort worldId) => configuredWorlds is null
        ? [.. _worlds.Where(w => w.Key != worldId).Select(w => (w.Key, w.Value))]
        : [.. configuredWorlds.Distinct().Where(id => id != worldId).Select(id => (id, World(id)))];

    private PlaceSweep Sweep(ushort worldId, WorldState w, long now)
    {
        int turns = 0;
        foreach (long account in w.Places.Where(p => p.Value <= now).Select(p => p.Key).ToList())
        {
            if (w.Marks.TryGetValue(account, out string? mark) && mark.StartsWith(TurnPrefix, StringComparison.Ordinal)) turns++;
            // Expired unclaimed past the horizon: kept by tickets alone to the end, so locked out as at a lapse.
            if (w.UnclaimedSince(account) is { } since && now - since >= (long)config.UnclaimedPlace.TotalMilliseconds)
                w.Lockouts[account] = now + (long)GameAuthPolicy.WorldFullRetryAfterMin.TotalMilliseconds;
            w.Forget(account);
        }

        var silent = w.Deadline.Where(d => d.Value <= now).Select(d => d.Key).ToList();
        foreach (long account in silent) RemoveFromLine(worldId, w, account);
        return new PlaceSweep(turns, silent.Count);
    }

    private void RemoveFromLine(ushort worldId, WorldState w, long account)
    {
        if (w.Arrival.Remove(account, out long arrival)) w.Line.Remove(arrival);
        w.Deadline.Remove(account);
        if (_waiting.TryGetValue(account, out (ushort World, long EnqueuedAt) waiting) && waiting.World == worldId)
            _waiting.Remove(account);
    }
}
