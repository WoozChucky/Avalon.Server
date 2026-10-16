using System.Globalization;
using Avalon.Common.GameAuth;
using Avalon.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Avalon.Infrastructure.WorldPlaces;

/// <summary>
/// <see cref="IWorldPlaceStore"/> in Redis (#967): every operation one Lua script, so "check the limit and take a place"
/// and "leave the line and take a place" are single steps. Time is the caller's <see cref="TimeProvider"/>, passed in as
/// Unix milliseconds. Single-instance Redis only: joining a line leaves the line of another world, which the script
/// names from the account's waiting key (<see cref="CacheKeys.WorldQueueWaiting"/>), not from KEYS.
/// <para>
/// Who holds a place is in the world's marks hash (<see cref="CacheKeys.WorldPlaceMarks"/>), two fields per account at
/// most. The field <c>{account}</c> is the place's kind: <c>h</c> (held after a drop), <c>t:{enqueuedAtMs}</c> (a turn
/// not yet used) or <see cref="TicketMark"/> (a join ticket's hold, the last ticket to enter). The field
/// <c>{account}:s</c> is the fencing token of the session that owns the place's lease (<c>{fence}</c>), or of the session
/// that ended there (<c>e:{fence}</c>, a tombstone Redis expires one lease after it was written, <c>HPEXPIRE</c>, whether the
/// place was kept or removed); fencing tokens only grow for an account, so a smaller one is an older session's.
/// </para>
/// </summary>
public sealed class RedisWorldPlaceStore(IReplicatedCache cache, IOptions<WorldPlacesConfiguration> options,
    IOptions<GameWorkloadConfiguration> workloads, TimeProvider clock, PlaceLedgerOutage outage) : IWorldPlaceStore
{
    private const long WaitingKeySeconds = 86_400;

    /// <summary>
    /// The mark of a join ticket's hold on a place: <c>j:{untilMs}:{ticket}</c>, the ticket by the id (<c>N</c> format)
    /// of the session it becomes, so a withdrawal or an activation only ever touches its own ticket's hold. A ticket that
    /// took a place held after a drop carries <c>:h</c> after its id (<paramref name="wasHeld"/>), so its withdrawal holds
    /// the place again rather than removing it.
    /// </summary>
    public static string TicketMark(long untilMs, Guid ticket, bool wasHeld = false) =>
        string.Create(CultureInfo.InvariantCulture, $"j:{untilMs}:{ticket:N}{(wasHeld ? ":h" : "")}");

    /// <summary>
    /// What every script reads a place's holders with: a ticket's hold (its end and id) from the kind field, the owning
    /// session's fencing token from the <c>{account}:s</c> field (<c>{fence}</c> while it is live, <c>e:{fence}</c> once it
    /// ended), and the removal of a place with both its fields. An ended session's field is a tombstone: written only by
    /// <c>tombstone</c>, which always sets it to expire one lease from now on Redis's clock (<c>HPEXPIRE</c>), so it refuses that
    /// session's late renewals whether or not the place is still there, and never outlives them. (A plain <c>HSET</c> of a
    /// newer session's live fence clears the expiry, as it should: the field then lives with the place.) A refused expiry
    /// sets <c>refused</c>, which the scripts that write tombstones return beside their result, so it is logged.
    /// </summary>
    private const string Holders = """
        local refused = 0
        -- A ticket's hold: its end, its id, and whether the place was held after a drop when the ticket took it.
        local function ticket(mark)
          if mark then
            local ends, id, was = string.match(mark, '^j:(%d+):(%x+)(:?h?)$')
            if ends then return tonumber(ends), id, was == ':h' end
          end
          return nil, nil, false
        end
        local function ticket_mark(ends, id, was)
          if was then return 'j:' .. ends .. ':' .. id .. ':h' end
          return 'j:' .. ends .. ':' .. id
        end
        local function owner(marks, account)
          local value = redis.call('HGET', marks, account .. ':s')
          if not value then return nil, false end
          if string.sub(value, 1, 2) == 'e:' then return tonumber(string.sub(value, 3)), true end
          return tonumber(value), false
        end
        -- The place's fields go with it, but an ended session's tombstone, which expires on its own.
        local function forget_fields(marks, account)
          redis.call('HDEL', marks, account)
          local _, ended = owner(marks, account)
          if not ended then redis.call('HDEL', marks, account .. ':s') end
        end
        local function forget_place(places, marks, account)
          redis.call('ZREM', places, account)
          forget_fields(marks, account)
        end
        -- An ended session's tombstone, expiring one lease (ttl ms) from now on Redis's own clock, so no clock skew between
        -- identity and Redis can cut it short. Should the expiry fail (HPEXPIRE refused, or a Redis before 7.4), the field
        -- is removed rather than left without one, whatever it held before (an earlier tombstone too): never a field that
        -- lasts forever. The refusal is returned, and logged by the caller.
        local function tombstone(marks, account, fence, ttl)
          redis.call('HSET', marks, account .. ':s', 'e:' .. fence)
          local armed = redis.pcall('HPEXPIRE', marks, ttl, 'FIELDS', 1, account .. ':s')
          if type(armed) == 'table' and armed.err then
            redis.call('HDEL', marks, account .. ':s')
            refused = 1
          end
        end

        """;

    /// <summary>The helpers every script that sweeps starts with. The line keys of another world are spelled as in CacheKeys.</summary>
    private const string Functions = Holders + """
        local function forget(key, world)
          local waiting = redis.call('GET', key)
          if waiting and string.sub(waiting, 1, #world + 1) == world .. ':' then redis.call('DEL', key) end
        end
        local function enqueued_at(key, now)
          local waiting = redis.call('GET', key)
          return (waiting and string.match(waiting, ':(%d+)$')) or now
        end
        local function timeout(position, front, front_ms, back_ms)
          if position <= front then return front_ms end
          return back_ms
        end
        local function sweep(places, marks, queue, checkins, now, prefix, world)
          local turns = 0
          local expired = redis.call('ZRANGEBYSCORE', places, '-inf', now)
          for _, account in ipairs(expired) do
            local mark = redis.call('HGET', marks, account)
            if mark and string.sub(mark, 1, 2) == 't:' then turns = turns + 1 end
            forget_fields(marks, account)
          end
          if #expired > 0 then redis.call('ZREMRANGEBYSCORE', places, '-inf', now) end
          local silent = redis.call('ZRANGEBYSCORE', checkins, '-inf', now)
          for _, account in ipairs(silent) do
            redis.call('ZREM', queue, account)
            forget(prefix .. account, world)
          end
          if #silent > 0 then redis.call('ZREMRANGEBYSCORE', checkins, '-inf', now) end
          return turns, #silent
        end

        """;

    // KEYS: 1 places, 2 marks, 3 queue, 4 checkins, 5 arrivals, 6 the account's waiting key.
    // ARGV: 1 account, 2 now, 3 until, 4 limit ('' for none), 5 staff ('1'/'0'), 6 queue capacity, 7 front positions,
    //       8 front timeout ms, 9 back timeout ms, 10 waiting-key prefix, 11 world id, 12 waiting-key lifetime s,
    //       13 the ticket's id (N format).
    // Returns {outcome, source, position, waited ms, turns expired, waiters timed out}.
    private const string Enter = Functions + """
        local account, now = ARGV[1], tonumber(ARGV[2])
        local turns, silent = sweep(KEYS[1], KEYS[2], KEYS[3], KEYS[4], ARGV[2], ARGV[10], ARGV[11])
        -- Every place a ticket is admitted to is never shortened (a held place's grace, a turn, an owner's lease), and is
        -- the ticket's to hold. The owner field is left as it is: on a new place it can only be an ended session's
        -- tombstone, which keeps refusing that session's late renewals until it expires.
        local function admit(source, waited, was)
          redis.call('ZADD', KEYS[1], 'GT', ARGV[3], account)
          redis.call('HSET', KEYS[2], account, ticket_mark(ARGV[3], ARGV[13], was))
          return {0, source, 0, waited, turns, silent}
        end
        local function leave_line()
          redis.call('ZREM', KEYS[3], account)
          redis.call('ZREM', KEYS[4], account)
          forget(KEYS[6], ARGV[11])
        end
        if redis.call('ZSCORE', KEYS[1], account) then
          local mark = redis.call('HGET', KEYS[2], account)
          if mark == 'h' then return admit(2, 0, true) end
          if mark and string.sub(mark, 1, 2) == 't:' then return admit(3, now - tonumber(string.sub(mark, 3))) end
          -- Kept: never shortened, and held by the last ticket to enter, to the later of its end and the hold already
          -- there, so the withdrawal of a ticket that entered before it never matches.
          redis.call('ZADD', KEYS[1], 'GT', ARGV[3], account)
          local ends = tonumber(ARGV[3])
          local held, _, was = ticket(mark)
          if held and held > ends then ends = held end
          redis.call('HSET', KEYS[2], account, ticket_mark(string.format('%d', ends), ARGV[13], was))
          return {0, 1, 0, 0, turns, silent}
        end
        local rank = redis.call('ZRANK', KEYS[3], account)
        local limit = tonumber(ARGV[4])
        if ARGV[5] == '1' or not limit then
          if rank then leave_line() end
          return admit(0, 0)
        end
        local taken = redis.call('ZCARD', KEYS[1])
        local front, front_ms, back_ms = tonumber(ARGV[7]), tonumber(ARGV[8]), tonumber(ARGV[9])
        if rank then
          if rank < limit - taken then
            local waited = now - tonumber(enqueued_at(KEYS[6], ARGV[2]))
            leave_line()
            return admit(3, waited)
          end
          redis.call('ZADD', KEYS[4], now + timeout(rank + 1, front, front_ms, back_ms), account)
          return {1, 0, rank + 1, 0, turns, silent}
        end
        local waiting = redis.call('ZCARD', KEYS[3])
        if waiting == 0 and taken < limit then return admit(0, 0) end
        if waiting >= tonumber(ARGV[6]) then return {2, 0, 0, 0, turns, silent} end
        local previous = redis.call('GET', KEYS[6])
        local other = previous and string.match(previous, '^(%d+):')
        if other and other ~= ARGV[11] then
          redis.call('ZREM', 'world:' .. other .. ':queue', account)
          redis.call('ZREM', 'world:' .. other .. ':queue:checkins', account)
        end
        redis.call('ZADD', KEYS[3], redis.call('INCR', KEYS[5]), account)
        local position = redis.call('ZRANK', KEYS[3], account) + 1
        redis.call('ZADD', KEYS[4], now + timeout(position, front, front_ms, back_ms), account)
        redis.call('SET', KEYS[6], ARGV[11] .. ':' .. ARGV[2], 'EX', ARGV[12])
        return {1, 0, position, 0, turns, silent}
        """;

    // KEYS: 1 places, 2 marks of the world, then places and marks of each other world (an activation only).
    // ARGV: 1 account, 2 until (the lease), 3 '1' for an activation, 4 the session's fencing token, 5 the session's id
    //       (N format), 6 now, 7 a tombstone's lifetime in ms (one lease).
    // Returns {1 when the session owns the place now, 0 when it changed nothing; 1 when a tombstone's expiry was refused}.
    private const string Renew = Holders + """
        local account, expiry, fence, now = ARGV[1], tonumber(ARGV[2]), tonumber(ARGV[4]), tonumber(ARGV[6])
        local current, ended = owner(KEYS[2], account)
        -- A session older than the place's owner renews nothing, nor does a session that ended there, or older than one
        -- that did (whether the place was kept or removed: the tombstone outlives the place by one lease).
        if current and (current > fence or (ended and current >= fence)) then return {0, refused} end
        local mark = redis.call('HGET', KEYS[2], account)
        local held, id = ticket(mark)
        if held and ARGV[3] == '1' and id == ARGV[5] then
          -- The activation spends its own ticket's hold, and only its own.
          redis.call('HDEL', KEYS[2], account)
          held = nil
        elseif mark and not held then
          redis.call('HDEL', KEYS[2], account)
        end
        if held and held > now and held > expiry then expiry = held end
        redis.call('ZADD', KEYS[1], expiry, account)
        redis.call('HSET', KEYS[2], account .. ':s', ARGV[4])
        for i = 3, #KEYS, 2 do
          -- A session is one per account: its place in another world goes, but for a ticket's hold still running there,
          -- and the older sessions there are ended, place or not (lost, swept), so a late renewal of one cannot bring a
          -- place back.
          local other = owner(KEYS[i + 1], account)
          if not other or other <= fence then
            if redis.call('ZSCORE', KEYS[i], account) then
              local hold = ticket(redis.call('HGET', KEYS[i + 1], account))
              if hold and hold > now then
                redis.call('ZADD', KEYS[i], 'GT', hold, account)
              else
                redis.call('ZREM', KEYS[i], account)
                redis.call('HDEL', KEYS[i + 1], account)
              end
            end
            tombstone(KEYS[i + 1], account, ARGV[4], ARGV[7])
          end
        end
        return {1, refused}
        """;

    // KEYS: 1 places, 2 marks. ARGV: 1 account, 2 '1' to hold, 3 held until, 4 now, 5 the ending session's fencing token,
    //       6 a tombstone's lifetime in ms (one lease).
    // Returns {1 when the place was the session's to end, 0 when a newer session owns it, this session's end was already
    // taken, or the place no longer exists; 1 when the tombstone's expiry was refused}.
    private const string End = Holders + """
        local account, now, fence = ARGV[1], tonumber(ARGV[4]), tonumber(ARGV[5])
        local current, ended = owner(KEYS[2], account)
        if current and current > fence then return {0, refused} end
        -- The same session's end again (a retry): the first one decided the place, and a repeat never extends a hold.
        if ended and current == fence then return {0, refused} end
        -- Whatever the end does to the place, the session is ended there: its late renewals change nothing.
        tombstone(KEYS[2], account, ARGV[5], ARGV[6])
        -- An end never creates a place: one lost or removed meanwhile stays gone, and so does one whose time has passed,
        -- swept or not.
        local score = redis.call('ZSCORE', KEYS[1], account)
        if not score then return {0, refused} end
        if tonumber(score) <= now then
          forget_place(KEYS[1], KEYS[2], account)
          return {0, refused}
        end
        local held = ticket(redis.call('HGET', KEYS[2], account))
        if held and held <= now then held = nil end
        if ARGV[2] == '1' then
          redis.call('ZADD', KEYS[1], 'GT', ARGV[3], account)
          -- A ticket's hold still running keeps its mark, so the session it becomes can renew the place.
          if not held then redis.call('HSET', KEYS[2], account, 'h') end
        elseif held then
          redis.call('ZADD', KEYS[1], 'GT', held, account)
        else
          redis.call('ZREM', KEYS[1], account)
          redis.call('HDEL', KEYS[2], account)
        end
        return {1, refused}
        """;

    // KEYS: 1 places, 2 marks. ARGV: 1 account, 2 the withdrawn ticket's id (N format), 3 a tombstone's lifetime in ms (one
    //       lease).
    // Returns {1 when the hold was the ticket's and is given back, 0 when another ticket entered since; 1 when the
    // tombstone's expiry was refused}.
    private const string Withdraw = Holders + """
        local _, id, was = ticket(redis.call('HGET', KEYS[2], ARGV[1]))
        if id ~= ARGV[2] then return {0, refused} end
        local current, ended = owner(KEYS[2], ARGV[1])
        if was and (not current or ended) then
          -- The ticket took a place held after a drop: it is held again, to the end it has, never removed.
          redis.call('HSET', KEYS[2], ARGV[1], 'h')
          return {1, refused}
        end
        redis.call('HDEL', KEYS[2], ARGV[1])
        if not current or ended then redis.call('ZREM', KEYS[1], ARGV[1]) end
        -- An ended session's tombstone stays, rearmed for one lease: the place it was kept for is gone.
        if ended then tombstone(KEYS[2], ARGV[1], string.format('%d', current), ARGV[3]) end
        return {1, refused}
        """;

    // KEYS: 1 places, 2 marks. ARGV: 1 account, 2 until, 3 the replayed ticket's id (N format).
    // Returns 1 when the place was there to extend, 0 when there is none: a replay never creates one.
    private const string Rejoin = Holders + """
        if not redis.call('ZSCORE', KEYS[1], ARGV[1]) then return 0 end
        redis.call('ZADD', KEYS[1], 'GT', ARGV[2], ARGV[1])
        local mark = redis.call('HGET', KEYS[2], ARGV[1])
        if mark == 'h' or (mark and string.sub(mark, 1, 2) == 't:') then return 1 end
        local ends = tonumber(ARGV[2])
        local held, _, was = ticket(mark)
        if held and held > ends then ends = held end
        redis.call('HSET', KEYS[2], ARGV[1], ticket_mark(string.format('%d', ends), ARGV[3], was))
        return 1
        """;

    // KEYS: 1 places, 2 marks, 3 queue, 4 checkins, 5 the account's waiting key.
    // ARGV: 1 account, 2 now, 3 limit ('' for none), 4 available ('1'/'0'), 5 front positions, 6 front timeout ms,
    //       7 back timeout ms, 8 the turn's end, 9 waiting-key prefix, 10 world id.
    // Returns {state, position, turns expired, waiters timed out}.
    private const string CheckIn = Functions + """
        local account, now = ARGV[1], tonumber(ARGV[2])
        local turns, silent = sweep(KEYS[1], KEYS[2], KEYS[3], KEYS[4], ARGV[2], ARGV[9], ARGV[10])
        if redis.call('ZSCORE', KEYS[1], account) then return {3, 0, turns, silent} end
        local rank = redis.call('ZRANK', KEYS[3], account)
        if not rank then return {0, 0, turns, silent} end
        local limit = tonumber(ARGV[3])
        if ARGV[4] == '1' and (not limit or rank < limit - redis.call('ZCARD', KEYS[1])) then
          local enqueued = enqueued_at(KEYS[5], ARGV[2])
          redis.call('ZREM', KEYS[3], account)
          redis.call('ZREM', KEYS[4], account)
          forget(KEYS[5], ARGV[10])
          redis.call('ZADD', KEYS[1], ARGV[8], account)
          redis.call('HSET', KEYS[2], account, 't:' .. enqueued)
          return {3, 0, turns, silent}
        end
        local position = rank + 1
        redis.call('ZADD', KEYS[4], now + timeout(position, tonumber(ARGV[5]), tonumber(ARGV[6]), tonumber(ARGV[7])), account)
        if ARGV[4] == '1' then return {1, position, turns, silent} end
        return {2, position, turns, silent}
        """;

    // KEYS: 1 places, 2 marks, 3 queue, 4 checkins, 5 the account's waiting key. ARGV: 1 account, 2 world id.
    private const string Leave = Functions + """
        local removed = redis.call('ZREM', KEYS[3], ARGV[1])
        redis.call('ZREM', KEYS[4], ARGV[1])
        forget(KEYS[5], ARGV[2])
        local mark = redis.call('HGET', KEYS[2], ARGV[1])
        if mark and string.sub(mark, 1, 2) == 't:' then
          forget_place(KEYS[1], KEYS[2], ARGV[1])
          removed = 1
        end
        return removed
        """;

    // KEYS: 1 places, 2 marks, 3 queue, 4 checkins. ARGV: 1 now, 2 waiting-key prefix, 3 world id.
    // Returns {taken, queued, held, turns expired, waiters timed out}.
    private const string Count = Functions + """
        local turns, silent = sweep(KEYS[1], KEYS[2], KEYS[3], KEYS[4], ARGV[1], ARGV[2], ARGV[3])
        local held = 0
        for _, mark in ipairs(redis.call('HVALS', KEYS[2])) do
          if mark == 'h' then held = held + 1 end
        end
        return {redis.call('ZCARD', KEYS[1]), redis.call('ZCARD', KEYS[3]), held, turns, silent}
        """;

    public async Task<PlaceDecision> EnterAsync(PlaceRequest request, CancellationToken cancellationToken)
    {
        WorldPlacesConfiguration c = options.Value;
        long until = Ms(request.Until);
        RedisResult[] r = await RunArrayAsync(Enter,
        [
            .. WorldKeys(request.WorldId), CacheKeys.WorldQueueArrivals(request.WorldId), CacheKeys.WorldQueueWaiting(request.AccountId),
        ],
        [
            request.AccountId, Now, until, Limit(request.Limit), request.Staff ? "1" : "0",
            request.QueueCapacity, WorldPlacesConfiguration.FrontPositions, Ms(c.TimeoutAt(1)),
            Ms(c.TimeoutAt(WorldPlacesConfiguration.FrontPositions + 1)), CacheKeys.WorldQueueWaitingPrefix,
            (int)request.WorldId, WaitingKeySeconds, request.Ticket.ToString("N"),
        ], cancellationToken);
        return new((PlaceOutcome)(int)r[0], (PlaceSource)(int)r[1], (int)r[2], TimeSpan.FromMilliseconds((long)r[3]),
            new((int)r[4], (int)r[5]));
    }

    public async Task RenewAsync(ushort worldId, long accountId, long fence, Guid sessionId, DateTime until, bool activation,
        CancellationToken cancellationToken)
    {
        var keys = new List<RedisKey> { CacheKeys.WorldPlaces(worldId), CacheKeys.WorldPlaceMarks(worldId) };
        if (activation)
        {
            // One pair per other world, however many workloads name it.
            foreach (ushort other in workloads.Value.Servers.Select(s => s.WorldId).Where(id => id != worldId).Distinct())
            {
                keys.Add(CacheKeys.WorldPlaces(other));
                keys.Add(CacheKeys.WorldPlaceMarks(other));
            }
        }

        await RunTombstoningAsync(Renew, [.. keys],
            [accountId, Ms(until), activation ? "1" : "0", fence, sessionId.ToString("N"), Now, s_tombstoneLifetime], cancellationToken);
    }

    public Task EndAsync(ushort worldId, long accountId, long fence, PlaceEnd end, DateTime heldUntil,
        CancellationToken cancellationToken) =>
        RunTombstoningAsync(End, [CacheKeys.WorldPlaces(worldId), CacheKeys.WorldPlaceMarks(worldId)],
            [accountId, end == PlaceEnd.Hold ? "1" : "0", Ms(heldUntil), Now, fence, s_tombstoneLifetime], cancellationToken);

    public Task WithdrawAsync(ushort worldId, long accountId, Guid ticket, CancellationToken cancellationToken) =>
        RunTombstoningAsync(Withdraw, [CacheKeys.WorldPlaces(worldId), CacheKeys.WorldPlaceMarks(worldId)],
            [accountId, ticket.ToString("N"), s_tombstoneLifetime], cancellationToken);

    /// <summary>A script that may write tombstones: an expiry Redis refused (the second result) is logged, throttled.</summary>
    private async Task RunTombstoningAsync(string script, RedisKey[] keys, RedisValue[] values, CancellationToken cancellationToken)
    {
        RedisResult[] r = await RunArrayAsync(script, keys, values, cancellationToken);
        if ((int)r[1] == 1) outage.ReportTombstoneRefused();
    }

    public Task RejoinAsync(ushort worldId, long accountId, Guid ticket, DateTime until, CancellationToken cancellationToken) =>
        RunAsync(Rejoin, [CacheKeys.WorldPlaces(worldId), CacheKeys.WorldPlaceMarks(worldId)],
            [accountId, Ms(until), ticket.ToString("N")], cancellationToken);

    public async Task<QueueCheckIn> CheckInAsync(ushort worldId, long accountId, int? limit, bool available, CancellationToken cancellationToken)
    {
        WorldPlacesConfiguration c = options.Value;
        long now = Now;
        RedisResult[] r = await RunArrayAsync(CheckIn, [.. WorldKeys(worldId), CacheKeys.WorldQueueWaiting(accountId)],
        [
            accountId, now, Limit(limit), available ? "1" : "0", WorldPlacesConfiguration.FrontPositions,
            Ms(c.TimeoutAt(1)), Ms(c.TimeoutAt(WorldPlacesConfiguration.FrontPositions + 1)),
            now + Ms(c.TurnWindow), CacheKeys.WorldQueueWaitingPrefix, (int)worldId,
        ], cancellationToken);
        return new((QueueState)(int)r[0], (int)r[1], new((int)r[2], (int)r[3]));
    }

    public async Task<bool> LeaveAsync(ushort worldId, long accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RedisResult result = await cache.Database.ScriptEvaluateAsync(Leave,
                [.. WorldKeys(worldId), CacheKeys.WorldQueueWaiting(accountId)], [accountId, (int)worldId])
            .WaitAsync(cancellationToken);
        return (long)result == 1;
    }

    public async Task<WorldPlaceCounts> CountAsync(ushort worldId, CancellationToken cancellationToken)
    {
        RedisResult[] r = await RunArrayAsync(Count, WorldKeys(worldId), [Now, CacheKeys.WorldQueueWaitingPrefix, (int)worldId],
            cancellationToken);
        return new((int)r[0], (int)r[1], (int)r[2], new((int)r[3], (int)r[4]));
    }

    private long Now => clock.GetUtcNow().ToUnixTimeMilliseconds();

    /// <summary>
    /// How long an ended session's tombstone lasts, in ms, counted by Redis from the moment it is written (<c>HPEXPIRE</c>,
    /// relative, so no skew between identity's clock and Redis's shortens it): one lease, past any renewal that session
    /// still has in flight, since a renewal is answered within its lease.
    /// </summary>
    private static readonly long s_tombstoneLifetime = Ms(GameAuthPolicy.SessionLeaseLifetime);
    private static long Ms(TimeSpan span) => (long)span.TotalMilliseconds;

    /// <summary>A UTC time as Unix milliseconds, as the scripts and the hold marks spell it.</summary>
    public static long Ms(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    /// <summary>A limit for a script: the number, or the empty string for none, which Lua's <c>tonumber</c> reads as nil.</summary>
    private static RedisValue Limit(int? limit) => limit is { } value ? value : RedisValue.EmptyString;

    // RedisValue has implicit conversions from int and long but not from ushort, hence the (int) casts on world ids.
    private static RedisKey[] WorldKeys(ushort worldId) =>
    [
        CacheKeys.WorldPlaces(worldId), CacheKeys.WorldPlaceMarks(worldId), CacheKeys.WorldQueue(worldId),
        CacheKeys.WorldQueueCheckIns(worldId),
    ];

    private async Task RunAsync(string script, RedisKey[] keys, RedisValue[] values, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await cache.Database.ScriptEvaluateAsync(script, keys, values).WaitAsync(cancellationToken);
    }

    private async Task<RedisResult[]> RunArrayAsync(string script, RedisKey[] keys, RedisValue[] values, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RedisResult result = await cache.Database.ScriptEvaluateAsync(script, keys, values).WaitAsync(cancellationToken);
        return (RedisResult[])result!;
    }
}
