using Avalon.World.Testing.Scenarios;

namespace Avalon.Server.World.UnitTests.Performance;

public class AllocationBaselineShould
{
    [Theory]
    [InlineData(10_000, 10_000, AllocationBaseline.Verdict.Within)]
    [InlineData(10_500, 10_000, AllocationBaseline.Verdict.Within)]     // +5%: not more than 5%
    [InlineData(10_501, 10_000, AllocationBaseline.Verdict.Regressed)]
    [InlineData(1_200, 1_000, AllocationBaseline.Verdict.Within)]       // +20% but only +200 B: under 256 B
    [InlineData(1_257, 1_000, AllocationBaseline.Verdict.Regressed)]
    [InlineData(9_499, 10_000, AllocationBaseline.Verdict.Improved)]
    [InlineData(800, 1_000, AllocationBaseline.Verdict.Within)]         // −20% but only −200 B
    [InlineData(0, 0, AllocationBaseline.Verdict.Within)]
    [InlineData(300, 0, AllocationBaseline.Verdict.Regressed)]          // from zero: any rise past 256 B
    public void Judge_a_window_against_the_committed_bytes(long current, long committed, AllocationBaseline.Verdict expected) =>
        Assert.Equal(expected, AllocationBaseline.Compare(current, committed));
}
