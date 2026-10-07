using Avalon.Common.Queues;
using Xunit;

namespace Avalon.Shared.UnitTests.Common.Collections;

public class SimplePriorityQueueShould
{
    [Fact]
    public void Dequeue_by_priority_then_by_insertion_order()
    {
        var queue = new SimplePriorityQueue<string, int>();
        Assert.Equal(0, queue.Count);

        queue.Enqueue("low", 10);
        queue.Enqueue("high", 1);
        queue.Enqueue("medium", 5);
        queue.Enqueue("medium-later", 5);
        Assert.Equal(4, queue.Count);

        Assert.Equal("high", queue.Dequeue());
        Assert.Equal("medium", queue.Dequeue());
        Assert.Equal("medium-later", queue.Dequeue());
        Assert.True(queue.TryDequeue(out string? last));
        Assert.Equal("low", last);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Reorder_on_a_priority_change_and_drop_a_removed_item()
    {
        var queue = new SimplePriorityQueue<string, int>();
        queue.Enqueue("a", 10);
        queue.Enqueue("b", 5);
        queue.Enqueue("gone", 7);

        queue.UpdatePriority("a", 1);
        queue.Remove("gone");

        Assert.False(queue.Contains("gone"));
        Assert.True(queue.Contains("b"));
        Assert.Equal("a", queue.Dequeue());
        Assert.Equal("b", queue.Dequeue());
    }

    [Fact]
    public void Peek_at_the_head_without_removing_it()
    {
        var queue = new SimplePriorityQueue<string, int>();
        queue.Enqueue("a", 5);
        queue.Enqueue("b", 1);

        Assert.Equal("b", queue.First);
        Assert.True(queue.TryFirst(out string? first));
        Assert.Equal("b", first);
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public void Refuse_to_read_or_remove_from_an_empty_queue()
    {
        var queue = new SimplePriorityQueue<string, int>();

        Assert.Throws<InvalidOperationException>(() => queue.Dequeue());
        Assert.Throws<InvalidOperationException>(() => _ = queue.First);
        Assert.Throws<InvalidOperationException>(() => queue.Remove("not-there"));
        Assert.False(queue.TryDequeue(out string? dequeued));
        Assert.Null(dequeued);
        Assert.False(queue.TryFirst(out string? first));
        Assert.Null(first);
    }

    [Fact]
    public void Enqueue_an_item_only_once_without_duplicates()
    {
        var queue = new SimplePriorityQueue<string, int>();

        Assert.True(queue.EnqueueWithoutDuplicates("x", 1));
        Assert.False(queue.EnqueueWithoutDuplicates("x", 2));
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void Enumerate_every_item_and_clear_them_all()
    {
        var queue = new SimplePriorityQueue<string, int>();
        queue.Enqueue("c", 3);
        queue.Enqueue("a", 1);
        queue.Enqueue("b", 2);

        Assert.Equal(["a", "b", "c"], queue.ToList().Order(StringComparer.Ordinal));

        queue.Clear();
        Assert.Equal(0, queue.Count);
        Assert.False(queue.Contains("a"));
    }
}
