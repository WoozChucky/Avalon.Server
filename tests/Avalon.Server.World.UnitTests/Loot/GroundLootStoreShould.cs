using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Loot;

namespace Avalon.Server.World.UnitTests.Loot;

public class GroundLootStoreShould
{
    private static GroundLoot Pile(uint id) => new()
    {
        Guid = new ObjectGuid(ObjectType.Loot, id),
        Position = Vector3.zero,
        Gold = 10,
        FreeForAllAt = DateTime.UnixEpoch
    };

    [Fact]
    public void Keep_A_Drop_Findable_By_The_Guid_It_Was_Added_Under_When_That_Guid_Object_Changes()
    {
        // ObjectGuid is a mutable class. The store keys on the raw value at Add, so a later Set on
        // the drop's own guid object cannot strand the entry under a key nothing can reach.
        var store = new GroundLootStore();
        GroundLoot drop = Pile(1);
        store.Add(drop);

        drop.Guid.Set(ObjectType.Loot, 5);

        Assert.True(store.TryGet(new ObjectGuid(ObjectType.Loot, 1), out GroundLoot? found));
        Assert.Same(drop, found);
        Assert.True(store.Remove(new ObjectGuid(ObjectType.Loot, 1)));
        Assert.Equal(0, store.Count);
    }
}
