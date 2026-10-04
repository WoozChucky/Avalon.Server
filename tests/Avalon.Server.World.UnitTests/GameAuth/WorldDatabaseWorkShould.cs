using Avalon.World.Persistence;

namespace Avalon.Server.World.UnitTests.GameAuth;

public sealed class WorldDatabaseWorkShould
{
    [Fact]
    public async Task Bound_concurrency_and_backlog_and_release_capacity_after_a_failed_operation()
    {
        var workers = new WorldDatabaseWork(1, 2);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = workers.Run(async () => { entered.SetResult(); await release.Task; if (release.Task.IsCompleted) throw new InvalidOperationException("fixture"); return 0; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        int secondStarted = 0;
        var second = workers.Run(() => { Interlocked.Increment(ref secondStarted); return Task.FromResult(2); });
        await Assert.ThrowsAsync<WorldWorkUnavailableException>(() => workers.Run(() => Task.FromResult(3)));
        Assert.Equal(0, secondStarted);
        release.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        Assert.Equal(2, await second.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(4, await workers.Run(() => Task.FromResult(4)).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Return_to_the_tick_while_the_provider_is_blocked_on_its_worker()
    {
        using var release = new ManualResetEventSlim();
        var returned = new TaskCompletionSource<Task<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        int tickThread = 0;
        var tick = new Thread(() =>
        {
            tickThread = Environment.CurrentManagedThreadId;
            returned.SetResult(WorldDatabaseWork.ThreadPool.Run(() =>
            {
                entered.SetResult(Environment.CurrentManagedThreadId);
                release.Wait(TimeSpan.FromSeconds(10));
                return Task.FromResult(42);
            }));
        }) { IsBackground = true };
        tick.Start();
        try
        {
            Task<int> work = await returned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(tick.Join(TimeSpan.FromSeconds(5)));
            Assert.NotEqual(tickThread, await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(work.IsCompleted);
            release.Set();
            Assert.Equal(42, await work.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { release.Set(); }
    }
}
