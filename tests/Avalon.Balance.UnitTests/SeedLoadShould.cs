using Avalon.Balance.Data;
using Avalon.Combat;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Combat;
using Avalon.World.Public.Enums;
using Xunit;

namespace Avalon.Balance.UnitTests;

public class SeedLoadShould
{
    [Fact]
    public void Reproduce_a_level_one_warrior_with_no_gear()
    {
        DerivedCharacterStats warrior = TestData.Seeded.CharacterStats(CharacterClass.Warrior, 1, []);

        Assert.Equal(240u, warrior.MaxHealth);     // 20 + 22 x 10
        Assert.Equal(46u, warrior.AttackDamage);   // 23 x 2
        Assert.Equal(100u, warrior.MaxPower);      // Fury is fixed at 100
    }

    [Fact]
    public void Give_cleave_a_base_of_25_point_8_for_that_warrior()
    {
        BalanceData data = TestData.Seeded;
        AbilityTemplate cleave = data.KitOf(CharacterClass.Warrior).Single(a => a.Id.Value == 200);
        AttackerCombat attacker = data.CharacterStats(CharacterClass.Warrior, 1, []).AttackerAt(1);

        (float min, float max) = HitResolver.AbilityBaseRange(attacker, cleave.EffectValue, cleave.ScalingStat,
            cleave.ScalingCoefficient, cleave.BaseDamageCoefficient);

        Assert.Equal(25.8f, min, precision: 4);
        Assert.Equal(25.8f, max, precision: 4);
    }

    [Fact]
    public void Load_every_seeded_ability_and_refuse_none()
    {
        BalanceData data = TestData.Seeded;

        Assert.Empty(data.Abilities.Refused);
        foreach (uint id in new uint[] { 200, 201, 202, 210, 211, 212, 220, 221, 222, 230, 231, 232, 300, 316 })
            Assert.True(data.Abilities.TryGet(new AbilityId(id), out _), $"ability {id}");
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
        SeedTables tables = SeedTables.Read();
        tables.AbilityTemplates.Single(a => a.Id.Value == 200).ScriptName = "NotAScript";

        var error = Assert.Throws<InvalidDataException>(() => BalanceData.From(tables));

        Assert.Contains("ability 200 'Cleave'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_the_experience_a_level_needs() =>
        Assert.Equal(400UL, TestData.Seeded.RequiredExperience(1));
}
