using Avalon.Common.Utils;
using Xunit;

namespace Avalon.Shared.UnitTests.Common.Utils;

public class TimeUtilsShould
{
    [Fact]
    public void GetApplicationStartTimeReturnsPositiveElapsed()
    {
        var elapsed = TimeUtils.GetApplicationStartTime();
        Assert.True(elapsed >= TimeSpan.Zero);
    }

    [Fact]
    public void GetTimeMsReturnsPositiveValue()
    {
        var time = TimeUtils.GetTimeMs();
        Assert.True(time >= TimeSpan.Zero);
    }

    [Fact]
    public void GetMsTimeReturnsPositiveUInt()
    {
        uint ms = TimeUtils.GetMsTime();
        Assert.True(ms > 0U);
    }

    [Fact]
    public void GetMsTimeDiffUIntSimpleDifference()
    {
        uint diff = TimeUtils.GetMsTimeDiff(100U, 200U);
        Assert.Equal(100U, diff);
    }

    [Fact]
    public void GetMsTimeDiffUIntHandlesOverflow()
    {
        uint oldMs = 0xFFFFFFF0U;
        uint newMs = 10U;
        uint diff = TimeUtils.GetMsTimeDiff(oldMs, newMs);

        Assert.Equal(0xFFFFFFFF - oldMs + newMs, diff);
    }

    [Fact]
    public void GetMsTimeDiffTimeSpanSimpleDifference()
    {
        var old = TimeSpan.FromMilliseconds(100);
        var now = TimeSpan.FromMilliseconds(300);

        var diff = TimeUtils.GetMsTimeDiff(old, now);
        Assert.Equal(TimeSpan.FromMilliseconds(200), diff);
    }

    [Fact]
    public void GetMsTimeDiffTimeSpanHandlesOldGreaterThanNew()
    {
        var old = TimeSpan.FromMilliseconds(500);
        var now = TimeSpan.FromMilliseconds(100);

        var diff = TimeUtils.GetMsTimeDiff(old, now);
        Assert.Equal(TimeSpan.FromMilliseconds(400), diff);
    }

    [Fact]
    public void GetMsTimeDiffToNowReturnsDifference()
    {
        uint start = TimeUtils.GetMsTime();
        Thread.Sleep(5);
        uint diff = TimeUtils.GetMsTimeDiffToNow(start);

        Assert.True(diff >= 0U);
    }

    [Fact]
    public void GetMsTimeDiffToNowTimeSpanReturnsDifference()
    {
        var start = TimeUtils.GetTimeMs();
        Thread.Sleep(5);
        var diff = TimeUtils.GetMsTimeDiffToNow(start);

        Assert.True(diff >= TimeSpan.Zero);
    }

    [Fact]
    public void GetEpochTimeReturnsReasonableValue()
    {
        long epoch = TimeUtils.GetEpochTime();
        // Should be after Jan 1 2020
        Assert.True(epoch > 1577836800L);
    }

    [Fact]
    public void GetMsTimeDiffWithTimeSpanNewTimeDelegates()
    {
        uint oldMs = 100U;
        var newTime = TimeSpan.FromMilliseconds(300);

        uint diff = TimeUtils.GetMsTimeDiff(oldMs, newTime);

        Assert.Equal(200U, diff);
    }
}
