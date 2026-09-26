using Avalon.Infrastructure;
using NSubstitute;

namespace Avalon.Server.Auth.UnitTests.Services;

/// <summary>
/// A cache whose counters behave like the Redis scripts the budgets use, expiry included, on a
/// clock the test controls:
/// <list type="bullet">
/// <item><c>IncrementAsync</c>: INCR, with the expiry set only by the increment that creates the key.</item>
/// <item><c>DecrementFloorAsync</c>: DECR floored at zero, keeping the expiry; a missing key stays missing.</item>
/// <item><c>HoldCounterAtLeastAsync</c>: raise to the floor and SET with a fresh expiry, recreating the key.</item>
/// <item><c>KeyExpireAsync</c>: sets the expiry of a key that exists, and does nothing to one that does not.</item>
/// <item><c>DecrementCounterIfAtMostAsync</c>: DECR only while 0 &lt; value &lt;= ceiling, keeping the expiry; a held value and a missing key are left alone.</item>
/// <item><c>RemoveCounterIfBelowAsync</c>: DEL only while the value is below the held value; a missing key stays missing.</item>
/// <item><c>RemoveAsync</c>: DEL.</item>
/// </list>
/// Everything else is a plain substitute.
/// </summary>
internal sealed class CounterCache
{
    private readonly Dictionary<string, (long Value, DateTime? ExpiresAt)> _keys = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private DateTime _now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public IReplicatedCache Cache { get; } = Substitute.For<IReplicatedCache>();

    /// <summary>Runs just before a hold (either kind) reaches the key: a place to expire it, or throw.</summary>
    public Action<string>? BeforeHold { get; set; }

    public CounterCache()
    {
        Cache.IncrementAsync(Arg.Any<string>(), Arg.Any<TimeSpan>())
            .Returns(ci => Increment(ci.ArgAt<string>(0), ci.ArgAt<TimeSpan>(1)));
        Cache.DecrementFloorAsync(Arg.Any<string>())
            .Returns(ci => DecrementFloor(ci.ArgAt<string>(0)));
        Cache.HoldCounterAtLeastAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<TimeSpan>())
            .Returns(ci =>
            {
                BeforeHold?.Invoke(ci.ArgAt<string>(0));
                return HoldAtLeast(ci.ArgAt<string>(0), ci.ArgAt<long>(1), ci.ArgAt<TimeSpan>(2));
            });
        Cache.KeyExpireAsync(Arg.Any<string>(), Arg.Any<TimeSpan>())
            .Returns(ci =>
            {
                BeforeHold?.Invoke(ci.ArgAt<string>(0));
                return Expire(ci.ArgAt<string>(0), ci.ArgAt<TimeSpan>(1));
            });
        Cache.DecrementCounterIfAtMostAsync(Arg.Any<string>(), Arg.Any<long>())
            .Returns(ci =>
            {
                lock (_gate)
                {
                    string key = ci.ArgAt<string>(0);
                    Purge(key);
                    if (!_keys.TryGetValue(key, out var entry)) return 0L;
                    if (entry.Value <= 0 || entry.Value > ci.ArgAt<long>(1)) return entry.Value;
                    _keys[key] = (entry.Value - 1, entry.ExpiresAt);
                    return entry.Value - 1;
                }
            });
        Cache.RemoveCounterIfBelowAsync(Arg.Any<string>(), Arg.Any<long>())
            .Returns(ci =>
            {
                lock (_gate)
                {
                    string key = ci.ArgAt<string>(0);
                    Purge(key);
                    if (!_keys.TryGetValue(key, out var entry) || entry.Value >= ci.ArgAt<long>(1)) return false;
                    return _keys.Remove(key);
                }
            });
        Cache.RemoveAsync(Arg.Any<string>())
            .Returns(ci =>
            {
                lock (_gate)
                {
                    Purge(ci.ArgAt<string>(0));
                    return _keys.Remove(ci.ArgAt<string>(0));
                }
            });
    }

    public void Advance(TimeSpan by)
    {
        lock (_gate) _now += by;
    }

    /// <summary>Makes the key expire now, as its TTL running out would.</summary>
    public void ExpireNow(string key)
    {
        lock (_gate) _keys.Remove(key);
    }

    /// <summary>The counter's value, or 0 when the key does not exist (or has expired).</summary>
    public long CountOf(string key)
    {
        lock (_gate)
        {
            Purge(key);
            return _keys.TryGetValue(key, out var entry) ? entry.Value : 0;
        }
    }

    public bool Exists(string key)
    {
        lock (_gate)
        {
            Purge(key);
            return _keys.ContainsKey(key);
        }
    }

    /// <summary>The time the key has left, or null when it has no expiry or does not exist.</summary>
    public TimeSpan? TimeToLive(string key)
    {
        lock (_gate)
        {
            Purge(key);
            return _keys.TryGetValue(key, out var entry) && entry.ExpiresAt is { } at ? at - _now : null;
        }
    }

    /// <summary>The keys of the per-username budget this cache holds now.</summary>
    public IReadOnlyList<string> UsernameKeys
    {
        get
        {
            lock (_gate)
            {
                foreach (string key in _keys.Keys.ToList()) Purge(key);
                return _keys.Keys.Where(k => k.StartsWith("auth:username:", StringComparison.Ordinal)).ToList();
            }
        }
    }

    private long Increment(string key, TimeSpan window)
    {
        lock (_gate)
        {
            Purge(key);
            if (_keys.TryGetValue(key, out var entry))
            {
                _keys[key] = (entry.Value + 1, entry.ExpiresAt);
                return entry.Value + 1;
            }

            _keys[key] = (1, _now + window);
            return 1;
        }
    }

    private long DecrementFloor(string key)
    {
        lock (_gate)
        {
            Purge(key);
            if (!_keys.TryGetValue(key, out var entry) || entry.Value <= 0) return 0;
            _keys[key] = (entry.Value - 1, entry.ExpiresAt);
            return entry.Value - 1;
        }
    }

    private long HoldAtLeast(string key, long floor, TimeSpan window)
    {
        lock (_gate)
        {
            Purge(key);
            long value = Math.Max(_keys.TryGetValue(key, out var entry) ? entry.Value : 0, floor);
            _keys[key] = (value, _now + window);
            return value;
        }
    }

    private bool Expire(string key, TimeSpan expiry)
    {
        lock (_gate)
        {
            Purge(key);
            if (!_keys.TryGetValue(key, out var entry)) return false;
            _keys[key] = (entry.Value, _now + expiry);
            return true;
        }
    }

    private void Purge(string key)
    {
        if (_keys.TryGetValue(key, out var entry) && entry.ExpiresAt is { } at && at <= _now)
            _keys.Remove(key);
    }
}
