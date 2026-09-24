using System.Diagnostics;

namespace Avalon.World;

/// <summary>
/// The clock for one <see cref="World" />: when it started, when it last ticked, and how long that
/// tick took. <see cref="World.Update" /> is the only writer.
/// </summary>
/// <remarks>
/// Deliberately per-world rather than static. As process-wide mutable state it could not be observed
/// by one world without another world's tick changing the answer — which in tests meant a world
/// advanced by one test leaked its delta into every other test in the process, and in production
/// would mean a second world in the same process silently overwriting the first's timings.
/// </remarks>
public sealed class GameTime
{
    private readonly Stopwatch _sinceLastUpdate = Stopwatch.StartNew();

    /// <summary>When this world's clock started.</summary>
    public DateTime StartTime { get; } = DateTime.UtcNow;

    /// <summary>Wall-clock time as of the last tick.</summary>
    public DateTime CurrentTime { get; private set; } = DateTime.UtcNow;

    /// <summary>How long this world has been running, as of the last tick.</summary>
    public TimeSpan Uptime => CurrentTime - StartTime;

    /// <summary>Time between <see cref="StartTime" /> and the last tick.</summary>
    public TimeSpan ElapsedSinceStart { get; private set; } = TimeSpan.Zero;

    /// <summary>
    /// Wall-clock time stamped at the last tick. <see cref="DateTime.MinValue" /> until the first
    /// one, which distinguishes "never ticked" from "ticked at the epoch".
    /// </summary>
    public DateTime SystemTime { get; private set; } = DateTime.MinValue;

    /// <summary>
    /// How long since the last tick, from a monotonic source — not affected by the system clock
    /// being adjusted underneath the process.
    /// </summary>
    public TimeSpan SinceLastUpdate => _sinceLastUpdate.Elapsed;

    /// <summary>Length of the last tick. Zero before the first one.</summary>
    public TimeSpan DeltaTime { get; private set; } = TimeSpan.Zero;

    /// <summary>Advances the clock. Called once per tick by <see cref="World.Update" />.</summary>
    public void Update(TimeSpan deltaTime)
    {
        DateTime now = DateTime.UtcNow;

        DeltaTime = deltaTime;
        CurrentTime = now;
        ElapsedSinceStart = now - StartTime;
        SystemTime = now;
        _sinceLastUpdate.Restart();
    }
}
