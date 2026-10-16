using System.Globalization;
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
    private const string TicketPrefix = "j:";

    private readonly Lock _gate = new();
    private readonly Dictionary<ushort, WorldState> _worlds = [];
    private readonly Dictionary<long, (ushort World, long EnqueuedAt)> _waiting = [];

    private sealed class WorldState
    {
        public readonly Dictionary<long, long> Places = [];      // account -> expiry
        public readonly Dictionary<long, string> Marks = [];     // account -> "h", "t:{enqueuedAt}" or "j:{ticket place expiry}"
        public readonly SortedList<long, long> Line = [];        // arrival -> account
        public readonly Dictionary<long, long> Arrival = [];     // account -> arrival
        public readonly Dictionary<long, long> Deadline = [];    // account -> removal deadline
        public long Arrivals;
        public int Rank(long account) => Arrival.TryGetValue(account, out long arrival) ? Line.IndexOfKey(arrival) : -1;
    }

    private long Now => clock.GetUtcNow().ToUnixTimeMilliseconds();
    private static long Ms(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
    private WorldState World(ushort id) => _worlds.TryGetValue(id, out WorldState? w) ? w : _worlds[id] = new WorldState();

    /// <summary>Until when a join ticket holds the account's place, when its mark says one does.</summary>
    private static long? Ticket(WorldState w, long account) =>
        w.Marks.TryGetValue(account, out string? mark) && mark.StartsWith(TicketPrefix, StringComparison.Ordinal)
            ? long.Parse(mark[2..], CultureInfo.InvariantCulture) : null;

    private static string TicketMark(long until) => TicketPrefix + until.ToString(CultureInfo.InvariantCulture);

    public Task<PlaceDecision> EnterAsync(PlaceRequest request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            long now = Now, until = Ms(request.Until), account = request.AccountId;
            WorldState w = World(request.WorldId);
            PlaceSweep sweep = Sweep(request.WorldId, w, now);
            PlaceDecision Admit(PlaceSource source, long waited)
            {
                w.Places[account] = until;
                w.Marks[account] = TicketMark(until);
                return new(PlaceOutcome.Admitted, source, 0, TimeSpan.FromMilliseconds(waited), sweep);
            }

            PlaceDecision Queued(int position) => new(PlaceOutcome.Queued, PlaceSource.Taken, position, TimeSpan.Zero, sweep);

            if (w.Places.TryGetValue(account, out long expiry))
            {
                w.Marks.TryGetValue(account, out string? mark);
                if (mark == Held) return Task.FromResult(Admit(PlaceSource.Returned, 0));
                if (mark?.StartsWith(TurnPrefix, StringComparison.Ordinal) == true)
                    return Task.FromResult(Admit(PlaceSource.Turn, now - long.Parse(mark[2..], CultureInfo.InvariantCulture)));
                // Kept: never shortened (a live session's lease, or an earlier ticket's), and held by this ticket too.
                w.Places[account] = Math.Max(expiry, until);
                if (Ticket(w, account) is not { } held || held < until) w.Marks[account] = TicketMark(until);
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

    public Task RenewAsync(ushort worldId, long accountId, DateTime until, bool leaveOtherWorlds, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            WorldState w = World(worldId);
            long expiry = Ms(until);
            long? held = Ticket(w, accountId);
            if (leaveOtherWorlds || held is null) w.Marks.Remove(accountId);
            else if (held > expiry) expiry = held.Value;
            w.Places[accountId] = expiry;
            if (leaveOtherWorlds)
            {
                foreach ((ushort id, WorldState other) in _worlds)
                {
                    if (id == worldId) continue;
                    other.Places.Remove(accountId);
                    other.Marks.Remove(accountId);
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task EndAsync(ushort worldId, long accountId, PlaceEnd end, DateTime heldUntil, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            WorldState w = World(worldId);
            if (end == PlaceEnd.Withdraw)
            {
                if (w.Marks.TryGetValue(accountId, out string? mark) && mark == TicketMark(Ms(heldUntil)))
                {
                    w.Places.Remove(accountId);
                    w.Marks.Remove(accountId);
                }

                return Task.CompletedTask;
            }

            long? held = Ticket(w, accountId);
            if (held <= Now) held = null;
            if (end == PlaceEnd.Hold)
            {
                w.Places[accountId] = Math.Max(Ms(heldUntil), held ?? 0);
                w.Marks[accountId] = Held;
            }
            else if (held is { } ticket)
            {
                // A ticket issued since (a takeover or a reconnect not yet redeemed) still holds the place.
                w.Places[accountId] = ticket;
            }
            else
            {
                w.Places.Remove(accountId);
                w.Marks.Remove(accountId);
            }
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
                w.Places.Remove(accountId);
                w.Marks.Remove(accountId);
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
            w.Places.Remove(account);
            w.Marks.Remove(account);
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
