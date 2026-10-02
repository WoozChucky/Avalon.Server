using Avalon.World.Threading;

namespace Avalon.Server.World.UnitTests.Threading;

/// <summary>
/// #639: the tick-thread assertion. Bound to the thread that runs the tick, it refuses any other thread; unbound (no
/// tick running, as in most tests and during start-up and the shutdown despawn) it lets every thread through.
/// </summary>
public class TickThreadGuardShould
{
    /// <summary>
    /// Compiled into every build (owner decision), so these tests run in Release as well; this assembly turns it on
    /// (<see cref="TickThreadGuardOn" />), and production leaves it off unless <c>Game:TickThreadGuard</c> is set.
    /// </summary>
    [Fact]
    public void Be_on_for_these_tests_and_off_by_default_in_production()
    {
        Assert.True(TickThreadGuard.Enabled);
        Assert.False(new Avalon.World.Configuration.GameConfiguration().TickThreadGuard);
    }

    [Fact]
    public void Let_any_thread_through_while_no_tick_is_bound()
    {
        var guard = new TickThreadGuard();

        Assert.Null(TickThreadGuardProbe.OffThread(() => guard.AssertOnTick("Probe")));
        guard.AssertOnTick("Probe");
    }

    [Fact]
    public void Let_the_bound_thread_through()
    {
        var guard = new TickThreadGuard();
        guard.Bind();

        guard.AssertOnTick("Probe");
    }

    [Fact]
    public void Refuse_another_thread_while_one_is_bound_and_name_the_operation()
    {
        var guard = new TickThreadGuard();
        guard.Bind();

        Exception? thrown = TickThreadGuardProbe.OffThread(() => guard.AssertOnTick("World.TransferPlayer"));

        var refused = Assert.IsType<InvalidOperationException>(thrown);
        Assert.Contains("World.TransferPlayer", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Let_every_thread_through_again_once_the_tick_unbinds()
    {
        var guard = new TickThreadGuard();
        guard.Bind();
        guard.Unbind();

        Assert.Null(TickThreadGuardProbe.OffThread(() => guard.AssertOnTick("Probe")));
    }

    /// <summary>Only the bound thread can unbind: another thread's unbind leaves the tick bound.</summary>
    [Fact]
    public void Stay_bound_when_another_thread_unbinds()
    {
        var guard = new TickThreadGuard();
        guard.Bind();

        Assert.Null(TickThreadGuardProbe.OffThread(guard.Unbind));

        Assert.IsType<InvalidOperationException>(TickThreadGuardProbe.OffThread(() => guard.AssertOnTick("Probe")));
    }
}

/// <summary>Runs an action on a thread of its own (never inlined on the caller, as a waited task can be).</summary>
internal static class TickThreadGuardProbe
{
    public static Exception? OffThread(Action action)
    {
        Exception? thrown = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                thrown = e;
            }
        });
        thread.Start();
        thread.Join();
        return thrown;
    }

    /// <summary>The same for an action that returns a task, whose synchronous part is what runs off the thread.</summary>
    public static Exception? OffThread(Func<Task> action) => OffThread(() => { _ = action(); });
}
