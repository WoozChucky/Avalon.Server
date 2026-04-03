using Avalon.Common.Threading;
using Xunit;

namespace Avalon.Shared.UnitTests.Common.Threading;

public class RingBufferShould
{
    [Fact]
    public void ThrowWhenCapacityIsZero()
    {
        Assert.Throws<ArgumentException>(() => new RingBuffer<int>("test", 0));
    }

    [Fact]
    public void ThrowWhenCapacityIsNegative()
    {
        Assert.Throws<ArgumentException>(() => new RingBuffer<int>("test", -1));
    }

    [Fact]
    public async Task DequeueReturnEnqueuedItem()
    {
        var buffer = new RingBuffer<string>("test", 4);

        buffer.Enqueue("hello");
        var result = await buffer.DequeueAsync();

        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task PreserveFifoOrder()
    {
        var buffer = new RingBuffer<int>("test", 8);

        buffer.Enqueue(1);
        buffer.Enqueue(2);
        buffer.Enqueue(3);

        Assert.Equal(1, await buffer.DequeueAsync());
        Assert.Equal(2, await buffer.DequeueAsync());
        Assert.Equal(3, await buffer.DequeueAsync());
    }

    [Fact]
    public async Task AcceptItemsUpToCapacity()
    {
        var buffer = new RingBuffer<int>("test", 3);

        buffer.Enqueue(10);
        buffer.Enqueue(20);
        buffer.Enqueue(30);

        Assert.Equal(10, await buffer.DequeueAsync());
        Assert.Equal(20, await buffer.DequeueAsync());
        Assert.Equal(30, await buffer.DequeueAsync());
    }

    [Fact]
    public async Task CancelDequeueViaToken()
    {
        var buffer = new RingBuffer<int>("test", 4);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => buffer.DequeueAsync(cts.Token));
    }

    [Fact]
    public async Task CancelDequeueAfterTimeout()
    {
        var buffer = new RingBuffer<int>("test", 4);
        using var cts = new CancellationTokenSource(millisecondsDelay: 50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => buffer.DequeueAsync(cts.Token));
    }

    [Fact]
    public async Task MultipleEnqueueDequeueRoundTrip()
    {
        var buffer = new RingBuffer<string>("test", 10);

        for (int i = 0; i < 5; i++)
            buffer.Enqueue($"item-{i}");

        for (int i = 0; i < 5; i++)
        {
            var result = await buffer.DequeueAsync();
            Assert.Equal($"item-{i}", result);
        }
    }
}
