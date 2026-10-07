using Avalon.Common;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Abilities.Targeting;
using Avalon.World.Entities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Abilities;

public class HostilityShould
{
    private static CharacterEntity Player(uint id, bool pvp)
    {
        CharacterEntity character = TestCharacters.New(id);
        character.Data!.PvpEnabled = pvp;
        return character;
    }

    private static ICreature Creature(bool invulnerable = false, uint id = 0)
    {
        ICreature creature = Substitute.For<ICreature>();
        creature.Invulnerable.Returns(invulnerable);
        creature.Guid.Returns(new ObjectGuid(ObjectType.Creature, id));
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

    /// <summary>#163: a creature caster finds every living player hostile, flagged or not, on every map type.</summary>
    [Theory]
    [InlineData(false, MapType.Normal)]
    [InlineData(true, MapType.Normal)]
    [InlineData(false, MapType.Town)]
    public void Make_a_living_player_hostile_to_a_creature_caster_on_every_map(bool pvp, MapType map) =>
        Assert.True(Hostility.IsHostile(Creature(), Player(1, pvp), map));

    [Fact]
    public void Never_make_a_dead_player_hostile_to_a_creature_caster()
    {
        CharacterEntity dead = Player(1, pvp: false);
        dead.IsDead = true;

        Assert.False(Hostility.IsHostile(Creature(), dead, MapType.Normal));
    }

    /// <summary>A creature caster's only ally is itself: creature heals on other creatures are out of scope (#163).</summary>
    [Fact]
    public void Count_only_itself_as_a_creature_casters_ally()
    {
        ICreature caster = Creature(id: 1);

        Assert.True(Hostility.IsAlly(caster, caster, MapType.Normal));
        Assert.False(Hostility.IsAlly(caster, Creature(id: 2), MapType.Normal));
        Assert.False(Hostility.IsAlly(caster, Player(1, pvp: false), MapType.Normal));
    }

    [Fact]
    public void Make_no_creature_hostile_to_a_creature_caster() =>
        Assert.False(Hostility.IsHostile(Creature(id: 1), Creature(id: 2), MapType.Normal));

    /// <summary>Two entities for one character (a relog's old and new copy) count as the same unit.</summary>
    [Fact]
    public void Treat_two_entities_with_one_guid_as_the_caster_itself()
    {
        CharacterEntity caster = Player(1, pvp: true);
        CharacterEntity sameGuid = Player(1, pvp: true);

        Assert.False(Hostility.IsHostile(caster, sameGuid, MapType.Normal));
        Assert.True(Hostility.IsAlly(caster, sameGuid, MapType.Normal));
    }

    /// <summary>A character that is not the World-side entity is neither hostile nor an ally, which fails safe.</summary>
    [Fact]
    public void Never_count_a_character_that_is_not_the_world_side_entity_as_an_ally()
    {
        ICharacter foreign = Substitute.For<ICharacter>();
        foreign.Guid.Returns(new ObjectGuid(ObjectType.Character, 99));
        CharacterEntity caster = Player(1, pvp: false);

        Assert.False(Hostility.IsAlly(caster, foreign, MapType.Normal));
        Assert.False(Hostility.IsAlly(foreign, caster, MapType.Normal));
        Assert.False(Hostility.IsHostile(caster, foreign, MapType.Normal));
    }
}
