namespace Avalon.Server.Auth;

/// <summary>
/// The world selects one auth connection may make per fixed window (#574). Each select costs
/// database reads, and a refused one a warning line, so a client repeating selects could flood
/// both; past the cap the connection is closed. The window starts at the first select and is
/// measured on the <see cref="TimeProvider"/>'s timestamps, so no timer or tick is needed. Also
/// carries, per window, whether a WorldUnavailable refusal has been logged, so that warning is
/// written at most once per window too.
/// </summary>
public sealed class WorldSelectBudget
{
    /// <summary>The window the cap (<c>Application:MaxWorldSelectsPerMinute</c>) is counted over.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly Lock _gate = new();
    private bool _started;
    private long _windowStart;
    private int _count;
    private bool _overCapLogged;
    private bool _unavailableLogged;

    /// <summary>Counts one select against the cap, rolling the window over first when it has ended.</summary>
    public WorldSelectAdmission Take(TimeProvider time, int cap)
    {
        lock (_gate)
        {
            long now = time.GetTimestamp();
            if (!_started || time.GetElapsedTime(_windowStart, now) >= Window)
            {
                _started = true;
                _windowStart = now;
                _count = 0;
                _overCapLogged = false;
                _unavailableLogged = false;
            }

            // Saturates at one past the cap, so a flood cannot overflow the counter.
            if (_count <= cap)
                _count++;

            if (_count <= cap)
                return WorldSelectAdmission.Admitted;

            if (_overCapLogged)
                return WorldSelectAdmission.Refused;

            _overCapLogged = true;
            return WorldSelectAdmission.RefusedFirstInWindow;
        }
    }

    /// <summary>
    /// True the first time it is asked in the current window, false after: whether a
    /// WorldUnavailable refusal should be logged at Warning.
    /// </summary>
    public bool TakeUnavailableWarning()
    {
        lock (_gate)
        {
            if (_unavailableLogged)
                return false;

            _unavailableLogged = true;
            return true;
        }
    }
}
