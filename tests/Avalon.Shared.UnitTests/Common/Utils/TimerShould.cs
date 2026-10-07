using Avalon.Common.Utils;
using Xunit;
using AvalonTimer = Avalon.Common.Utils.Timer;

namespace Avalon.Shared.UnitTests.Common.Utils;

public class TimerShould
{
    [Theory]
    [InlineData(1500L)]
    [InlineData(0L)]
    public void ConvertMillisecondsToTimeSpan(long ms)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(ms), ms.ToTimeSpan());
    }

    [Fact]
    public void Read_a_clock_that_never_runs_backwards()
    {
        long t1 = AvalonTimer.CurrentTimeMillis();
        long t2 = AvalonTimer.CurrentTimeMillis();

        Assert.True(t1 > 0);
        Assert.True(t2 >= t1);
    }

    /// <summary>An old time past the new one is a tick counter that wrapped.</summary>
    [Theory]
    [InlineData(100L, 200L, 100L)]
    [InlineData(0xFFFFFFF0L, 10L, 0xFFFFFFFFL - 0xFFFFFFF0L + 10L)]
    public void Diff_two_times_across_a_wrap(long oldMs, long newMs, long expected)
    {
        Assert.Equal(expected, AvalonTimer.GetDiff(oldMs, newMs));
    }
}
