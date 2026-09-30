using Avalon.Common;
using Avalon.Server.World.UnitTests.Combat;
using Avalon.World.Configuration;
using Avalon.World.Loot;
using Avalon.World.Parties;
using Avalon.World.Public.Characters;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Loot;

public class PartyLootAllocatorShould
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private static ICharacter Member(uint id)
    {
        ICharacter c = Substitute.For<ICharacter>();
        c.Guid.Returns(new ObjectGuid(ObjectType.Character, id));
        return c;
    }

    private static PartyLootAllocator Allocator(ScriptedCombatRandom random) =>
        new(Options.Create(new GameConfiguration()), new FixedTimeProvider(Now), random);

    [Fact]
    public void Reserve_a_solo_instances_drops_for_its_owner()
    {
        LootAllocation got = Allocator(new ScriptedCombatRandom()).Allocate(7, null, [Member(8)]);

        Assert.Equal(7u, got.OwnerCharacterId);
        Assert.Equal(Now.UtcDateTime + TimeSpan.FromSeconds(30), got.FreeForAllAt);
    }

    [Fact]
    public void Make_a_towns_drops_free_for_all_at_once()
    {
        LootAllocation got = Allocator(new ScriptedCombatRandom()).Allocate(null, null, [Member(8)]);

        Assert.Null(got.OwnerCharacterId);
        Assert.Equal(Now.UtcDateTime, got.FreeForAllAt);
    }

    [Fact]
    public void Draw_one_eligible_member_per_drop_in_a_party_instance()
    {
        var random = new ScriptedCombatRandom().Longs(2, 0);
        PartyLootAllocator allocator = Allocator(random);
        ICharacter[] eligible = [Member(1), Member(2), Member(3)];

        Assert.Equal(3u, allocator.Allocate(null, new PartyId(1), eligible).OwnerCharacterId);
        Assert.Equal(1u, allocator.Allocate(null, new PartyId(1), eligible).OwnerCharacterId);
        Assert.Equal([(0L, 2L), (0L, 2L)], random.WeaponRolls); // drawn over the three members' indices
    }

    [Fact]
    public void Reserve_a_party_drop_for_the_grace_period()
    {
        var random = new ScriptedCombatRandom().Longs(1);

        LootAllocation got = Allocator(random).Allocate(null, new PartyId(1), [Member(1), Member(2)]);

        Assert.Equal(2u, got.OwnerCharacterId);
        Assert.Equal(Now.UtcDateTime + TimeSpan.FromSeconds(30), got.FreeForAllAt);
    }

    [Fact]
    public void Draw_nothing_for_a_single_eligible_member() =>
        Assert.Equal(5u, Allocator(new ScriptedCombatRandom()).Allocate(null, new PartyId(1), [Member(5)]).OwnerCharacterId);

    [Fact]
    public void Make_a_party_drop_free_for_all_when_nobody_is_eligible()
    {
        LootAllocation got = Allocator(new ScriptedCombatRandom()).Allocate(null, new PartyId(1), []);

        Assert.Null(got.OwnerCharacterId);
        Assert.Equal(Now.UtcDateTime, got.FreeForAllAt);
    }
}
