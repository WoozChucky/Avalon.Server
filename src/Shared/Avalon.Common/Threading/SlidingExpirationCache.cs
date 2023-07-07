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
        var success = _cache.TryGetValue(key, out var tuple);
        value = tuple.Value;

        if (success)
        {
            // Update the LastAccessed time to "reset" the sliding expiration
            _cache.TryUpdate(key, (value, DateTime.UtcNow), tuple);
        }
        
        return success;
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
