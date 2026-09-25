using Avalon.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Avalon.Infrastructure;

public interface IReplicatedCache
{
    IDatabase Database { get; }
    Task ConnectAsync();
    Task DisconnectAsync();
    Task<bool> SetAsync(string key, string value, TimeSpan? expiry);
    /// <summary>Sets the key only if it does not already exist (atomic SETNX). Returns true if the key was set.</summary>
    Task<bool> SetNxAsync(string key, string value, TimeSpan expiry);
    Task<string?> GetAsync(string key);
    /// <summary>
    /// Atomically increments the counter at <paramref name="key"/> and returns the new value. The
    /// expiry is set only by the increment that creates the key, so the window is fixed from the
    /// first increment and later ones do not extend it.
    /// </summary>
    Task<long> IncrementAsync(string key, TimeSpan window);
    /// <summary>
    /// Atomically decrements the counter at <paramref name="key"/>, never below zero, keeping its
    /// expiry. A missing key stays missing. Returns the new value.
    /// </summary>
    Task<long> DecrementFloorAsync(string key);
    /// <summary>
    /// Atomically raises the counter at <paramref name="key"/> to at least <paramref name="floor"/>
    /// and sets its expiry to <paramref name="window"/> from now, recreating the key if it has
    /// expired or never existed. Returns the new value.
    /// </summary>
    Task<long> HoldCounterAtLeastAsync(string key, long floor, TimeSpan window);
    /// <summary>
    /// Atomically deletes the counter at <paramref name="key"/>, but only while its value is below
    /// <paramref name="limit"/>. Returns true when it deleted the key; a missing key, or one at or
    /// above the limit, is left as it is.
    /// </summary>
    Task<bool> RemoveCounterIfBelowAsync(string key, long limit);
    /// <summary>
    /// Atomically increments <paramref name="field"/> of the hash at <paramref name="key"/> and
    /// returns the new value, but only while the hash exists: returns -1, creating nothing, when
    /// it does not, so an expired hash is never recreated without its expiry.
    /// </summary>
    Task<long> HashIncrementIfExistsAsync(string key, string field);
    Task<bool> RemoveAsync(string key);
    Task<bool> KeyExistsAsync(string key);
    Task<bool> KeyExpireAsync(string key, TimeSpan expiry);
    Task<bool> KeyExpireAsync(string key, DateTime expiry);
    Task SubscribeAsync(string channel, Action<RedisChannel, RedisValue> handler);
    Task UnsubscribeAsync(string channel, Action<RedisChannel, RedisValue> handler);
    Task PublishAsync(string channel, string message);
}

public class ReplicatedCache : IReplicatedCache
{
    private readonly ILogger<ReplicatedCache> _logger;
    private readonly CacheConfiguration _configuration;

    private ConnectionMultiplexer _redis = null!;


    public ReplicatedCache(ILoggerFactory loggerFactory, IOptions<CacheConfiguration> configuration)
    {
        _logger = loggerFactory.CreateLogger<ReplicatedCache>();
        _configuration = string.IsNullOrWhiteSpace(configuration.Value.Host) ? throw new Exception("Invalid IOptions<CacheConfiguration>!") : configuration.Value;
        _logger.LogInformation("ReplicatedCache initialized with configuration: {@Configuration}", _configuration);
    }

    public IDatabase Database => _redis.GetDatabase();

    public async Task ConnectAsync()
    {
        _redis = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
        {
            EndPoints = new EndPointCollection() { _configuration.Host },
            AllowAdmin = true,
            Password = _configuration.Password
        });

        _logger.LogInformation("Connected to Redis at {Host}", _configuration.Host);
    }

    public async Task DisconnectAsync()
    {
        await _redis.CloseAsync();
        _logger.LogInformation("Disconnected from Redis at {Host}", _configuration.Host);
    }

    public async Task<bool> SetAsync(string key, string value, TimeSpan? expiry)
    {
        var expiration = new Expiration(expiry ?? TimeSpan.Zero);
        return await _redis.GetDatabase().StringSetAsync(key, value, expiration);
    }

    public async Task<bool> SetNxAsync(string key, string value, TimeSpan expiry)
    {
        return await _redis.GetDatabase().StringSetAsync(key, value, expiry, when: When.NotExists);
    }

    public async Task<string?> GetAsync(string key)
    {
        return await _redis.GetDatabase().StringGetAsync(key);
    }

    // INCR and the first PEXPIRE in one script: a client lost between the two would otherwise
    // leave a counter that never expires.
    private const string IncrementWithWindowScript =
        "local v = redis.call('INCR', KEYS[1]) " +
        "if v == 1 then redis.call('PEXPIRE', KEYS[1], ARGV[1]) end " +
        "return v";

    public async Task<long> IncrementAsync(string key, TimeSpan window)
    {
        RedisResult result = await _redis.GetDatabase().ScriptEvaluateAsync(IncrementWithWindowScript,
            [new RedisKey(key)], [(long)window.TotalMilliseconds]);
        return (long)result;
    }

    private const string DecrementFloorScript =
        "local v = tonumber(redis.call('GET', KEYS[1])) " +
        "if v and v > 0 then return redis.call('DECR', KEYS[1]) end " +
        "return 0";

    public async Task<long> DecrementFloorAsync(string key)
    {
        RedisResult result = await _redis.GetDatabase().ScriptEvaluateAsync(DecrementFloorScript, [new RedisKey(key)]);
        return (long)result;
    }

    // GET, raise and SET with PX in one script: a key that expired between the caller's INCR and
    // this call is recreated rather than left missing.
    private const string HoldCounterAtLeastScript =
        "local v = tonumber(redis.call('GET', KEYS[1])) or 0 " +
        "local m = tonumber(ARGV[1]) " +
        "if v < m then v = m end " +
        "redis.call('SET', KEYS[1], v, 'PX', ARGV[2]) " +
        "return v";

    public async Task<long> HoldCounterAtLeastAsync(string key, long floor, TimeSpan window)
    {
        RedisResult result = await _redis.GetDatabase().ScriptEvaluateAsync(HoldCounterAtLeastScript,
            [new RedisKey(key)], [floor, (long)window.TotalMilliseconds]);
        return (long)result;
    }

    // GET and DEL in one script: a hold that raised the counter to the limit between the two is
    // never deleted.
    private const string RemoveCounterIfBelowScript =
        "local v = tonumber(redis.call('GET', KEYS[1])) " +
        "if v and v < tonumber(ARGV[1]) then redis.call('DEL', KEYS[1]) return 1 end " +
        "return 0";

    public async Task<bool> RemoveCounterIfBelowAsync(string key, long limit)
    {
        RedisResult result = await _redis.GetDatabase().ScriptEvaluateAsync(RemoveCounterIfBelowScript,
            [new RedisKey(key)], [limit]);
        return (long)result == 1;
    }

    private const string HashIncrementIfExistsScript =
        "if redis.call('EXISTS', KEYS[1]) == 1 then return redis.call('HINCRBY', KEYS[1], ARGV[1], 1) end " +
        "return -1";

    public async Task<long> HashIncrementIfExistsAsync(string key, string field)
    {
        RedisResult result = await _redis.GetDatabase().ScriptEvaluateAsync(HashIncrementIfExistsScript,
            [new RedisKey(key)], [field]);
        return (long)result;
    }

    public async Task<bool> RemoveAsync(string key)
    {
        return await _redis.GetDatabase().KeyDeleteAsync(key);
    }

    public async Task<bool> KeyExistsAsync(string key)
    {
        return await _redis.GetDatabase().KeyExistsAsync(key);
    }

    public async Task<bool> KeyExpireAsync(string key, TimeSpan expiry)
    {
        return await _redis.GetDatabase().KeyExpireAsync(key, expiry);
    }

    public async Task<bool> KeyExpireAsync(string key, DateTime expiry)
    {
        return await _redis.GetDatabase().KeyExpireAsync(key, expiry);
    }

    public async Task SubscribeAsync(string channel, Action<RedisChannel, RedisValue> handler)
    {
        var sub = _redis.GetSubscriber();
        // await sub.SubscribeAsync(new RedisChannel(channel, RedisChannel.PatternMode.Auto));
        await sub.SubscribeAsync(channel, handler);
    }

    public async Task UnsubscribeAsync(string channel, Action<RedisChannel, RedisValue> handler)
    {
        var sub = _redis.GetSubscriber();
        await sub.UnsubscribeAsync(channel, handler);
    }

    public async Task PublishAsync(string channel, string message)
    {
        var sub = _redis.GetSubscriber();
        await sub.PublishAsync(channel, message);
    }
}
