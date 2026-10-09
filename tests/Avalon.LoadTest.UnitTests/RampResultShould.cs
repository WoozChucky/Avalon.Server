using Avalon.LoadTest.Ramp;
using Xunit;

namespace Avalon.LoadTest.UnitTests;

public class RampResultShould
{
    /// <summary>
    /// Version, pod uid and container restart count before the ramp and at its end, then the change the report names
    /// (a run with one does not stand) and how much of the check could be made.
    /// </summary>
    public static TheoryData<string, (string? Version, string? Pod, int? Restarts), (string? Version, string? Pod, int? Restarts), string?, RestartCheck> Cases => new()
    {
        { "the same process throughout", ("1.0", "a", 0), ("1.0", "a", 0), null, RestartCheck.Complete },
        { "only the versions compared, and they differ", ("1.0", null, null), ("1.1", null, null),
          "changed during the ramp: 1.0 → 1.1", RestartCheck.Partial },
        { "a new pod on the same version", ("1.0", "a", 0), ("1.0", "b", 0),
          "world restarted during the ramp (1.0; pod a → b)", RestartCheck.Complete },
        { "a container restart inside the same pod", ("1.0", "a", 1), ("1.0", "a", 3),
          "world restarted during the ramp (1.0; container restarted 2 times)", RestartCheck.Complete },
        { "nothing read at the end", ("1.0", "a", 0), (null, null, null), null, RestartCheck.Unknown },
        { "a restart count that fell is unknown, not a restart", ("1.0", "a", 3), ("1.0", "a", 1), null, RestartCheck.Partial },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Decide_whether_the_world_restarted_during_the_ramp(string _, (string? Version, string? Pod, int? Restarts) start,
        (string? Version, string? Pod, int? Restarts) end, string? change, RestartCheck check)
    {
        var result = new RampResult(RampOutcome.Capacity, 100, [], [], start.Version, TimeSpan.Zero, default, default)
        {
            ServerPod = start.Pod,
            ContainerRestartsAtStart = start.Restarts,
            ServerVersionAtEnd = end.Version,
            ServerPodAtEnd = end.Pod,
            ContainerRestartsAtEnd = end.Restarts,
        };

        Assert.Equal(change, result.ServerChange);
        Assert.Equal(change is not null, result.ServerRestarted);
        Assert.Equal(check, result.RestartCheck);
    }
}
