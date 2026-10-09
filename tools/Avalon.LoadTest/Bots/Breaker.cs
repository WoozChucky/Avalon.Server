using System.Diagnostics;

namespace Avalon.LoadTest.Bots;

/// <summary>
/// A breaker over one kind of call a ramp makes per bot (a sign-out, a leave) that can hang on a peer that stopped
/// answering. Once <see cref="Threshold"/> calls in a row fail that way, it trips: calls are skipped at once and
/// counted (<see cref="Skipped"/>), except one probe let through every <see cref="ProbeInterval"/>. A call answered,
/// a probe or one still in flight from before the trip, resets the count and closes the breaker; a failed probe keeps
/// it tripped. So a dead peer costs one window of calls rather than one timeout per bot, and a short outage costs no
/// more than itself. Armed only for a ramp's stop sequence (<see cref="Arm"/>): until then every call is made and
/// nothing is counted, so a breaker never changes the load a step is judged on. Thread-safe: every transition (the
/// count, the trip, the reset, the next probe) happens under one lock, taken once per leave or sign-out.
/// </summary>
public sealed class Breaker
{
    /// <summary>Failed calls in a row that trip the breaker: one window of the ramp's 32 leaves at once.</summary>
    public const int Threshold = 32;

    /// <summary>While tripped, one call is let through as a probe this often.</summary>
    public static TimeSpan ProbeInterval { get; } = TimeSpan.FromSeconds(5);

    private static readonly long s_probeTicks = (long)(ProbeInterval.TotalSeconds * Stopwatch.Frequency);

    private readonly Lock _lock = new();
    private int _failuresInARow;
    private int _skipped;
    private bool _tripped;
    private bool _armed;

    /// <summary>The <see cref="Stopwatch"/> timestamp from which the next probe may go.</summary>
    private long _nextProbe;

    /// <summary>Calls skipped while the breaker was tripped.</summary>
    public int Skipped
    {
        get
        {
            lock (_lock)
            {
                return _skipped;
            }
        }
    }

    /// <summary>Arms the breaker: from now on failures count, and it may trip. Called when the stop sequence begins.</summary>
    public void Arm()
    {
        lock (_lock)
        {
            _armed = true;
        }
    }

    /// <summary>
    /// Whether to make the call: always while unarmed or closed; while tripped, only for the one caller that takes a
    /// due probe. A call not made is counted as skipped.
    /// </summary>
    public bool TryEnter()
    {
        lock (_lock)
        {
            if (!_armed || !_tripped) return true;

            long now = Stopwatch.GetTimestamp();
            if (now >= _nextProbe)
            {
                _nextProbe = now + s_probeTicks;
                return true;
            }

            _skipped++;
            return false;
        }
    }

    /// <summary>
    /// The peer answered (whatever it said: it is alive): the count restarts and the breaker closes. Nothing while
    /// unarmed.
    /// </summary>
    public void Succeeded()
    {
        lock (_lock)
        {
            if (!_armed) return;

            _failuresInARow = 0;
            _tripped = false;
        }
    }

    /// <summary>
    /// The call failed the way a dead peer fails (no answer in time); the <see cref="Threshold"/>th in a row trips the
    /// breaker, its first probe one interval later. Nothing while unarmed.
    /// </summary>
    public void Failed()
    {
        lock (_lock)
        {
            if (!_armed) return;

            _failuresInARow++;
            if (_tripped || _failuresInARow < Threshold) return;

            _tripped = true;
            _nextProbe = Stopwatch.GetTimestamp() + s_probeTicks;
        }
    }
}
