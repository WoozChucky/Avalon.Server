using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Loot;
using Avalon.World.Loot;
using Avalon.World.Public.Maps;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Loot;

public class LootPlacementShould
{
    private static readonly Vector3 Corpse = new(10f, 3f, 20f);
    private static readonly DateTime FreeAt = new(2026, 9, 25, 12, 0, 30, DateTimeKind.Utc);

    private static IMapNavigator OpenGround(float groundY = 7f)
    {
        var navigator = Substitute.For<IMapNavigator>();
        navigator.RaycastWalkable(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns(ci => ci.ArgAt<Vector3>(1));
        navigator.SampleGroundHeight(Arg.Any<float>(), Arg.Any<float>(), Arg.Any<float>()).Returns(groundY);
        return navigator;
    }

    private static Func<uint> Ids(uint first = 100)
    {
        uint next = first;
        return () => next++;
    }

    private static IReadOnlyList<RolledDrop> FourDrops() =>
    [
        RolledDrop.Item(new ItemTemplateId(1), 3),
        RolledDrop.Item(new ItemTemplateId(4), 1),
        RolledDrop.Item(new ItemTemplateId(4), 1),
        RolledDrop.GoldPile(25),
    ];

    [Fact]
    public void Spread_Drops_On_A_Ring_Around_The_Corpse()
    {
        IReadOnlyList<GroundLoot> drops = LootPlacement.Place(
            Corpse, FourDrops(), new LootAllocation(7, FreeAt), OpenGround(), Ids());

        Assert.Equal(4, drops.Count);
        Assert.Equal(4, drops.Select(d => (d.Position.x, d.Position.z)).Distinct().Count());
        Assert.All(drops, d =>
        {
            float dx = d.Position.x - Corpse.x, dz = d.Position.z - Corpse.z;
            Assert.Equal(LootPlacement.RingRadius, MathF.Sqrt(dx * dx + dz * dz), precision: 3);
        });

        // Drop i of n sits at angle 2*pi*i/n, starting along +x and turning towards +z.
        Assert.Equal(Corpse.x + 1f, drops[0].Position.x, precision: 3);
        Assert.Equal(Corpse.z, drops[0].Position.z, precision: 3);
        Assert.Equal(Corpse.x, drops[1].Position.x, precision: 3);
        Assert.Equal(Corpse.z + 1f, drops[1].Position.z, precision: 3);
    }

    [Fact]
    public void Snap_Every_Drop_To_The_Ground_Searching_From_The_Corpse_Height()
    {
        IMapNavigator navigator = OpenGround(groundY: 7f);

        IReadOnlyList<GroundLoot> drops = LootPlacement.Place(
            Corpse, FourDrops(), new LootAllocation(7, FreeAt), navigator, Ids());

        Assert.All(drops, d => Assert.Equal(7f, d.Position.y));
        navigator.Received(4).SampleGroundHeight(Arg.Any<float>(), Arg.Is(Corpse.y), Arg.Any<float>());
    }

    [Fact]
    public void Keep_A_Drop_On_This_Side_Of_A_Wall()
    {
        // The navmesh says nothing past the corpse is walkable: every ring point is pulled back to it.
        var navigator = Substitute.For<IMapNavigator>();
        navigator.RaycastWalkable(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns(ci => ci.ArgAt<Vector3>(0));
        navigator.SampleGroundHeight(Arg.Any<float>(), Arg.Any<float>(), Arg.Any<float>()).Returns(3f);

        IReadOnlyList<GroundLoot> drops = LootPlacement.Place(
            Corpse, FourDrops(), new LootAllocation(7, FreeAt), navigator, Ids());

        Assert.All(drops, d => Assert.Equal(Corpse, d.Position));
        // The ground is sampled where the drop was pulled back to, not at the ring point in the wall.
        navigator.Received(4).SampleGroundHeight(Corpse.x, Corpse.y, Corpse.z);
    }

    [Fact]
    public void Carry_What_Was_Rolled_And_Who_It_Was_Allocated_To()
    {
        IReadOnlyList<GroundLoot> drops = LootPlacement.Place(
            Corpse, FourDrops(), new LootAllocation(7, FreeAt), OpenGround(), Ids(first: 100));

        Assert.Equal([100u, 101u, 102u, 103u], drops.Select(d => d.Guid.Id));
        Assert.All(drops, d => Assert.Equal(ObjectType.Loot, d.Guid.Type));
        Assert.All(drops, d => Assert.Equal(7u, d.OwnerCharacterId));
        Assert.All(drops, d => Assert.Equal(FreeAt, d.FreeForAllAt));
        Assert.Equal(new ItemTemplateId(1), drops[0].ItemTemplateId);
        Assert.Equal(3u, drops[0].Count);
        Assert.True(drops[3].IsGold);
        Assert.Equal(25UL, drops[3].Gold);
    }

    [Fact]
    public void Map_A_Drop_To_The_Wire_Shape()
    {
        GroundLoot sword = LootPlacement.Place(Corpse, [RolledDrop.Item(new ItemTemplateId(4), 1)],
            new LootAllocation(7, FreeAt), OpenGround(), Ids())[0];
        GroundLoot pile = LootPlacement.Place(Corpse, [RolledDrop.GoldPile(25)],
            new LootAllocation(null, FreeAt), OpenGround(), Ids())[0];

        LootDropDto swordDto = LootDropMapper.ToDto(sword);
        LootDropDto pileDto = LootDropMapper.ToDto(pile);

        Assert.Equal(sword.Guid.RawValue, swordDto.LootGuid);
        Assert.Equal(4UL, swordDto.ItemTemplateId);
        Assert.Equal(1u, swordDto.Count);
        Assert.Null(swordDto.Gold);
        Assert.Equal(7u, swordDto.OwnerCharacterId);
        Assert.Equal(FreeAt.Ticks, swordDto.FreeForAllAt);
        Assert.Equal(sword.Position.x, swordDto.Position.X);
        Assert.Null(pileDto.ItemTemplateId);
        Assert.Equal(25UL, pileDto.Gold);
        Assert.Null(pileDto.OwnerCharacterId);
    }
}
