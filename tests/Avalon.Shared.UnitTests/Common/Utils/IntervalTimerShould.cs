using Avalon.Common.Utils;
using Xunit;

namespace Avalon.Shared.UnitTests.Common.Utils;

public class IntervalTimerShould
{
    [Fact]
    public void StartAtZero()
    {
        var timer = new IntervalTimer();

        Assert.Equal(0L, timer.GetCurrent());
        Assert.Equal(0L, timer.GetInterval());
    }

    [Fact]
    public void AccumulateDiffOnUpdate()
    {
        var timer = new IntervalTimer();
        timer.SetInterval(100);

        timer.Update(40);
        timer.Update(30);

        Assert.Equal(70L, timer.GetCurrent());
    }

    [Fact]
    public void ReturnFalseFromPassedBeforeIntervalReached()
    {
        var timer = new IntervalTimer();
        timer.SetInterval(100);
        timer.Update(99);

        Assert.False(timer.Passed());
    }

    [Fact]
    public void ReturnTrueFromPassedAtExactInterval()
    {
        var timer = new IntervalTimer();
        timer.SetInterval(100);
        timer.Update(100);

        Assert.True(timer.Passed());
    }

    [Fact]
    public void ReturnTrueFromPassedWhenCurrentExceedsInterval()
    {
        var timer = new IntervalTimer();
        timer.SetInterval(100);
        timer.Update(200);

        Assert.True(timer.Passed());
    }

    [Fact]
    public void ClampCurrentToZeroOnNegativeDiff()
    {
        var timer = new IntervalTimer();
        timer.Update(-50);

        Assert.Equal(0L, timer.GetCurrent());
    }

    [Fact]
    public void CarryOverRemainderOnReset()
    {
        var timer = new IntervalTimer();
        timer.SetInterval(100);
        timer.Update(150);

        timer.Reset();

        // 150 % 100 == 50
        Assert.Equal(50L, timer.GetCurrent());
    }

    [Fact]
    public void NotModifyCurrentOnResetWhenNotPassed()
    {
        var timer = new IntervalTimer();
        timer.SetInterval(100);
        timer.Update(60);

        timer.Reset();

        Assert.Equal(60L, timer.GetCurrent());
    }

    [Fact]
    public void ResetToZeroWhenCurrentEqualsInterval()
    {
        var timer = new IntervalTimer();
        timer.SetInterval(100);
        timer.Update(100);

        timer.Reset();

        Assert.Equal(0L, timer.GetCurrent());
    }

    [Fact]
    public void RespectSetCurrentAndSetInterval()
    {
        var timer = new IntervalTimer();

        timer.SetCurrent(75);
        timer.SetInterval(100);

        Assert.Equal(75L, timer.GetCurrent());
        Assert.Equal(100L, timer.GetInterval());
        Assert.False(timer.Passed());
    }
}
