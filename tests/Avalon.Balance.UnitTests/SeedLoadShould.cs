using Avalon.Balance.Core;
using Avalon.Balance.Data;
using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
using Avalon.World.Public.Abilities;
using Xunit;

namespace Avalon.Balance.UnitTests;

public class SeedLoadShould
{
    /// <summary>
    /// A level-1 Warrior bare and in the checked-in forest profile (B-R4: each piece a fixed copy at the midpoint of its
    /// ranges): the class row sets its mana, Strength its physical attack, Stamina its health, and the gear its armour and
    /// its weapon, bare the unarmed one. Forest's physical attack is (23 + 2) x 3.5, the Barkplate Legguards' Strength
    /// implicit 2 at its midpoint; its weapon's attack time and least damage are the midpoints of item 7's ranges.
    /// </summary>
    [Theory]
    [InlineData("none", 80.5f, 240u, 2u, 816u, 3u)]      // 23 x 3.5; 130 + 22 x 5; 23 x 0.1, truncated
    [InlineData("forest", 87.5f, 258u, 59u, 700u, 6u)]
    public void Reproduce_a_level_one_warrior(string gear, float physicalAttack, uint maxHealth, uint armor, uint attackTimeMs,
        uint weaponMin)
    {
        BalanceData data = TestData.Seeded;
        DerivedCharacterStats warrior = data.CharacterStats(CharacterClass.Warrior, 1, TestData.Gear(gear, CharacterClass.Warrior));

        Assert.Equal(180u, warrior.MaxPower);      // 100 + 20 x 4
        Assert.Equal(physicalAttack, warrior.Offence.Increased(OffenceStat.PhysicalAttack));
        Assert.Equal((maxHealth, armor, attackTimeMs, weaponMin),
            (warrior.MaxHealth, warrior.Armor, warrior.Weapon.AttackTimeMs, warrior.Weapon.Min1));
    }

    [Fact]
    public void Give_cleave_its_base_for_a_level_one_warrior()
    {
        BalanceData data = TestData.Seeded;
        AbilityTemplate cleave = data.Abilities[new AbilityId(200)];
        AttackerCombat attacker = data.CharacterStats(CharacterClass.Warrior, 1, []).AttackerAt(1);

        AbilityMetadata metadata = AbilityMetadataMapper.From(cleave);
        double min = OffenceMath.DirectBase(attacker, metadata, RollEnds.Low).Total;
        double max = OffenceMath.DirectBase(attacker, metadata, RollEnds.High).Total;

        Assert.Equal(17.05725, min, precision: 4);   // 3 x 3.15 x 1.805
        Assert.Equal(34.1145, max, precision: 4);    // 6 x 3.15 x 1.805
    }

    /// <summary>
    /// The first eleven nodes of the axes build (B-R10 to B-R13): the hub's Strength path, then Hurled Axe's branch to
    /// Twin Axes. Its abilities are the starter, then Hurled Axe, which Twin Axes fans to two lanes; its lines fold into
    /// the stats as the world's refresh folds them.
    /// </summary>
    [Fact]
    public void Hold_a_builds_lines_abilities_and_shapes()
    {
        BalanceData data = TestData.Seeded;

        HeldTalents held = data.Talents.Hold(CharacterClass.Warrior, [57, 58, 59, 60, 61, 69, 70, 71, 72, 73, 311]);

        Assert.Equal([319u, 202u], held.Abilities.Select(a => a.Template.Id.Value));
        HeldAbility axe = held.Abilities[1];
        Assert.Equal(2, axe.Shaping.Apply(AbilityMetadataMapper.From(axe.Template)).ProjectileCount);

        DerivedCharacterStats stats = data.CharacterStats(CharacterClass.Warrior, 1, [], talents: held.Totals);
        Assert.Equal(33u, stats.Strength);      // 23 + S1 5 + L2.c3 5
        Assert.Equal(247u, stats.MaxHealth);    // 240 x 1.03, half away from zero
        var pace = CasterPace.From(stats.Offence, stats.Weapon.AttackTimeMs / 1000f, data.Combat.Formula);
        Assert.Equal(6d, pace.AttackSpeedPct, precision: 4);   // 2 from Agility, + 2 + 2 from L2.c2 and L2.c5
    }

    [Theory]
    [InlineData(CharacterClass.Warrior, new[] { 2, 2 }, "node 2: AlreadyAllocated")]
    [InlineData(CharacterClass.Warrior, new[] { 1 }, "node 1: AlreadyAllocated")]
    [InlineData(CharacterClass.Warrior, new[] { 3 }, "node 3: NotConnected")]
    [InlineData(CharacterClass.Warrior, new[] { 999 }, "node 999: UnknownNode")]
    [InlineData(CharacterClass.Warrior, new[] { 2, 3, 4, 5, 6, 22, 23, 24, 25, 26, 306, 307 }, "node 307: ForkLocked")]
    [InlineData(CharacterClass.Warrior, new[] { 75 }, "node 75: NotConnected")]
    public void Refuse_a_build_the_talent_rules_refuse(CharacterClass @class, int[] nodes, string message)
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => TestData.Seeded.Talents.Hold(@class, nodes));

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_every_seeded_ability_and_item_row_and_refuse_none()
    {
        BalanceData data = TestData.Seeded;

        Assert.Empty(data.RefusedAbilities);
        foreach (uint id in new uint[] { 200, 201, 202, 210, 211, 212, 220, 221, 222, 230, 231, 232, 300, 316 })
            Assert.True(data.Abilities.TryGetValue(new AbilityId(id), out _), $"ability {id}");

        Assert.Equal(data.Tables.ItemModifiers.Count, data.ItemModifiers.Count);
        foreach (IGrouping<ulong, ItemTemplateModifier> links in data.Tables.ItemTemplateModifiers.GroupBy(l => l.TemplateId.Value))
            Assert.True(data.ItemModifiers.LinkedTo(new ItemTemplateId(links.Key)).Count == links.Count(), $"item {links.Key}");
    }

    [Fact]
    public void Know_the_seven_forest_creatures_as_hostile()
    {
        Assert.Equal(new ulong[] { 4, 5, 6, 7, 8, 9, 10 },
            TestData.Seeded.HostileTemplates.Select(t => t.Id.Value).Order().ToArray());
    }

    [Fact]
    public void Stop_when_the_ability_catalog_refuses_a_row()
    {
        SeedTables tables = SeedSource.Load();
        tables.AbilityTemplates.Single(a => a.Id.Value == 200).ScriptName = "NotAScript";

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => BalanceData.From(tables));

        Assert.Contains("ability 200 'Cleave'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_the_experience_a_level_needs() =>
        Assert.Equal(400UL, TestData.Seeded.RequiredExperience(1));
}
