using Avalon.Common.Threading;
using Xunit;

namespace Avalon.Shared.UnitTests.Common.Threading;

public class LockedQueueShould
{
    private class Item(string label)
    {
        public string Label { get; } = label;
        public override string ToString() => Label;
    }

    [Fact]
    public void BeEmptyInitially()
    {
        var queue = new LockedQueue<Item>();

        Assert.True(queue.IsEmpty());
        Assert.False(queue.Next(out _));
    }

    [Fact]
    public void ReturnItemsInFifoOrder()
    {
        var queue = new LockedQueue<Item>();
        var first = new Item("first");
        var second = new Item("second");
        var third = new Item("third");

        queue.Add(first);
        queue.Add(second);
        queue.Add(third);

        Assert.True(queue.Next(out var r1));
        Assert.True(queue.Next(out var r2));
        Assert.True(queue.Next(out var r3));

        Assert.Same(first, r1);
        Assert.Same(second, r2);
        Assert.Same(third, r3);
    }

    [Fact]
    public void ReturnFalseFromNextWhenEmpty()
    {
        var queue = new LockedQueue<Item>();

        var result = queue.Next(out var item);

        Assert.False(result);
        Assert.Null(item);
    }

    [Fact]
    public void ReaddItemsToFrontInOriginalOrder()
    {
        var queue = new LockedQueue<Item>();
        var existing = new Item("existing");
        queue.Add(existing);

        var readdItems = new List<Item> { new("readd-1"), new("readd-2") };
        queue.Readd(readdItems);

        // After Readd, order should be: readd-1, readd-2, existing
        Assert.True(queue.Next(out var first));
        Assert.True(queue.Next(out var second));
        Assert.True(queue.Next(out var third));

        Assert.Equal("readd-1", first!.Label);
        Assert.Equal("readd-2", second!.Label);
        Assert.Equal("existing", third!.Label);
    }

    [Fact]
    public void DequeueWhenPredicatePasses()
    {
        var queue = new LockedQueue<Item>();
        var item = new Item("ready");
        queue.Add(item);

        var result = queue.Next(out var dequeued, _ => true);

        Assert.True(result);
        Assert.Same(item, dequeued);
        Assert.True(queue.IsEmpty());
    }

    [Fact]
    public void NotDequeueWhenPredicateFails()
    {
        var queue = new LockedQueue<Item>();
        var item = new Item("not-ready");
        queue.Add(item);

        var result = queue.Next(out var dequeued, _ => false);

        Assert.False(result);
        Assert.Null(dequeued);
        Assert.False(queue.IsEmpty());
    }

    [Fact]
    public void PeekReturnsFrontWithoutRemoving()
    {
        var queue = new LockedQueue<Item>();
        var item = new Item("peek-me");
        queue.Add(item);

        var peeked = queue.Peek();

        Assert.Same(item, peeked);
        Assert.False(queue.IsEmpty());
    }

    [Fact]
    public void PopFrontRemovesLeadingItem()
    {
        var queue = new LockedQueue<Item>();
        queue.Add(new Item("a"));
        queue.Add(new Item("b"));

        queue.PopFront();

        Assert.True(queue.Next(out var remaining));
        Assert.Equal("b", remaining!.Label);
    }

    [Fact]
    public void PopFrontIsNoOpOnEmptyQueue()
    {
        var queue = new LockedQueue<Item>();

        var exception = Record.Exception(() => queue.PopFront());

        Assert.Null(exception);
    }

    [Fact]
    public void ReportCancelledStateCorrectly()
    {
        var queue = new LockedQueue<Item>();

        Assert.False(queue.IsCancelled());

        queue.Cancel();

        Assert.True(queue.IsCancelled());
    }

    [Fact]
    public void BeNotEmptyAfterAdd()
    {
        var queue = new LockedQueue<Item>();
        queue.Add(new Item("x"));

        Assert.False(queue.IsEmpty());
    }

    [Fact]
    public void NextWithPredicateOnEmptyQueueReturnsFalse()
    {
        var queue = new LockedQueue<Item>();

        var result = queue.Next(out var item, _ => true);

        Assert.False(result);
        Assert.Null(item);
    }

    [Fact]
    public void ReaddEmptyListIsNoOp()
    {
        var queue = new LockedQueue<Item>();
        queue.Add(new Item("a"));

        queue.Readd(new List<Item>());

        Assert.True(queue.Next(out var item));
        Assert.Equal("a", item!.Label);
    }
}
