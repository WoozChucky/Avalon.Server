using Avalon.Combat;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Combat;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Public;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Characters;

/// <summary>
/// #669: a character's own client is told each ability's per-hit amount again, whole, whenever a stats
/// refresh moves one of them, and only then.
/// </summary>
public class AbilityAmountsFlusherShould
{
    private static DerivedCharacterStats Stats(float physicalAttack = 50f, uint weaponMin = 24, uint weaponMax = 28,
        float crit = 5f)
    {
        OffenceStats offence = default;
        offence.AddIncreased(OffenceStat.PhysicalAttack, physicalAttack);
        offence.AddIncreased(OffenceStat.Healing, 25f);
        return new(MaxHealth: 240, MaxPower: 100, Stamina: 22, Strength: 23, Agility: 20, Intellect: 21, Armor: 8,
            BlockPct: 3f, Evasion: 4, CritPct: crit,
            Weapon: new WeaponDamage(weaponMin, weaponMax, DamageType.Physical, 0, 0, null, 800), Offence: offence);
    }

    /// <summary>
    /// #1178: a weapon strike (its roll × 1.5 for 50 % increased physical attack damage) and a heal (its 40 spell base ×
    /// 1.25 for 25 % increased healing).
    /// </summary>
    private static CharacterEntity Character()
    {
        CharacterEntity character = New();
        AbilityTemplate strike = AbilityTestData.Cone(1);
        AbilityTemplate heal = AbilityTestData.HealCircle(2);
        character.Spells.Load([AbilityTestData.Game(strike), AbilityTestData.Game(heal)]);
        character.ApplyStats(Stats(), CurrentValues.EnterWorld, TestCombat.Formula);
        return character;
    }

    private static IWorldConnection Recording(CharacterEntity character, List<OutboundPacket> sent)
    {
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        TestTown.Record(connection, character, sent);
        return connection;
    }

    private static List<SCharacterAbilityAmountsPacket> Updates(List<OutboundPacket> sent) =>
        TestTown.Read<SCharacterAbilityAmountsPacket>(sent, NetworkPacketType.SMSG_CHARACTER_ABILITY_AMOUNTS);

    private static (AbilityAmountKind, uint, uint) Of(AbilityAmountInfo a) => (a.Kind, a.Min, a.Max);

    [Fact]
    public void Send_every_abilitys_amount_once_and_not_again_while_nothing_changes()
    {
        CharacterEntity character = Character();
        var sent = new List<OutboundPacket>();
        IWorldConnection connection = Recording(character, sent);

        AbilityAmountsFlusher.Flush(connection);
        AbilityAmountsFlusher.Flush(connection);

        SCharacterAbilityAmountsPacket update = Assert.Single(Updates(sent));
        Assert.Equal([1u, 2u], update.Amounts.Select(a => a.AbilityId));
        Assert.Equal((AbilityAmountKind.Damage, 36u, 42u), Of(update.Amounts[0]));
        Assert.Equal((AbilityAmountKind.Healing, 50u, 50u), Of(update.Amounts[1]));
    }

    /// <summary>A new main hand, or more physical attack damage, reaches the tooltip on the tick it is equipped.</summary>
    [Fact]
    public void Send_the_new_amounts_after_a_stats_refresh_that_moves_them()
    {
        CharacterEntity character = Character();
        var sent = new List<OutboundPacket>();
        IWorldConnection connection = Recording(character, sent);
        AbilityAmountsFlusher.Flush(connection);

        character.ApplyStats(Stats(physicalAttack: 60f, weaponMin: 30, weaponMax: 40), CurrentValues.KeepShare, TestCombat.Formula);
        AbilityAmountsFlusher.Flush(connection);

        Assert.Equal(2, Updates(sent).Count);
        SCharacterAbilityAmountsPacket update = Updates(sent)[1];
        Assert.Equal((AbilityAmountKind.Damage, 48u, 64u), Of(update.Amounts[0]));
        Assert.Equal((AbilityAmountKind.Healing, 50u, 50u), Of(update.Amounts[1]));
    }

    /// <summary>A refresh that moves no amount (here only the crit chance) sends nothing.</summary>
    [Fact]
    public void Send_nothing_after_a_stats_refresh_that_moves_no_amount()
    {
        CharacterEntity character = Character();
        var sent = new List<OutboundPacket>();
        IWorldConnection connection = Recording(character, sent);
        AbilityAmountsFlusher.Flush(connection);

        character.ApplyStats(Stats(crit: 20f), CurrentValues.KeepShare, TestCombat.Formula);
        AbilityAmountsFlusher.Flush(connection);

        Assert.Single(Updates(sent));
    }

    [Fact]
    public void Send_nothing_to_a_connection_with_no_character()
    {
        var sent = new List<OutboundPacket>();
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns((Avalon.World.Public.Characters.ICharacter?)null);
        connection.When(c => c.Send(Arg.Any<OutboundPacket>())).Do(ci => sent.Add(ci.Arg<OutboundPacket>()));

        AbilityAmountsFlusher.Flush(connection);

        Assert.Empty(sent);
    }
}
