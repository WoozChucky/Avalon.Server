using Avalon.Common.Utils;
using Xunit;

namespace Avalon.Shared.UnitTests.Common.Utils;

public class TimeUtilsShould
{
    [Fact]
    public void Read_the_clocks()
    {
        Assert.True(TimeUtils.GetApplicationStartTime() >= TimeSpan.Zero);
        Assert.True(TimeUtils.GetTimeMs() >= TimeSpan.Zero);
        // After Jan 1 2020.
        Assert.True(TimeUtils.GetEpochTime() > 1577836800L);
        Assert.True(TimeUtils.GetMsTimeDiffToNow(TimeUtils.GetMsTime()) < 60_000U);
        Assert.True(TimeUtils.GetMsTimeDiffToNow(TimeUtils.GetTimeMs()) < TimeSpan.FromMinutes(1));
    }

    /// <summary>An old time past the new one is a millisecond counter that wrapped.</summary>
    [Theory]
    [InlineData(100U, 200U, 100U)]
    [InlineData(0xFFFFFFF0U, 10U, 0xFFFFFFFFU - 0xFFFFFFF0U + 10U)]
    public void Diff_millisecond_counters_across_a_wrap(uint oldMs, uint newMs, uint expected)
    {
        Assert.Equal(expected, TimeUtils.GetMsTimeDiff(oldMs, newMs));
        Assert.Equal(expected, TimeUtils.GetMsTimeDiff(oldMs, TimeSpan.FromMilliseconds(newMs)));
    }

    [Theory]
    [InlineData(100, 300, 200)]
    [InlineData(500, 100, 400)]
    public void Diff_time_spans_either_way_round(int oldMs, int newMs, int expected)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(expected),
            TimeUtils.GetMsTimeDiff(TimeSpan.FromMilliseconds(oldMs), TimeSpan.FromMilliseconds(newMs)));
    }
}
