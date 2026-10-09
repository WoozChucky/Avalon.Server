using Avalon.LoadTest.Ramp;
using Xunit;

namespace Avalon.LoadTest.UnitTests;

public class RampResultShould
{
    /// <summary>The ramp's last judged window ends at this offset (seconds) from <see cref="s_t0"/>.</summary>
    private const int LastJudgedEnd = 600;

    private static readonly DateTimeOffset s_t0 = new(2026, 10, 9, 18, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Version, pod uid, container restart count and container start (seconds from <see cref="s_t0"/>) before the ramp
    /// and at its end, then the change the report names, whether the run does not stand, and how much of the check
    /// could be made.
    /// </summary>
    public static TheoryData<string, (string? Version, string? Pod, int? Restarts, int? Started), (string? Version, string? Pod, int? Restarts, int? Started), string?, bool, RestartCheck> Cases => new()
    {
        { "the same process throughout", ("1.0", "a", 0, 0), ("1.0", "a", 0, 0), null, false, RestartCheck.Complete },
        { "only the versions compared, and they differ", ("1.0", null, null, null), ("1.1", null, null, null),
          "changed during the ramp: 1.0 → 1.1", true, RestartCheck.Partial },
        { "a new pod on the same version, started before the last judged window ended", ("1.0", "a", 0, 0), ("1.0", "b", 0, 300),
          "world restarted during the ramp (1.0; pod a → b; container started 18:05:00 UTC)", true, RestartCheck.Complete },
        { "a new pod whose start time is unknown counts as during the ramp", ("1.0", "a", 0, null), ("1.0", "b", 0, null),
          "world restarted during the ramp (1.0; pod a → b)", true, RestartCheck.Partial },
        { "two container restarts inside the pod, the last one after the last judged window", ("1.0", "a", 1, 0), ("1.0", "a", 3, 700),
          "world restarted during the ramp (1.0; container restarted 2 times, last started 18:11:40 UTC)", true, RestartCheck.Complete },
        { "one container restart after the last judged window, while the bots left", ("1.0", "a", 0, 0), ("1.0", "a", 1, 700),
          "world restarted after the ramp's last judged step, while the bots left (1.0; container restarted once, last started 18:11:40 UTC)",
          false, RestartCheck.Complete },
        { "a replaced pod target_info does not show yet, started after the last judged window", ("1.0", "a", 0, 0), ("1.0", "a", 0, 700),
          "world restarted after the ramp's last judged step, while the bots left (1.0; container started 18:11:40 UTC)", false, RestartCheck.Complete },
        { "nothing read at the end", ("1.0", "a", 0, 0), (null, null, null, null), null, false, RestartCheck.Unknown },
        { "a restart count that fell is unknown, not a restart", ("1.0", "a", 3, 0), ("1.0", "a", 1, 0), null, false, RestartCheck.Partial },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Decide_whether_the_world_restarted_during_the_ramp(string _,
        (string? Version, string? Pod, int? Restarts, int? Started) start, (string? Version, string? Pod, int? Restarts, int? Started) end,
        string? change, bool doesNotStand, RestartCheck check)
    {
        var result = new RampResult(RampOutcome.Capacity, 100, [], [], start.Version, TimeSpan.Zero, default, default)
        {
            ServerPod = start.Pod,
            ContainerRestartsAtStart = start.Restarts,
            ContainerStartedAtStart = At(start.Started),
            ServerVersionAtEnd = end.Version,
            ServerPodAtEnd = end.Pod,
            ContainerRestartsAtEnd = end.Restarts,
            ContainerStartedAtEnd = At(end.Started),
            LastJudgedEnd = At(LastJudgedEnd),
        };

        Assert.Equal(change, result.ServerChange);
        Assert.Equal(doesNotStand, result.RestartedDuringRamp);
        Assert.Equal(check, result.RestartCheck);
    }

    private static DateTimeOffset? At(int? seconds) => seconds is { } s ? s_t0.AddSeconds(s) : null;
}
