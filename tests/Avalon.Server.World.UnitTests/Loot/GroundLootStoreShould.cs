using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Loot;
using Xunit;

namespace Avalon.Server.World.UnitTests.Loot;

public class GroundLootStoreShould
{
    private static GroundLoot Pile(uint id) => new()
    {
        Guid = new ObjectGuid(ObjectType.Loot, id), Position = Vector3.zero, Gold = 10, FreeForAllAt = DateTime.UnixEpoch
    };

    [Fact]
    public void Find_A_Drop_Until_It_Is_Removed()
    {
        var store = new GroundLootStore();
        store.Add(Pile(1));
        store.Add(Pile(2));

        Assert.True(store.TryGet(new ObjectGuid(ObjectType.Loot, 1), out GroundLoot? found));
        Assert.Equal(10UL, found!.Gold);
        Assert.True(store.Remove(new ObjectGuid(ObjectType.Loot, 1)));
        Assert.False(store.TryGet(new ObjectGuid(ObjectType.Loot, 1), out _));
        Assert.False(store.Remove(new ObjectGuid(ObjectType.Loot, 1)));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Forget_Everything_On_Clear()
    {
        var store = new GroundLootStore();
        store.Add(Pile(1));
        store.Add(Pile(2));

        store.Clear();

        Assert.Equal(0, store.Count);
        Assert.Empty(store.All);
    }
}
