using Avalon.World;
using Avalon.World.Configuration;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Microsoft.Extensions.Options;

namespace Avalon.Server.World.UnitTests.Inventory;

/// <summary>The real economy over a test world, its item rolls (#1179) seeded so a run's rolls are the same every time.</summary>
internal static class TestEconomy
{
    public static CharacterEconomy Over(IWorld world)
    {
        var ids = new ItemIdAllocator();
        return new CharacterEconomy(world, ids, Roller(ids));
    }

    /// <summary>A roller over <paramref name="ids" />, its numbers seeded.</summary>
    public static ItemRoller Roller(IItemIdAllocator ids) =>
        new(new LootRandom(new Random(1179)), ids, Options.Create(new GameConfiguration()));
}
