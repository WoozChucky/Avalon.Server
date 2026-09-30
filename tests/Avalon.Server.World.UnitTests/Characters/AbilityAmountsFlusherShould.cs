using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;
using Avalon.Combat;

namespace Avalon.Server.World.UnitTests.Characters;

/// <summary>
/// #669: a character's own client is told each ability's per-hit amount again, whole, whenever a stats
/// refresh moves one of them, and only then.
/// </summary>
public class AbilityAmountsFlusherShould
{
    private static DerivedCharacterStats Stats(uint attack = 40, uint ability = 20, uint weaponMin = 24, uint weaponMax = 28,
        float crit = 5f) =>
        new(MaxHealth: 240, MaxPower: 100, Stamina: 22, Strength: 23, Agility: 20, Intellect: 21, Armor: 8,
            BlockPct: 3f, DodgePct: 4f, CritPct: crit, AttackDamage: attack, AbilityDamage: ability,
            WeaponMin: weaponMin, WeaponMax: weaponMax);

    /// <summary>A weapon strike (10 + 0.5 × attack + weapon roll) and a heal (40 + 0.5 × ability damage).</summary>
    private static CharacterEntity Character()
    {
        CharacterEntity character = New();
        AbilityTemplate strike = AbilityTestData.Cone(1);
        strike.ScalingCoefficient = 0.5f;
        strike.BaseDamageCoefficient = 1f;
        AbilityTemplate heal = AbilityTestData.HealCircle(2);
        heal.ScalingStat = ScalingStat.Ability;
        heal.ScalingCoefficient = 0.5f;
        character.Spells.Load([AbilityTestData.Game(strike), AbilityTestData.Game(heal)]);
        character.ApplyStats(Stats(), CurrentValues.EnterWorld, TestCombat.Formula);
        return character;
    }

    private static IWorldConnection Recording(CharacterEntity character, List<NetworkPacket> sent)
    {
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        TestTown.Record(connection, character, sent);
        return connection;
    }

    private static List<SCharacterAbilityAmountsPacket> Updates(List<NetworkPacket> sent) =>
        TestTown.Read<SCharacterAbilityAmountsPacket>(sent, NetworkPacketType.SMSG_CHARACTER_ABILITY_AMOUNTS);

    private static (AbilityAmountKind, uint, uint) Of(AbilityAmountInfo a) => (a.Kind, a.Min, a.Max);

    [Fact]
    public void Send_every_abilitys_amount_once_and_not_again_while_nothing_changes()
    {
        CharacterEntity character = Character();
        var sent = new List<NetworkPacket>();
        IWorldConnection connection = Recording(character, sent);

        AbilityAmountsFlusher.Flush(connection);
        AbilityAmountsFlusher.Flush(connection);

        SCharacterAbilityAmountsPacket update = Assert.Single(Updates(sent));
        Assert.Equal([1u, 2u], update.Amounts.Select(a => a.AbilityId));
        Assert.Equal((AbilityAmountKind.Damage, 54u, 58u), Of(update.Amounts[0]));
        Assert.Equal((AbilityAmountKind.Healing, 50u, 50u), Of(update.Amounts[1]));
    }

    /// <summary>A new main hand, or more attack damage, reaches the tooltip on the tick it is equipped.</summary>
    [Fact]
    public void Send_the_new_amounts_after_a_stats_refresh_that_moves_them()
    {
        CharacterEntity character = Character();
        var sent = new List<NetworkPacket>();
        IWorldConnection connection = Recording(character, sent);
        AbilityAmountsFlusher.Flush(connection);

        character.ApplyStats(Stats(attack: 50, weaponMin: 30, weaponMax: 40), CurrentValues.KeepShare, TestCombat.Formula);
        AbilityAmountsFlusher.Flush(connection);

        Assert.Equal(2, Updates(sent).Count);
        SCharacterAbilityAmountsPacket update = Updates(sent)[1];
        Assert.Equal((AbilityAmountKind.Damage, 65u, 75u), Of(update.Amounts[0]));
        Assert.Equal((AbilityAmountKind.Healing, 50u, 50u), Of(update.Amounts[1]));
    }

    /// <summary>A refresh that moves no amount (here only the crit chance) sends nothing.</summary>
    [Fact]
    public void Send_nothing_after_a_stats_refresh_that_moves_no_amount()
    {
        CharacterEntity character = Character();
        var sent = new List<NetworkPacket>();
        IWorldConnection connection = Recording(character, sent);
        AbilityAmountsFlusher.Flush(connection);

        character.ApplyStats(Stats(crit: 20f), CurrentValues.KeepShare, TestCombat.Formula);
        AbilityAmountsFlusher.Flush(connection);

        Assert.Single(Updates(sent));
    }

    [Fact]
    public void Send_nothing_to_a_connection_with_no_character()
    {
        var sent = new List<NetworkPacket>();
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns((Avalon.World.Public.Characters.ICharacter?)null);
        connection.When(c => c.Send(Arg.Any<NetworkPacket>())).Do(ci => sent.Add(ci.Arg<NetworkPacket>()));

        AbilityAmountsFlusher.Flush(connection);

        Assert.Empty(sent);
    }
}
