using StackExchange.Redis;

namespace Avalon.Infrastructure.GameAuth;

/// <summary>Shared Redis transactions cover handoff consumption, attempts, context creation and credential rotation together.</summary>
public sealed class RedisGameContextStore(IReplicatedCache cache, TimeProvider clock) : IGameContextStore
{
    private const string Exchange = """
        for i = 1, #KEYS do
          local offset = (i - 1) * 5
          local current = redis.call('GET', KEYS[i])
          if ARGV[offset + 1] == '0' then
            if current then return 0 end
          elseif current ~= ARGV[offset + 2] then return 0 end
        end
        for i = 1, #KEYS do
          local offset = (i - 1) * 5
          if ARGV[offset + 3] == '0' then redis.call('DEL', KEYS[i])
          else redis.call('SET', KEYS[i], ARGV[offset + 4], 'PX', ARGV[offset + 5]) end
        end
        return 1
        """;

    public async Task<string?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await cache.Database.StringGetAsync(key).WaitAsync(cancellationToken);
        return value.IsNull ? null : (string?)value;
    }

    public async Task<bool> CompareExchangeAsync(IReadOnlyList<GameAuthMutation> mutations, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (mutations.Count is < 1 or > 16 || mutations.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != mutations.Count)
            throw new ArgumentException("Invalid atomic game-auth write set.", nameof(mutations));
        var now = clock.GetUtcNow().UtcDateTime;
        var keys = new RedisKey[mutations.Count];
        var values = new RedisValue[mutations.Count * 5];
        for (var i = 0; i < mutations.Count; i++)
        {
            var mutation = mutations[i];
            if (mutation.Value is not null && mutation.ExpiresAt <= now) return false;
            if (mutation.Key.Length > 256 || mutation.Expected?.Length > 16384 || mutation.Value?.Length > 16384)
                throw new ArgumentException("Game-auth entry exceeds its bound.", nameof(mutations));
            keys[i] = mutation.Key;
            values[i * 5] = mutation.Expected is null ? "0" : "1";
            values[i * 5 + 1] = mutation.Expected ?? "";
            values[i * 5 + 2] = mutation.Value is null ? "0" : "1";
            values[i * 5 + 3] = mutation.Value ?? "";
            values[i * 5 + 4] = Math.Max(1L, (long)(mutation.ExpiresAt - now).TotalMilliseconds);
        }
        return (long)await cache.Database.ScriptEvaluateAsync(Exchange, keys, values).WaitAsync(cancellationToken) == 1;
    }
}
