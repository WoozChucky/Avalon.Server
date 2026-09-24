using Avalon.World;
using Xunit;

namespace Avalon.Server.World.UnitTests.World;

public class GameTimeShould
{
    [Fact]
    public void Report_A_Start_Time_Between_Construction_And_Now()
    {
        var before = DateTime.UtcNow;
        var time = new GameTime();

        Assert.True(time.StartTime >= before.AddSeconds(-1));
        Assert.True(time.StartTime <= DateTime.UtcNow);
    }

    [Fact]
    public void Report_A_Current_Time_Around_Now()
    {
        var before = DateTime.UtcNow;
        var time = new GameTime();
        var after = DateTime.UtcNow;

        Assert.True(time.CurrentTime >= before.AddSeconds(-1));
        Assert.True(time.CurrentTime <= after.AddSeconds(1));
    }

    [Fact]
    public void Report_A_Non_Negative_Interval_Since_The_Last_Update()
    {
        var time = new GameTime();

        Assert.True(time.SinceLastUpdate >= TimeSpan.Zero);
    }

    [Fact]
    public void Report_A_Non_Negative_Uptime()
    {
        var time = new GameTime();

        Assert.True(time.Uptime >= TimeSpan.Zero);
    }

    [Fact]
    public void Report_A_Non_Negative_Elapsed_Since_Start()
    {
        var time = new GameTime();

        Assert.True(time.ElapsedSinceStart >= TimeSpan.Zero);
    }

    [Fact]
    public void Report_A_Zero_Delta_Before_The_First_Update()
    {
        var time = new GameTime();

        Assert.Equal(TimeSpan.Zero, time.DeltaTime);
    }

    [Fact]
    public void Record_The_Delta_It_Was_Updated_With()
    {
        var time = new GameTime();
        var delta = TimeSpan.FromMilliseconds(16.67);

        time.Update(delta);

        Assert.Equal(delta, time.DeltaTime);
    }

    [Fact]
    public void Stamp_The_System_Time_On_Update()
    {
        var time = new GameTime();

        var before = DateTime.UtcNow;
        time.Update(TimeSpan.Zero);
        var after = DateTime.UtcNow;

        Assert.True(time.SystemTime >= before.AddMilliseconds(-100));
        Assert.True(time.SystemTime <= after.AddMilliseconds(100));
    }

    /// <summary>
    /// The reason this type stopped being static. It used to hold process-wide mutable state, so a
    /// world ticked by one test leaked its delta into every other test in the process — any test
    /// that advanced a world by six seconds could make an unrelated assertion on a fresh delta see
    /// six seconds instead of zero, depending purely on xUnit's run order across parallel
    /// collections.
    /// </summary>
    [Fact]
    public void Keep_Its_State_To_Itself()
    {
        var ticked = new GameTime();
        var untouched = new GameTime();

        ticked.Update(TimeSpan.FromSeconds(6));

        Assert.Equal(TimeSpan.FromSeconds(6), ticked.DeltaTime);
        Assert.Equal(TimeSpan.Zero, untouched.DeltaTime);
    }
}
