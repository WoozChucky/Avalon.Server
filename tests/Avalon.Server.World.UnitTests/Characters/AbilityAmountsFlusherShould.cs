using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Combat;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
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

    /// <summary>
    /// A new main hand, or more physical attack damage, increased or more, reaches the tooltip on the tick it is equipped
    /// (24..28 × 1.5 before: 36..42).
    /// </summary>
    [Theory]
    [InlineData("weapon", 45u, 60u)]     // 30..40 × 1.5
    [InlineData("increased", 38u, 44u)]  // 24..28 × 1.6
    [InlineData("more", 43u, 50u)]       // 24..28 × 1.5 × 1.2
    public void Send_the_new_amounts_after_a_stats_refresh_that_moves_them(string change, uint min, uint max)
    {
        CharacterEntity character = Character();
        var sent = new List<OutboundPacket>();
        IWorldConnection connection = Recording(character, sent);
        AbilityAmountsFlusher.Flush(connection);

        DerivedCharacterStats stats = change switch
        {
            "weapon" => Stats(weaponMin: 30, weaponMax: 40),
            "increased" => Stats(physicalAttack: 60f),
            _ => Stats(),
        };
        if (change == "more")
        {
            OffenceStats offence = stats.Offence;
            offence.AddMore(OffenceStat.PhysicalAttack, 20f);
            stats = stats with { Offence = offence };
        }

        character.ApplyStats(stats, CurrentValues.KeepShare, TestCombat.Formula);
        AbilityAmountsFlusher.Flush(connection);

        Assert.Equal(2, Updates(sent).Count);
        SCharacterAbilityAmountsPacket update = Updates(sent)[1];
        Assert.Equal((AbilityAmountKind.Damage, min, max), Of(update.Amounts[0]));
        Assert.Equal((AbilityAmountKind.Healing, 50u, 50u), Of(update.Amounts[1]));
    }

    /// <summary>
    /// #1178 (B-R9, D1): each entry carries the pace the server computes. A Cleave-shaped basic attack waits its weapon's
    /// attack time over attack speed (a level-1 Warrior's Agility 20 gives 2 %: item 32's 792 ms / 1.02 → 776, an 801 ms
    /// weapon's → 785), so a weapon swap resends it; a Spark Bolt-shaped basic spell waits its 800 ms row over cast speed,
    /// which nothing gives, whatever the weapon.
    /// </summary>
    [Fact]
    public void Resend_each_abilitys_pace_when_the_weapon_changes()
    {
        var warriorOne = new ClassLevelStat
        {
            Class = CharacterClass.Warrior,
            Level = 1,
            BaseHp = 130,
            Stamina = 22,
            Strength = 23,
            Agility = 20,
            Intellect = 20,
        };
        DerivedCharacterStats Wearing(ItemTemplate weapon) => CharacterStatsCalculator.Calculate(warriorOne, [weapon],
            TestCombat.FactorsOf(CharacterClass.Warrior), TestCombat.Formula);

        AbilityTemplate cleave = AbilityTestData.Cone(200);
        cleave.Flags = AbilityFlags.Basic;
        cleave.DamageEffectiveness = 315f;
        AbilityTemplate sparkBolt = AbilityTestData.Projectile(210).Dealing(14);
        sparkBolt.Flags = AbilityFlags.Basic;
        sparkBolt.Cooldown = 800;
        sparkBolt.DamageType = DamageType.Lightning;
        CharacterEntity character = New();
        character.Spells.Load([AbilityTestData.Game(cleave), AbilityTestData.Game(sparkBolt)]);
        character.ApplyStats(Wearing(Weapon(32, 792)), CurrentValues.EnterWorld, TestCombat.Formula);
        var sent = new List<OutboundPacket>();
        IWorldConnection connection = Recording(character, sent);

        AbilityAmountsFlusher.Flush(connection);
        character.ApplyStats(Wearing(Weapon(1178_701, 801)), CurrentValues.KeepShare, TestCombat.Formula);
        AbilityAmountsFlusher.Flush(connection);

        List<SCharacterAbilityAmountsPacket> updates = Updates(sent);
        Assert.Equal(2, updates.Count);
        Assert.Equal((DamageTypeMask.Physical, 776u, 0u), Pace(updates[0].Amounts[0]));
        Assert.Equal((DamageTypeMask.Lightning, 800u, 0u), Pace(updates[0].Amounts[1]));
        Assert.Equal((DamageTypeMask.Physical, 785u, 0u), Pace(updates[1].Amounts[0]));
        Assert.Equal((DamageTypeMask.Lightning, 800u, 0u), Pace(updates[1].Amounts[1]));
    }

    private static (DamageTypeMask, uint, uint) Pace(AbilityAmountInfo a) => (a.Types, a.CooldownMs, a.CastTimeMs);

    private static ItemTemplate Weapon(ulong id, uint attackTimeMs) => new()
    {
        Id = new ItemTemplateId(id),
        Name = "Sword",
        Class = ItemClass.Weapon,
        SubClass = ItemSubClass.OneHanded,
        Slot = ItemSlotType.MainHand,
        MaxStackSize = 1,
        DamageMin1 = 4,
        DamageMax1 = 7,
        DamageType1 = DamageType.Physical,
        AttackTimeMs = attackTimeMs,
    };

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
