using Avalon.Common.Threading;
using Xunit;

namespace Avalon.Shared.UnitTests.Common.Threading;

public class SlidingExpirationCacheShould
{
    [Fact]
    public void ReturnValueForExistingKey()
    {
        using var cache = new SlidingExpirationCache<string, int>(TimeSpan.FromMinutes(5));

        cache.AddOrUpdate("key", 42);
        var found = cache.TryGetValue("key", out var value);

        Assert.True(found);
        Assert.Equal(42, value);
    }

    [Fact]
    public void ReturnFalseForMissingKey()
    {
        using var cache = new SlidingExpirationCache<string, int>(TimeSpan.FromMinutes(5));

        var found = cache.TryGetValue("missing", out var value);

        Assert.False(found);
        Assert.Equal(default, value);
    }

    [Fact]
    public void OverwriteExistingKeyOnAddOrUpdate()
    {
        using var cache = new SlidingExpirationCache<string, string>(TimeSpan.FromMinutes(5));

        cache.AddOrUpdate("key", "original");
        cache.AddOrUpdate("key", "updated");

        var found = cache.TryGetValue("key", out var value);

        Assert.True(found);
        Assert.Equal("updated", value);
    }

    [Fact]
    public void SupportMultipleKeys()
    {
        using var cache = new SlidingExpirationCache<int, string>(TimeSpan.FromMinutes(5));

        cache.AddOrUpdate(1, "one");
        cache.AddOrUpdate(2, "two");
        cache.AddOrUpdate(3, "three");

        Assert.True(cache.TryGetValue(1, out var v1));
        Assert.True(cache.TryGetValue(2, out var v2));
        Assert.True(cache.TryGetValue(3, out var v3));

        Assert.Equal("one", v1);
        Assert.Equal("two", v2);
        Assert.Equal("three", v3);
    }

    [Fact]
    public void ExpireEntryAfterExpirationWindow()
    {
        using var cache = new SlidingExpirationCache<string, int>(TimeSpan.FromMilliseconds(50));

        cache.AddOrUpdate("expiring", 99);

        Thread.Sleep(200);

        var found = cache.TryGetValue("expiring", out _);
        Assert.False(found);
    }

    [Fact]
    public void PreserveEntryWhenAccessedBeforeExpiry()
    {
        using var cache = new SlidingExpirationCache<string, int>(TimeSpan.FromMilliseconds(200));

        cache.AddOrUpdate("sliding", 7);

        // Access the entry to reset the sliding window
        cache.TryGetValue("sliding", out _);

        // Wait less than expiry – entry should still be present
        Thread.Sleep(100);

        var found = cache.TryGetValue("sliding", out var value);
        Assert.True(found);
        Assert.Equal(7, value);
    }

    [Fact]
    public void DisposeWithoutThrowing()
    {
        var cache = new SlidingExpirationCache<string, int>(TimeSpan.FromMinutes(1));
        var exception = Record.Exception(() => cache.Dispose());

        Assert.Null(exception);
    }
}
