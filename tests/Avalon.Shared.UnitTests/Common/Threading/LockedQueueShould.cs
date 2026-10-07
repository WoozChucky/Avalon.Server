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
    public void ReturnItemsInFifoOrder()
    {
        var queue = new LockedQueue<Item>();
        var first = new Item("first");
        var second = new Item("second");
        var third = new Item("third");

        queue.Add(first);
        queue.Add(second);
        queue.Add(third);

        Assert.False(queue.IsEmpty());
        Assert.True(queue.Next(out Item? r1));
        Assert.True(queue.Next(out Item? r2));
        Assert.True(queue.Next(out Item? r3));
        Assert.Same(first, r1);
        Assert.Same(second, r2);
        Assert.Same(third, r3);
    }

    [Fact]
    public void Hand_out_nothing_from_an_empty_queue()
    {
        var queue = new LockedQueue<Item>();

        Assert.True(queue.IsEmpty());
        Assert.False(queue.Next(out Item? item));
        Assert.Null(item);
        Assert.False(queue.Next(out Item? checkedItem, _ => true));
        Assert.Null(checkedItem);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Dequeue_only_when_the_predicate_passes(bool passes)
    {
        var queue = new LockedQueue<Item>();
        var item = new Item("head");
        queue.Add(item);

        bool result = queue.Next(out Item? dequeued, _ => passes);

        Assert.Equal(passes, result);
        Assert.Equal(passes ? item : null, dequeued);
        Assert.Equal(passes, queue.IsEmpty());
    }

    [Fact]
    public void Peek_at_and_pop_the_front()
    {
        var queue = new LockedQueue<Item>();
        Assert.Null(Record.Exception(() => queue.PopFront()));
        var a = new Item("a");
        queue.Add(a);
        queue.Add(new Item("b"));

        Assert.Same(a, queue.Peek());
        queue.PopFront();

        Assert.True(queue.Next(out Item? remaining));
        Assert.Equal("b", remaining!.Label);
    }

    [Fact]
    public void ReportCancelledStateCorrectly()
    {
        var queue = new LockedQueue<Item>();
        Assert.False(queue.IsCancelled());
        queue.Cancel();
        Assert.True(queue.IsCancelled());
    }
}
