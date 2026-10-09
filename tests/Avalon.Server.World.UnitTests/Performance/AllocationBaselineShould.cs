using Avalon.World.Testing.Scenarios;

namespace Avalon.Server.World.UnitTests.Performance;

public class AllocationBaselineShould
{
    [Theory]
    [InlineData(100_000, 100_000, AllocationBaseline.Verdict.Within)]
    [InlineData(101_000, 100_000, AllocationBaseline.Verdict.Within)]   // +1%: not more than 1%
    [InlineData(101_001, 100_000, AllocationBaseline.Verdict.Regressed)]
    [InlineData(10_200, 10_000, AllocationBaseline.Verdict.Within)]     // +2% but only +200 B: under 256 B
    [InlineData(1_257, 1_000, AllocationBaseline.Verdict.Regressed)]
    [InlineData(98_999, 100_000, AllocationBaseline.Verdict.Improved)]
    [InlineData(9_800, 10_000, AllocationBaseline.Verdict.Within)]      // −2% but only −200 B
    [InlineData(0, 0, AllocationBaseline.Verdict.Within)]
    [InlineData(300, 0, AllocationBaseline.Verdict.Regressed)]          // from zero: any rise past 256 B
    public void Judge_a_window_against_the_committed_bytes(long current, long committed, AllocationBaseline.Verdict expected) =>
        Assert.Equal(expected, AllocationBaseline.Compare(current, committed));
}
