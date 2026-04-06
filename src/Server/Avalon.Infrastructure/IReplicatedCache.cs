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
