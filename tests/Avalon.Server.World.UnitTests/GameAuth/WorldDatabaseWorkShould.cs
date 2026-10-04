using Avalon.World.Persistence;

namespace Avalon.Server.World.UnitTests.GameAuth;

public sealed class WorldDatabaseWorkShould
{
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
