using Avalon.World;

namespace Avalon.Server.World.UnitTests.World;

public class GameTimeShould
{
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
