using Avalon.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Avalon.Infrastructure.WorldPlaces;

/// <summary>
/// <see cref="IWorldPlaceStore"/> in Redis (#967): every operation one Lua script, so "check the limit and take a place"
/// and "leave the line and take a place" are single steps. Time is the caller's <see cref="TimeProvider"/>, passed in as
/// Unix milliseconds. Single-instance Redis only: joining a line leaves the line of another world, which the script
/// names from the account's waiting key (<see cref="CacheKeys.WorldQueueWaiting"/>), not from KEYS.
/// </summary>
public sealed class RedisWorldPlaceStore(IReplicatedCache cache, IOptions<WorldPlacesConfiguration> options,
    IOptions<GameWorkloadConfiguration> workloads, TimeProvider clock) : IWorldPlaceStore
{
    private const long WaitingKeySeconds = 86_400;

    /// <summary>The helpers every script that sweeps starts with. The line keys of another world are spelled as in CacheKeys.</summary>
    private const string Functions = """
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
            redis.call('HDEL', marks, account)
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
    //       8 front timeout ms, 9 back timeout ms, 10 waiting-key prefix, 11 world id, 12 waiting-key lifetime s.
    // Returns {outcome, source, position, waited ms, turns expired, waiters timed out}.
    private const string Enter = Functions + """
        local account, now = ARGV[1], tonumber(ARGV[2])
        local turns, silent = sweep(KEYS[1], KEYS[2], KEYS[3], KEYS[4], ARGV[2], ARGV[10], ARGV[11])
        local function admit(source, waited)
          redis.call('ZADD', KEYS[1], ARGV[3], account)
          redis.call('HDEL', KEYS[2], account)
          return {0, source, 0, waited, turns, silent}
        end
        local function leave_line()
          redis.call('ZREM', KEYS[3], account)
          redis.call('ZREM', KEYS[4], account)
          forget(KEYS[6], ARGV[11])
        end
        if redis.call('ZSCORE', KEYS[1], account) then
          local mark = redis.call('HGET', KEYS[2], account)
          if mark == 'h' then return admit(2, 0) end
          if mark and string.sub(mark, 1, 2) == 't:' then return admit(3, now - tonumber(string.sub(mark, 3))) end
          return admit(1, 0)
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

    // KEYS: 1 places, 2 marks of the world, then places and marks of each other world. ARGV: 1 account, 2 until.
    private const string Renew = """
        redis.call('ZADD', KEYS[1], ARGV[2], ARGV[1])
        redis.call('HDEL', KEYS[2], ARGV[1])
        for i = 3, #KEYS, 2 do
          redis.call('ZREM', KEYS[i], ARGV[1])
          redis.call('HDEL', KEYS[i + 1], ARGV[1])
        end
        return 1
        """;

    // KEYS: 1 places, 2 marks. ARGV: 1 account, 2 '1' to hold, 3 held until.
    private const string End = """
        if ARGV[2] == '1' then
          redis.call('ZADD', KEYS[1], ARGV[3], ARGV[1])
          redis.call('HSET', KEYS[2], ARGV[1], 'h')
        else
          redis.call('ZREM', KEYS[1], ARGV[1])
          redis.call('HDEL', KEYS[2], ARGV[1])
        end
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
          redis.call('ZREM', KEYS[1], ARGV[1])
          redis.call('HDEL', KEYS[2], ARGV[1])
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
        RedisResult[] r = await RunArrayAsync(Enter,
        [
            .. WorldKeys(request.WorldId), CacheKeys.WorldQueueArrivals(request.WorldId), CacheKeys.WorldQueueWaiting(request.AccountId),
        ],
        [
            request.AccountId, Now, Ms(request.Until), Limit(request.Limit), request.Staff ? "1" : "0",
            request.QueueCapacity, WorldPlacesConfiguration.FrontPositions, Ms(c.TimeoutAt(1)),
            Ms(c.TimeoutAt(WorldPlacesConfiguration.FrontPositions + 1)), CacheKeys.WorldQueueWaitingPrefix,
            (int)request.WorldId, WaitingKeySeconds,
        ], cancellationToken);
        return new((PlaceOutcome)(int)r[0], (PlaceSource)(int)r[1], (int)r[2], TimeSpan.FromMilliseconds((long)r[3]),
            new((int)r[4], (int)r[5]));
    }

    public async Task RenewAsync(ushort worldId, long accountId, DateTime until, bool leaveOtherWorlds, CancellationToken cancellationToken)
    {
        var keys = new List<RedisKey> { CacheKeys.WorldPlaces(worldId), CacheKeys.WorldPlaceMarks(worldId) };
        if (leaveOtherWorlds)
        {
            foreach (GameServerDefinition server in workloads.Value.Servers.Where(s => s.WorldId != worldId))
            {
                keys.Add(CacheKeys.WorldPlaces(server.WorldId));
                keys.Add(CacheKeys.WorldPlaceMarks(server.WorldId));
            }
        }

        await RunAsync(Renew, [.. keys], [accountId, Ms(until)], cancellationToken);
    }

    public Task EndAsync(ushort worldId, long accountId, PlaceEnd end, DateTime heldUntil, CancellationToken cancellationToken) =>
        RunAsync(End, [CacheKeys.WorldPlaces(worldId), CacheKeys.WorldPlaceMarks(worldId)],
            [accountId, end == PlaceEnd.Hold ? "1" : "0", Ms(heldUntil)], cancellationToken);

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
    private static long Ms(TimeSpan span) => (long)span.TotalMilliseconds;
    private static long Ms(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

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
