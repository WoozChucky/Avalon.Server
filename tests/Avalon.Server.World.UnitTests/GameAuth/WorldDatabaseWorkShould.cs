using Avalon.World.Persistence;

namespace Avalon.Server.World.UnitTests.GameAuth;

public sealed class WorldDatabaseWorkShould
{
    /// <summary>
    /// A limit on a hang only: the work runs on the thread pool, which a loaded test run can hold for seconds (#960), so
    /// no wait here is for an amount of time (#1019).
    /// </summary>
    private static readonly TimeSpan s_hang = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Bound_concurrency_and_backlog_and_release_capacity_after_a_failed_operation()
    {
        var workers = new WorldDatabaseWork(1, 2);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> first = workers.Run(async () => { entered.SetResult(); await release.Task; if (release.Task.IsCompleted) throw new InvalidOperationException("fixture"); return 0; });
        await entered.Task.WaitAsync(s_hang);
        int secondStarted = 0;
        Task<int> second = workers.Run(() => { Interlocked.Increment(ref secondStarted); return Task.FromResult(2); });
        await Assert.ThrowsAsync<WorldWorkUnavailableException>(() => workers.Run(() => Task.FromResult(3)));
        Assert.Equal(0, secondStarted);
        release.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        Assert.Equal(2, await second.WaitAsync(s_hang));
        Assert.Equal(4, await workers.Run(() => Task.FromResult(4)).WaitAsync(s_hang));
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
                release.Wait(); // until the test lets it go: its finally always does
                return Task.FromResult(42);
            }));
        })
        { IsBackground = true };
        tick.Start();
        try
        {
            Task<int> work = await returned.Task.WaitAsync(s_hang);
            Assert.NotEqual(tickThread, await entered.Task.WaitAsync(s_hang));
            Assert.False(work.IsCompleted);
            release.Set();
            Assert.Equal(42, await work.WaitAsync(s_hang));
        }
        finally { release.Set(); }
    }
}
