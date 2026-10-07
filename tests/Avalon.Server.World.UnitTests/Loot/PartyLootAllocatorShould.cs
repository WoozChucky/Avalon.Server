using Avalon.Common;
using Avalon.Server.World.UnitTests.Combat;
using Avalon.World.Configuration;
using Avalon.World.Loot;
using Avalon.World.Parties;
using Avalon.World.Public.Characters;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Loot;

public class PartyLootAllocatorShould
{
    private static readonly DateTimeOffset s_now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private static ICharacter Member(uint id)
    {
        ICharacter c = Substitute.For<ICharacter>();
        c.Guid.Returns(new ObjectGuid(ObjectType.Character, id));
        return c;
    }

    private static PartyLootAllocator Allocator(ScriptedCombatRandom random) =>
        new(Options.Create(new GameConfiguration()), new FixedTimeProvider(s_now), random);

    /// <summary>Every allocation that needs no draw; the scripted random has no number, so a draw would throw.</summary>
    [Theory]
    [InlineData(7u, null, new uint[] { 8 }, 7u, 30)]       // a solo instance: its owner's, for the grace period
    [InlineData(null, null, new uint[] { 8 }, null, 0)]    // a town: free for all at once
    [InlineData(null, 1u, new uint[] { 5 }, 5u, 30)]       // a party with one eligible member: no draw
    [InlineData(null, 1u, new uint[0], null, 0)]           // a party kill nobody is eligible for: free for all at once
    public void Allocate_without_a_draw(uint? owner, uint? party, uint[] eligible, uint? expected, int reservedSeconds)
    {
        LootAllocation got = Allocator(new ScriptedCombatRandom())
            .Allocate(owner, party is { } id ? new PartyId(id) : null, eligible.Select(Member).ToList());

        Assert.Equal(expected, got.OwnerCharacterId);
        Assert.Equal(s_now.UtcDateTime + TimeSpan.FromSeconds(reservedSeconds), got.FreeForAllAt);
    }

    [Fact]
    public void Draw_one_eligible_member_per_drop_in_a_party_instance_and_reserve_it_for_the_grace_period()
    {
        ScriptedCombatRandom random = new ScriptedCombatRandom().Longs(2, 0);
        PartyLootAllocator allocator = Allocator(random);
        ICharacter[] eligible = [Member(1), Member(2), Member(3)];

        LootAllocation first = allocator.Allocate(null, new PartyId(1), eligible);

        Assert.Equal(3u, first.OwnerCharacterId);
        Assert.Equal(s_now.UtcDateTime + TimeSpan.FromSeconds(30), first.FreeForAllAt);
        Assert.Equal(1u, allocator.Allocate(null, new PartyId(1), eligible).OwnerCharacterId);
        Assert.Equal([(0L, 2L), (0L, 2L)], random.WeaponRolls); // drawn over the three members' indices
    }
}
