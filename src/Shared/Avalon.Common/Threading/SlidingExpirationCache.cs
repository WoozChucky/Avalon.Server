using System.Collections.Concurrent;

namespace Avalon.Common.Threading;

public class SlidingExpirationCache<TKey, TValue> : IDisposable
{
    private readonly ConcurrentDictionary<TKey, (TValue Value, DateTime LastAccessed)> _cache;
    private readonly TimeSpan _expiration;
    private readonly Timer _timer;

    public SlidingExpirationCache(TimeSpan expiration)
    {
        _cache = new ConcurrentDictionary<TKey, (TValue Value, DateTime LastAccessed)>();
        _expiration = expiration;
        _timer = new Timer(RemoveExpiredEntries, null, expiration, expiration);
    }

    public void AddOrUpdate(TKey key, TValue value)
    {
        _cache.AddOrUpdate(key, (value, DateTime.UtcNow), (k, v) => (value, DateTime.UtcNow));
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        if (!_cache.TryGetValue(key, out var tuple))
        {
            value = default!;
            return false;
        }

        // Lazy expiration: evict the entry immediately if it has expired,
        // regardless of whether the background timer has run yet.
        if (DateTime.UtcNow - tuple.LastAccessed > _expiration)
        {
            _cache.TryRemove(key, out _);
            value = default!;
            return false;
        }

        value = tuple.Value;

        // Reset the sliding window on access.
        _cache.TryUpdate(key, (value, DateTime.UtcNow), tuple);

        return true;
    }

    private void RemoveExpiredEntries(object state)
    {
        foreach (var entry in _cache)
        {
            if (DateTime.UtcNow - entry.Value.LastAccessed > _expiration)
            {
                _cache.TryRemove(entry.Key, out _);
            }
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
    }

}
