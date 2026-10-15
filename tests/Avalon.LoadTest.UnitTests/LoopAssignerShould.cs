using Avalon.LoadTest.Bots;
using Xunit;

namespace Avalon.LoadTest.UnitTests;

public class LoopAssignerShould
{
    /// <summary>
    /// The input driver's loops, each on its own thread, walk the bots step after step while bots join in batches (a
    /// ramp step's), as the driver does: a bot driven by two loops would get two inputs a step, one driven by none none,
    /// and neither shows anywhere but in the world's numbers. Each bot must go to exactly one loop, the first time any
    /// loop sees it, and stay there; and each behaviour must be shared out evenly, also with a loop count that shares a
    /// factor with the mix's rotation (every ten bot indexes hold the same behaviours), where an assignment by index
    /// would put every churner (5 loops, default mix) or most fighters on a few loops.
    /// </summary>
    [Theory]
    [InlineData(Mix.Default, 5)]
    [InlineData("idle=40,walker=20,churner=10,fighter=30", 4)]
    [InlineData("idle=40,walker=20,churner=10,fighter=30", 8)]
    public void Give_each_bot_one_loop_for_good_and_share_each_behaviour_out_evenly(string mixText, int loops)
    {
        const int Bots = 1000;
        const int Batch = 50;
        IReadOnlyList<(BehaviourKind Kind, int Weight)> mix = Mix.Parse(mixText);
        BehaviourKind[] kinds = [.. Enumerable.Range(0, Bots).Select(index => Mix.For(index, mix))];
        int[] slots = [.. Enumerable.Repeat(LoopAssigner.Unassigned, Bots)];
        int[] drivenBy = [.. Enumerable.Repeat(LoopAssigner.Unassigned, Bots)];
        int conflicts = 0;
        int joined = Batch;
        var assigner = new LoopAssigner(loops);

        // Every loop walks the bots joined so far, then waits for the others; the next batch joins between passes.
        using var pass = new Barrier(loops, _ => joined = Math.Min(joined + Batch, Bots));
        Thread[] threads = [.. Enumerable.Range(0, loops).Select(loop => new Thread(() =>
        {
            for (int step = 0; step < Bots / Batch + 2; step++)
            {
                for (int bot = 0; bot < Volatile.Read(ref joined); bot++)
                {
                    if (assigner.LoopOf(ref slots[bot], kinds[bot]) != loop) continue;

                    int driver = Interlocked.CompareExchange(ref drivenBy[bot], loop, LoopAssigner.Unassigned);
                    if (driver != LoopAssigner.Unassigned && driver != loop) Interlocked.Increment(ref conflicts);
                }

                pass.SignalAndWait();
            }
        }))];
        foreach (Thread thread in threads) thread.Start();
        foreach (Thread thread in threads) thread.Join();

        Assert.Equal(0, conflicts);
        Assert.Equal(slots, drivenBy);
        Assert.DoesNotContain(LoopAssigner.Unassigned, drivenBy);
        foreach (BehaviourKind kind in mix.Select(entry => entry.Kind))
        {
            int[] perLoop = [.. Enumerable.Range(0, loops).Select(loop => Enumerable.Range(0, Bots).Count(bot => kinds[bot] == kind && slots[bot] == loop))];
            Assert.True(perLoop.Max() - perLoop.Min() <= 1, $"{kind} per loop: {string.Join(", ", perLoop)}");
        }

        int[] totals = [.. Enumerable.Range(0, loops).Select(loop => slots.Count(slot => slot == loop))];
        Assert.True(totals.Max() - totals.Min() <= mix.Count, $"bots per loop: {string.Join(", ", totals)}");
    }
}
