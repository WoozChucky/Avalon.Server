using Avalon.World.Configuration;
using Avalon.World.Loot;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Loot;

public class InstanceOwnerLootAllocatorShould
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static LootAllocation Allocate(uint? owner, TimeSpan? grace = null)
    {
        var configuration = new GameConfiguration();
        if (grace is { } g)
            configuration.LootGracePeriod = g;

        var instance = Substitute.For<IMapInstance>();
        instance.OwnerCharacterId.Returns(owner);

        return new InstanceOwnerLootAllocator(Options.Create(configuration), new FixedTimeProvider(Now))
            .Allocate(Substitute.For<ICreature>(), instance);
    }

    [Fact]
    public void Give_The_Instance_Owner_The_Drop_For_The_Grace_Period()
    {
        LootAllocation allocation = Allocate(owner: 7);

        Assert.Equal(7u, allocation.OwnerCharacterId);
        Assert.Equal(Now.UtcDateTime + TimeSpan.FromSeconds(30), allocation.FreeForAllAt);
        Assert.Equal(DateTimeKind.Utc, allocation.FreeForAllAt.Kind);
    }

    [Fact]
    public void Make_The_Drop_Free_For_All_At_Once_When_The_Instance_Has_No_Owner()
    {
        LootAllocation allocation = Allocate(owner: null);

        Assert.Null(allocation.OwnerCharacterId);
        Assert.Equal(Now.UtcDateTime, allocation.FreeForAllAt);
    }

    [Fact]
    public void Read_The_Grace_Period_From_Configuration()
    {
        Assert.Equal(Now.UtcDateTime + TimeSpan.FromSeconds(5),
            Allocate(owner: 7, grace: TimeSpan.FromSeconds(5)).FreeForAllAt);
    }

    [Fact]
    public void Default_To_A_Thirty_Second_Grace_And_A_Five_Metre_Pickup_Range()
    {
        var configuration = new GameConfiguration();

        Assert.Equal(TimeSpan.FromSeconds(30), configuration.LootGracePeriod);
        Assert.Equal(5f, configuration.LootPickupRange);
    }
}
