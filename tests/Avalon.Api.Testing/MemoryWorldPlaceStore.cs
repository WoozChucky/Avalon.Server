using System.Globalization;
using Avalon.Common.GameAuth;
using Avalon.Configuration;
using Avalon.Infrastructure.WorldPlaces;

namespace Avalon.Api.Testing;

/// <summary>
/// <see cref="IWorldPlaceStore"/> in memory, every operation under one lock: the reference the Redis scripts are held to by
/// <c>WorldPlaceScenarios</c>. The same steps, in the same order, as <c>RedisWorldPlaceStore</c>'s scripts.
/// </summary>
public sealed class MemoryWorldPlaceStore(TimeProvider clock, WorldPlacesConfiguration config) : IWorldPlaceStore
{
    private const string Held = "h";
    private const string TurnPrefix = "t:";

    private readonly Lock _gate = new();
    private readonly Dictionary<ushort, WorldState> _worlds = [];
    private readonly Dictionary<long, (ushort World, long EnqueuedAt)> _waiting = [];

    private sealed class WorldState
    {
        public readonly Dictionary<long, long> Places = [];      // account -> expiry
        public readonly Dictionary<long, string> Marks = [];     // account -> "h", "t:{enqueuedAt}" or a ticket's hold mark
        // account -> the owning session, or the one that ended there (a tombstone, until its expiry)
        public readonly Dictionary<long, (long Fence, long? TombstoneUntil)> Owners = [];
        public readonly SortedList<long, long> Line = [];        // arrival -> account
        public readonly Dictionary<long, long> Arrival = [];     // account -> arrival
        public readonly Dictionary<long, long> Deadline = [];    // account -> removal deadline
        public long Arrivals;
        public int Rank(long account) => Arrival.TryGetValue(account, out long arrival) ? Line.IndexOfKey(arrival) : -1;

        /// <summary>ZADD GT: the place's end, never moved earlier; a missing place is added.</summary>
        public void Extend(long account, long expiry) =>
            Places[account] = Places.TryGetValue(account, out long current) ? Math.Max(current, expiry) : expiry;

        /// <summary>The place and its fields, but an ended session's tombstone, which expires on its own.</summary>
        public void Forget(long account)
        {
            Places.Remove(account);
            Marks.Remove(account);
            if (Owners.TryGetValue(account, out (long Fence, long? TombstoneUntil) owner) && owner.TombstoneUntil is null)
                Owners.Remove(account);
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

        /// <summary>The ticket's hold on the account's place, its end and its id, when the mark is one.</summary>
        public (long Until, string Id)? Ticket(long account)
        {
            if (!Marks.TryGetValue(account, out string? mark) || !mark.StartsWith("j:", StringComparison.Ordinal)) return null;
            int colon = mark.IndexOf(':', 2);
            return (long.Parse(mark[2..colon], CultureInfo.InvariantCulture), mark[(colon + 1)..]);
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
            PlaceDecision Admit(PlaceSource source, long waited)
            {
                // Every place a ticket is admitted to is never shortened, and is the ticket's to hold. The owner is left
                // as it is: on a new place it can only be an ended session's tombstone.
                w.Extend(account, until);
                w.Marks[account] = hold;
                return new(PlaceOutcome.Admitted, source, 0, TimeSpan.FromMilliseconds(waited), sweep);
            }

            PlaceDecision Queued(int position) => new(PlaceOutcome.Queued, PlaceSource.Taken, position, TimeSpan.Zero, sweep);

            if (w.Places.ContainsKey(account))
            {
                w.Marks.TryGetValue(account, out string? mark);
                if (mark == Held) return Task.FromResult(Admit(PlaceSource.Returned, 0));
                if (mark?.StartsWith(TurnPrefix, StringComparison.Ordinal) == true)
                    return Task.FromResult(Admit(PlaceSource.Turn, now - long.Parse(mark[2..], CultureInfo.InvariantCulture)));
                // Kept: never shortened, and held by the last ticket to enter, to the later of its end and the hold there.
                w.Extend(account, until);
                w.Marks[account] = RedisWorldPlaceStore.TicketMark(Math.Max(until, w.Ticket(account)?.Until ?? 0), request.Ticket);
                return Task.FromResult(new PlaceDecision(PlaceOutcome.Admitted, PlaceSource.Kept, 0, TimeSpan.Zero, sweep));
            }

            int rank = w.Rank(account);
            if (request.Staff || request.Limit is null)
            {
                if (rank >= 0) RemoveFromLine(request.WorldId, w, account);
                return Task.FromResult(Admit(PlaceSource.Taken, 0));
            }

            int limit = request.Limit.Value;
            if (rank >= 0)
            {
                if (rank < limit - w.Places.Count)
                {
                    long waited = now - _waiting[account].EnqueuedAt;
                    RemoveFromLine(request.WorldId, w, account);
                    return Task.FromResult(Admit(PlaceSource.Turn, waited));
                }

                w.Deadline[account] = now + (long)config.TimeoutAt(rank + 1).TotalMilliseconds;
                return Task.FromResult(Queued(rank + 1));
            }

            if (w.Line.Count == 0 && w.Places.Count < limit) return Task.FromResult(Admit(PlaceSource.Taken, 0));
            if (w.Line.Count >= request.QueueCapacity)
                return Task.FromResult(new PlaceDecision(PlaceOutcome.WorldFull, PlaceSource.Taken, 0, TimeSpan.Zero, sweep));

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

            w.Marks.TryGetValue(accountId, out string? mark);
            (long Until, string Id)? held = w.Ticket(accountId);
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
            foreach ((ushort id, WorldState other) in _worlds)
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
            if (w.Owner(accountId, now) is { } current && current.Fence > fence) return Task.CompletedTask;
            w.Tombstone(accountId, fence, now); // whatever the end does to the place, the session is ended there
            if (!w.Places.ContainsKey(accountId)) return Task.CompletedTask; // an end never creates a place
            long? held = w.Ticket(accountId)?.Until;
            if (held <= now) held = null;
            if (end == PlaceEnd.Hold)
            {
                w.Extend(accountId, Ms(heldUntil));
                if (held is null) w.Marks[accountId] = Held;
            }
            else if (held is { } running)
            {
                w.Extend(accountId, running);
            }
            else
            {
                w.Places.Remove(accountId);
                w.Marks.Remove(accountId);
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
            if (w.Ticket(accountId)?.Id != ticket.ToString("N")) return Task.CompletedTask;
            w.Marks.Remove(accountId);
            (long Fence, bool Ended)? owner = w.Owner(accountId, now);
            if (owner is null or { Ended: true }) w.Places.Remove(accountId);
            if (owner is { Ended: true } ended) w.Tombstone(accountId, ended.Fence, now); // rearmed: the place is gone
        }

        return Task.CompletedTask;
    }

    public Task RejoinAsync(ushort worldId, long accountId, Guid ticket, DateTime until, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            WorldState w = World(worldId);
            if (!w.Places.ContainsKey(accountId)) return Task.CompletedTask; // a replay never creates a place
            long expiry = Ms(until);
            w.Extend(accountId, expiry);
            w.Marks.TryGetValue(accountId, out string? mark);
            if (mark == Held || mark?.StartsWith(TurnPrefix, StringComparison.Ordinal) == true) return Task.CompletedTask;
            w.Marks[accountId] = RedisWorldPlaceStore.TicketMark(Math.Max(expiry, w.Ticket(accountId)?.Until ?? 0), ticket);
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
            if (w.Places.ContainsKey(accountId)) return Task.FromResult(new QueueCheckIn(QueueState.YourTurn, 0, sweep));
            int rank = w.Rank(accountId);
            if (rank < 0) return Task.FromResult(new QueueCheckIn(QueueState.NotQueued, 0, sweep));
            if (available && (limit is null || rank < limit.Value - w.Places.Count))
            {
                long enqueuedAt = _waiting[accountId].EnqueuedAt;
                RemoveFromLine(worldId, w, accountId);
                w.Places[accountId] = now + (long)config.TurnWindow.TotalMilliseconds;
                w.Marks[accountId] = TurnPrefix + enqueuedAt.ToString(CultureInfo.InvariantCulture);
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
            if (w.Marks.TryGetValue(accountId, out string? mark) && mark.StartsWith(TurnPrefix, StringComparison.Ordinal))
            {
                w.Forget(accountId);
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

    private PlaceSweep Sweep(ushort worldId, WorldState w, long now)
    {
        int turns = 0;
        foreach (long account in w.Places.Where(p => p.Value <= now).Select(p => p.Key).ToList())
        {
            if (w.Marks.TryGetValue(account, out string? mark) && mark.StartsWith(TurnPrefix, StringComparison.Ordinal)) turns++;
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
