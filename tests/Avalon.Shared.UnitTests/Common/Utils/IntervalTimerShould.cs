using Avalon.Common.Utils;
using Xunit;

namespace Avalon.Shared.UnitTests.Common.Utils;

public class IntervalTimerShould
{
    [Fact]
    public void Accumulate_from_zero_and_never_go_below_it()
    {
        var timer = new IntervalTimer();
        Assert.Equal(0L, timer.GetCurrent());
        Assert.Equal(0L, timer.GetInterval());

        timer.Update(-50);
        Assert.Equal(0L, timer.GetCurrent());

        timer.SetInterval(100);
        timer.Update(40);
        timer.Update(30);
        Assert.Equal(70L, timer.GetCurrent());
    }

    [Theory]
    [InlineData(99, false)]
    [InlineData(100, true)]
    [InlineData(200, true)]
    public void Pass_once_the_interval_is_reached(long elapsed, bool passed)
    {
        var timer = new IntervalTimer();
        timer.SetInterval(100);
        timer.Update(elapsed);

        Assert.Equal(passed, timer.Passed());
    }

    /// <summary>A reset keeps the time past the interval (150 % 100 = 50) and leaves a timer that has not passed alone.</summary>
    [Theory]
    [InlineData(150, 50)]
    [InlineData(100, 0)]
    [InlineData(60, 60)]
    public void Carry_the_remainder_over_a_reset(long elapsed, long afterReset)
    {
        var timer = new IntervalTimer();
        timer.SetInterval(100);
        timer.Update(elapsed);

        timer.Reset();

        Assert.Equal(afterReset, timer.GetCurrent());
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
