namespace Avalon.Server.World.UnitTests;

/// <summary>
/// A <see cref="TimeProvider" /> whose time and timers move only when the test says so. A timer it
/// creates (<c>Task.WaitAsync(TimeSpan, TimeProvider, ...)</c>, <c>Task.Delay(TimeSpan, TimeProvider)</c>)
/// fires on the thread that calls <see cref="Advance" />, once the clock reaches its due time, and
/// never on its own: a test that never advances the clock has no timer racing it.
/// </summary>
internal sealed class ManualTimerClock(DateTimeOffset start) : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = start;

    public ManualTimerClock() : this(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
    {
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
            return _now;
    }

    public override long GetTimestamp() => GetUtcNow().UtcTicks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <summary>Moves the clock on and fires, in due order, every timer that falls due on the way.</summary>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        DateTimeOffset target;
        lock (_gate)
            target = _now + by;

        while (true)
        {
            ManualTimer? due;
            lock (_gate)
            {
                due = _timers.Where(t => t.DueAt is { } at && at <= target).MinBy(t => t.DueAt);
                if (due is null)
                {
                    _now = target;
                    return;
                }

                _now = due.DueAt!.Value;
                due.DueAt = due.Period is { } period ? _now + period : null;
            }

            due.Fire();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (_gate)
            _timers.Add(timer);
        return timer;
    }

    private sealed class ManualTimer(ManualTimerClock clock, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? DueAt { get; set; }
        public TimeSpan? Period { get; private set; }

        /// <summary>
        /// Runs the callback with no synchronization context, as a system timer's thread would. The
        /// runtime runs an awaiting continuation inline only where no context is current, so under
        /// the test framework's context the timeout's continuations would otherwise be posted to the
        /// thread pool, and the test would have to poll for them again.
        /// </summary>
        public void Fire()
        {
            SynchronizationContext? previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                callback(state);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._gate)
            {
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;
                Period = period == Timeout.InfiniteTimeSpan || period == TimeSpan.Zero ? null : period;
            }

            return true;
        }

        public void Dispose()
        {
            lock (clock._gate)
            {
                DueAt = null;
                clock._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
