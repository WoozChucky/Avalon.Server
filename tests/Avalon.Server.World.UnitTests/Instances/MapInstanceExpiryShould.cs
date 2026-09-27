using Avalon.Server.World.UnitTests.Loot;
using Avalon.World.Instances;
using Xunit;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>An empty instance expires by the container's clock, the one the rest of the world uses (#614).</summary>
public class MapInstanceExpiryShould
{
    [Fact]
    public void Expire_By_Its_Clock_Once_Empty_For_The_Expiry()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2001, 1, 1, 12, 0, 0, TimeSpan.Zero));
        using MapInstance instance = TestMapInstances.Build(NewWorld(), time: clock);
        MapInstanceClient client = Join(instance, 614_001);

        instance.RemoveCharacter(client.Connection);

        Assert.Equal(clock.Now.UtcDateTime, instance.LastEmptyAt);
        Assert.False(instance.IsExpired(TimeSpan.FromMinutes(15)));
        clock.Now = clock.Now.AddMinutes(15);
        Assert.True(instance.IsExpired(TimeSpan.FromMinutes(15)));
    }
}
