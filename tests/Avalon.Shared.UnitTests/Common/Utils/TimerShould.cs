using Avalon.Common.Utils;
using Xunit;
using AvalonTimer = Avalon.Common.Utils.Timer;

namespace Avalon.Shared.UnitTests.Common.Utils;

public class TimerShould
{
    [Fact]
    public void ConvertMillisecondsToTimeSpan()
    {
        long ms = 1500L;
        var result = ms.ToTimeSpan();

        Assert.Equal(TimeSpan.FromMilliseconds(1500), result);
    }

    [Fact]
    public void ConvertZeroMilliseconds()
    {
        Assert.Equal(TimeSpan.Zero, 0L.ToTimeSpan());
    }

    [Fact]
    public void CurrentTimeMillisReturnsPositiveValue()
    {
        long ms = AvalonTimer.CurrentTimeMillis();
        Assert.True(ms > 0);
    }

    [Fact]
    public void CurrentTimeMillisAdvancesOverTime()
    {
        long t1 = AvalonTimer.CurrentTimeMillis();
        Thread.Sleep(10);
        long t2 = AvalonTimer.CurrentTimeMillis();

        Assert.True(t2 >= t1);
    }

    [Fact]
    public void GetDiffReturnsSimpleDifference()
    {
        long diff = AvalonTimer.GetDiff(100L, 200L);
        Assert.Equal(100L, diff);
    }

    [Fact]
    public void GetDiffHandlesOverflow()
    {
        // oldMs > newMs simulates a tick counter overflow
        long oldMs = 0xFFFFFFF0L;
        long newMs = 10L;
        long diff = AvalonTimer.GetDiff(oldMs, newMs);

        Assert.Equal((0xFFFFFFFF - oldMs) + newMs, diff);
    }
}
