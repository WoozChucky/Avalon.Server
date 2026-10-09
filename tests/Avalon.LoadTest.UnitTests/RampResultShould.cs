using Avalon.LoadTest.Ramp;
using Xunit;

namespace Avalon.LoadTest.UnitTests;

public class RampResultShould
{
    private static readonly DateTimeOffset s_t0 = new(2026, 10, 9, 18, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Each row: the reads before and at the end of a ramp whose last judged window ended at 600 s and whose stop began
    /// at 615 s (times in seconds from 18:00:00 UTC; the world's container started at 0 s in pod a), then the change the
    /// report names and whether the run stands.
    /// </summary>
    private static readonly Dictionary<string, (Func<RampResult> Result, string? Change, bool Stands)> s_rows = new()
    {
        ["the same process throughout"] = (() => Result(), null, true),
        ["only the versions compared, and they differ"] = (() => Result(versionAtEnd: "1.1", pod: null, restartsAtStart: null, startedAtStart: null),
            "version changed; not proven after the last judged step (when the old process ended is unknown): 1.0 → 1.1", false),
        ["a pod replaced during the ramp"] = (() => Result(podAtEnd: "b", newestPod: "b", newestPodStarted: 300, oldPodLastUp: 290, startedAtEnd: 300),
            "world restarted during the ramp (1.0; pod a → b; container started 18:05:00 UTC)", false),
        ["a container crash before the window ended, its new start after it"] = (
            () => Result(restartsAtEnd: 1, terminated: 590, startedAtEnd: 650),
            "world restarted during the ramp (1.0; container restarted once, last started 18:10:50 UTC)", false),
        ["a container restart after the window, before the stop"] = (
            () => Result(restartsAtEnd: 1, terminated: 605, startedAtEnd: 607),
            "world restarted after the ramp's last judged step (1.0; container restarted once, last started 18:10:07 UTC)", true),
        ["a container restart while the bots left"] = (
            () => Result(restartsAtEnd: 1, terminated: 690, startedAtEnd: 700),
            "world restarted after the ramp's last judged step, while the bots left (1.0; container restarted once, last started 18:11:40 UTC)", true),
        ["a restart after the window with its restart count unknown"] = (
            () => Result(restartsAtEnd: null, terminated: 690, startedAtEnd: 700),
            "world restarted; not proven after the last judged step (the container's restart count is unknown): 1.0; container started 18:11:40 UTC",
            false),
        ["a pod replaced while the bots left, target_info still naming the old one"] = (
            () => Result(newestPod: "b", newestPodStarted: 645, oldPodLastUp: 640, startedAtEnd: 645),
            "world restarted after the ramp's last judged step, while the bots left (1.0; pod a → b; container started 18:10:45 UTC)", true),
        ["a pod replaced while the bots left, then its new pod's container restarted"] = (
            () => Result(podAtEnd: "b", newestPod: "b", newestPodStarted: 645, newestRestarts: 1, oldPodLastUp: 640, startedAtEnd: 700),
            "world restarted; not proven after the last judged step (the new pod's container restarted): 1.0; pod a → b; container started 18:11:40 UTC",
            false),
        ["nothing read at the end"] = (
            () => Result(versionAtEnd: null, podAtEnd: null, restartsAtEnd: null, startedAtEnd: null, newestPod: null,
                newestPodStarted: null), null, false),
        ["a restart count that fell is unknown, not a restart"] = (() => Result(restartsAtStart: 3, restartsAtEnd: 1), null, false),
    };

    public static TheoryData<string> Cases => [.. s_rows.Keys];

    [Theory]
    [MemberData(nameof(Cases))]
    public void Decide_whether_the_run_stands_after_a_world_restart(string row)
    {
        (Func<RampResult> build, string? change, bool stands) = s_rows[row];
        RampResult result = build();

        Assert.Equal(change, result.ServerChange);
        Assert.Equal(stands, result.Stands);
    }

    /// <summary>A ramp's result with its restart reads; by default the same process throughout, every read known.</summary>
    private static RampResult Result(
        string? versionAtEnd = "1.0", string? pod = "a", string? podAtEnd = "a", int? restartsAtStart = 0, int? restartsAtEnd = 0,
        int? startedAtStart = 0, int? startedAtEnd = 0, int? terminated = null, int? oldPodLastUp = null,
        string? newestPod = "a", int? newestPodStarted = 0, int? newestRestarts = 0) =>
        new(RampOutcome.Capacity, 100, [], [], "1.0", TimeSpan.Zero, default, default)
        {
            ServerVersionAtEnd = versionAtEnd,
            ServerPod = pod,
            ServerPodAtEnd = podAtEnd,
            ContainerRestartsAtStart = restartsAtStart,
            ContainerRestartsAtEnd = restartsAtEnd,
            ContainerStartedAtStart = At(startedAtStart),
            ContainerStartedAtEnd = At(startedAtEnd),
            ContainerLastTerminatedAt = At(terminated),
            OldPodLastUpAt = At(oldPodLastUp),
            NewestPod = newestPod,
            NewestPodStartedAt = At(newestPodStarted),
            NewestPodRestarts = newestRestarts,
            LastJudgedEnd = At(600),
            StopStarted = At(615),
        };

    private static DateTimeOffset? At(int? seconds) => seconds is { } s ? s_t0.AddSeconds(s) : null;
}
