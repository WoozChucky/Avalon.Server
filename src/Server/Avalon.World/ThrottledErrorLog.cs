using Microsoft.Extensions.Logging;

namespace Avalon.World;

/// <summary>
/// Logs a step of the tick that threw, at Error, at most once per <see cref="Interval" />, with how many throws were
/// left out since, so a step that throws on every tick cannot flood the log at 60 Hz (the rule InstanceTicker keeps
/// per instance). Not thread-safe: used on the tick thread, or off it under a lock its caller holds (DoodadScripts, which
/// the off-tick instance builds ask too).
/// </summary>
internal sealed class ThrottledErrorLog(ILogger logger, TimeProvider time, string step)
{
    /// <summary>The least time between two logged throws, InstanceTicker's.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private DateTimeOffset? _lastLogged;
    private int _suppressed;

    public void Failed(Exception e)
    {
        DateTimeOffset now = time.GetUtcNow();
        if (_lastLogged is { } last && now - last < Interval)
        {
            _suppressed++;
            return;
        }

        logger.LogError(e, "{Step} threw; the rest of the tick still ran. {Suppressed} earlier throws of it were not logged",
            step, _suppressed);
        _lastLogged = now;
        _suppressed = 0;
    }
}
