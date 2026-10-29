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
    /// ranges): the class row sets its mana, Strength its physical attack, the gear its armour and its weapon.
    /// </summary>
    [Theory]
    [InlineData("none", 80.5f)]     // 23 x 3.5
    [InlineData("forest", 87.5f)]   // (23 + 2) x 3.5: the Barkplate Legguards' Strength implicit, 2 at its midpoint
    public void Reproduce_a_level_one_warrior(string gear, float physicalAttack)
    {
        BalanceData data = TestData.Seeded;
        DerivedCharacterStats warrior = data.CharacterStats(CharacterClass.Warrior, 1, TestData.Gear(gear, CharacterClass.Warrior));

        Assert.Equal(180u, warrior.MaxPower);      // 100 + 20 x 4
        Assert.Equal(physicalAttack, warrior.Offence.Increased(OffenceStat.PhysicalAttack));
        if (gear == "none")
        {
            Assert.Equal(240u, warrior.MaxHealth);     // 130 + 22 x 5
            Assert.Equal(816u, warrior.Weapon.AttackTimeMs);
            return;
        }

        Assert.True(warrior.Armor > 2, $"armour {warrior.Armor}");   // none's: 23 x 0.1, truncated
        Assert.Equal(TestData.MidpointOf(7, ItemBaseStat.AttackTime), (int)warrior.Weapon.AttackTimeMs);
        Assert.Equal(TestData.MidpointOf(7, ItemBaseStat.DamageMin), (int)warrior.Weapon.Min1);
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
