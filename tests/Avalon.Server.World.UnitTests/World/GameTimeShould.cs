using Avalon.World;
using Xunit;

namespace Avalon.Server.World.UnitTests.World;

public class GameTimeShould
{
    [Fact]
    public void GetStartTime_ReturnsDeterministicNonMinValue()
    {
        var startTime = GameTime.GetStartTime();

        Assert.True(startTime > DateTime.MinValue);
        Assert.True(startTime <= DateTime.UtcNow);
    }

    [Fact]
    public void GetGameTime_ReturnsCurrentUtcApproximate()
    {
        var before = DateTime.UtcNow;
        var gameTime = GameTime.GetGameTime();
        var after = DateTime.UtcNow;

        // GameTime.GetGameTime() returns DateTime.UtcNow so it should be within the test window
        Assert.True(gameTime >= before.AddSeconds(-1));
        Assert.True(gameTime <= after.AddSeconds(1));
    }

    [Fact]
    public void Now_ReturnsPositiveElapsed()
    {
        var elapsed = GameTime.Now();

        Assert.True(elapsed >= TimeSpan.Zero);
    }

    [Fact]
    public void GetUptime_ReturnsNonNegativeTimespan()
    {
        var uptime = GameTime.GetUptime();

        Assert.True(uptime >= TimeSpan.Zero);
    }

    [Fact]
    public void GetDeltaTime_ReturnsZeroBeforeFirstUpdate()
    {
        // Before any UpdateGameTimers call, delta should be zero
        var delta = GameTime.GetDeltaTime();

        Assert.Equal(TimeSpan.Zero, delta);
    }

    [Fact]
    public void UpdateGameTimers_SetsDeltaTime()
    {
        var delta = TimeSpan.FromMilliseconds(16.67);

        GameTime.UpdateGameTimers(delta);

        Assert.Equal(delta, GameTime.GetDeltaTime());
    }

    [Fact]
    public void UpdateGameTimers_SetsSystemTime()
    {
        var before = DateTime.UtcNow;
        GameTime.UpdateGameTimers(TimeSpan.Zero);
        var after = DateTime.UtcNow;

        var systemTime = GameTime.GetSystemTime();
        Assert.True(systemTime >= before.AddMilliseconds(-100));
        Assert.True(systemTime <= after.AddMilliseconds(100));
    }

    [Fact]
    public void GetGameTimeMS_ReturnsNonNegativeTimespan()
    {
        var gameMs = GameTime.GetGameTimeMS();

        Assert.True(gameMs >= TimeSpan.Zero);
    }
}
