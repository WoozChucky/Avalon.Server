using Avalon.Common.Queues;
using Xunit;

namespace Avalon.Shared.UnitTests.Common.Collections;

public class SimplePriorityQueueShould
{
    [Fact]
    public void BeEmptyInitially()
    {
        var queue = new SimplePriorityQueue<string, int>();

        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void ReturnItemsInPriorityOrder()
    {
        var queue = new SimplePriorityQueue<string, int>();

        queue.Enqueue("low", 10);
        queue.Enqueue("high", 1);
        queue.Enqueue("medium", 5);

        Assert.Equal("high", queue.Dequeue());
        Assert.Equal("medium", queue.Dequeue());
        Assert.Equal("low", queue.Dequeue());
    }

    [Fact]
    public void IncrementCountOnEnqueue()
    {
        var queue = new SimplePriorityQueue<string, int>();

        queue.Enqueue("a", 1);
        queue.Enqueue("b", 2);

        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public void DecrementCountOnDequeue()
    {
        var queue = new SimplePriorityQueue<string, int>();
        queue.Enqueue("a", 1);

        queue.Dequeue();

        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void ContainEnqueuedItem()
    {
        var queue = new SimplePriorityQueue<string, int>();
        queue.Enqueue("hello", 1);

        Assert.True(queue.Contains("hello"));
        Assert.False(queue.Contains("world"));
    }

    [Fact]
    public void RemoveSpecificItem()
    {
        var queue = new SimplePriorityQueue<string, int>();
        queue.Enqueue("keep", 1);
        queue.Enqueue("remove", 2);

        queue.Remove("remove");

        Assert.Equal(1, queue.Count);
        Assert.False(queue.Contains("remove"));
        Assert.True(queue.Contains("keep"));
    }

    [Fact]
    public void ThrowOnDequeueWhenEmpty()
    {
        var queue = new SimplePriorityQueue<string, int>();

        Assert.Throws<InvalidOperationException>(() => queue.Dequeue());
    }

    [Fact]
    public void TryDequeueReturnsTrueWhenNonEmpty()
    {
        var queue = new SimplePriorityQueue<string, int>();
        queue.Enqueue("item", 1);

        var result = queue.TryDequeue(out var dequeued);

        Assert.True(result);
        Assert.Equal("item", dequeued);
    }

    [Fact]
    public void TryDequeueReturnsFalseWhenEmpty()
    {
        var queue = new SimplePriorityQueue<string, int>();

        var result = queue.TryDequeue(out var dequeued);

        Assert.False(result);
        Assert.Null(dequeued);
    }

    [Fact]
    public void FirstReturnsHeadWithoutRemoving()
    {
        var queue = new SimplePriorityQueue<string, int>();
        queue.Enqueue("a", 5);
        queue.Enqueue("b", 1);

        var first = queue.First;

        Assert.Equal("b", first);
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public void ClearRemovesAllItems()
    {
        var queue = new SimplePriorityQueue<string, int>();
        queue.Enqueue("a", 1);
        queue.Enqueue("b", 2);

        queue.Clear();

        Assert.Equal(0, queue.Count);
        Assert.False(queue.Contains("a"));
    }

    [Fact]
    public void BreakTiesByInsertionOrder()
    {
        var queue = new SimplePriorityQueue<string, int>();

        queue.Enqueue("first", 1);
        queue.Enqueue("second", 1);

        Assert.Equal("first", queue.Dequeue());
        Assert.Equal("second", queue.Dequeue());
    }

    [Fact]
    public void UpdatePriorityChangesOrder()
    {
        var queue = new SimplePriorityQueue<string, int>();
        queue.Enqueue("a", 10);
        queue.Enqueue("b", 5);

        queue.UpdatePriority("a", 1);

        Assert.Equal("a", queue.Dequeue()); // now highest priority
    }

    [Fact]
    public void EnqueueWithoutDuplicatesReturnsTrueOnFirstInsert()
    {
        var queue = new SimplePriorityQueue<string, int>();

        bool result = queue.EnqueueWithoutDuplicates("x", 1);

        Assert.True(result);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void EnqueueWithoutDuplicatesReturnsFalseOnDuplicate()
    {
        var queue = new SimplePriorityQueue<string, int>();
        queue.Enqueue("x", 1);

        bool result = queue.EnqueueWithoutDuplicates("x", 2);

        Assert.False(result);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void TryFirstReturnsTrueAndHeadWhenNonEmpty()
    {
        var queue = new SimplePriorityQueue<string, int>();
        queue.Enqueue("a", 5);
        queue.Enqueue("b", 1);

        var found = queue.TryFirst(out var first);

        Assert.True(found);
        Assert.Equal("b", first);
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public void TryFirstReturnsFalseWhenEmpty()
    {
        var queue = new SimplePriorityQueue<string, int>();

        var found = queue.TryFirst(out var first);

        Assert.False(found);
        Assert.Null(first);
    }

    [Fact]
    public void RemoveThrowsIfItemNotEnqueued()
    {
        var queue = new SimplePriorityQueue<string, int>();

        Assert.Throws<InvalidOperationException>(() => queue.Remove("not-there"));
    }

    [Fact]
    public void FirstThrowsWhenEmpty()
    {
        var queue = new SimplePriorityQueue<string, int>();

        Assert.Throws<InvalidOperationException>(() => _ = queue.First);
    }

    [Fact]
    public void EnumerationYieldsAllItems()
    {
        var queue = new SimplePriorityQueue<string, int>();
        queue.Enqueue("c", 3);
        queue.Enqueue("a", 1);
        queue.Enqueue("b", 2);

        var items = queue.ToList();

        Assert.Equal(3, items.Count);
        Assert.Contains("a", items);
        Assert.Contains("b", items);
        Assert.Contains("c", items);
    }
}
