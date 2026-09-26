using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Abilities.Targeting;
using Avalon.World.Entities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Abilities;

public class HostilityShould
{
    private static CharacterEntity Player(uint id, bool pvp)
    {
        CharacterEntity character = TestCharacters.New(id);
        character.Data!.PvpEnabled = pvp;
        return character;
    }

    private static ICreature Creature(bool invulnerable = false)
    {
        var creature = Substitute.For<ICreature>();
        creature.Invulnerable.Returns(invulnerable);
        return creature;
    }

    [Fact]
    public void Make_a_creature_hostile_to_a_player() =>
        Assert.True(Hostility.IsHostile(Player(1, pvp: false), Creature(), MapType.Normal));

    [Fact]
    public void Never_make_an_invulnerable_creature_hostile() =>
        Assert.False(Hostility.IsHostile(Player(1, pvp: true), Creature(invulnerable: true), MapType.Normal));

    [Theory]
    [InlineData(true, true, MapType.Normal, true)]
    [InlineData(true, false, MapType.Normal, false)]
    [InlineData(false, true, MapType.Normal, false)]
    [InlineData(false, false, MapType.Normal, false)]
    [InlineData(true, true, MapType.Town, false)]    // towns never allow PvP
    public void Make_players_hostile_only_when_both_are_flagged_outside_a_town(
        bool casterPvp, bool targetPvp, MapType map, bool expected) =>
        Assert.Equal(expected, Hostility.IsHostile(Player(1, casterPvp), Player(2, targetPvp), map));

    [Fact]
    public void Never_make_the_caster_hostile_to_itself()
    {
        CharacterEntity caster = Player(1, pvp: true);
        Assert.False(Hostility.IsHostile(caster, caster, MapType.Normal));
    }

    [Fact]
    public void Count_the_caster_and_unflagged_players_as_allies_but_never_a_creature()
    {
        CharacterEntity caster = Player(1, pvp: true);

        Assert.True(Hostility.IsAlly(caster, caster, MapType.Normal));
        Assert.True(Hostility.IsAlly(caster, Player(2, pvp: false), MapType.Normal));
        Assert.False(Hostility.IsAlly(caster, Player(3, pvp: true), MapType.Normal));   // hostile, so not an ally
        Assert.True(Hostility.IsAlly(caster, Player(4, pvp: true), MapType.Town));
        Assert.False(Hostility.IsAlly(caster, Creature(), MapType.Normal));
        Assert.False(Hostility.IsAlly(caster, Creature(invulnerable: true), MapType.Town));
    }

    [Fact]
    public void Make_nothing_hostile_to_a_creature_caster_until_creatures_cast() =>
        Assert.False(Hostility.IsHostile(Creature(), Player(1, pvp: true), MapType.Normal));
}
