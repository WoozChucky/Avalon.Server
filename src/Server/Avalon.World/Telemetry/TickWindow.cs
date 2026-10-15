namespace Avalon.World.Telemetry;

/// <summary>
/// The world's ticks gathered into windows of about one second (#1016), from each tick's start and end timestamps: the
/// mean tick time every time-sync ping carries (<c>SPingPacket.TickMicros</c>). The first window opens at the start of
/// the first tick; each later one where the one before it closed, so the windows follow one another without a gap. A
/// window closes at the end of the first tick that ends a second or more after it opened, and holds the ticks that ended
/// in it. No clock read and no allocation: a sum and a count per tick, one division per window. Tick thread only.
/// </summary>
/// <param name="frequency">Timestamps per second (<see cref="System.Diagnostics.Stopwatch.Frequency" />).</param>
public struct TickWindow(long frequency)
{
    private readonly long _frequency = frequency;
    private long _start;
    private long _busy;
    private int _ticks;
    private bool _opened;

    /// <summary>
    /// The mean busy time of the ticks in the last window that closed, in whole microseconds; never 0 once one has
    /// closed, which leaves 0 to mean "not measured yet".
    /// </summary>
    public uint MeanMicros { get; private set; }

    /// <summary>Adds one tick that ran from <paramref name="start" /> to <paramref name="end" />; true when it closed the window.</summary>
    public bool Add(long start, long end)
    {
        if (!_opened)
        {
            _start = start;
            _opened = true;
        }

        _busy += end - start;
        _ticks++;
        if (end - _start < _frequency)
            return false;

        // Never 0, which the ping keeps for "not reported", and no more than the field holds.
        MeanMicros = (uint)Math.Clamp(Math.Round(_busy * 1_000_000.0 / _frequency / _ticks), 1, uint.MaxValue);
        _start = end;
        _busy = 0;
        _ticks = 0;
        return true;
    }
}
