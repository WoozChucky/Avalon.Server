using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;

namespace Avalon.Combat.UnitTests;

/// <summary>A character's offence block (#1178): its attributes' conversions, its gear's keys, then its auras.</summary>
public class CharacterOffenceShould
{
    [Fact]
    public void Turn_attributes_gear_and_auras_into_offence_by_one_rule()
    {
        var formula = new CombatFormula
        {
            IncreasedPhysicalAttackPerStrength = 3.5f,
            AttackSpeedPerAgility = 0.1f,
            IncreasedSpellPerIntellect = 3.5f,
            IncreasedHealingPerIntellect = 3.5f,
        };
        var item = new ItemTemplate
        {
            StatType1 = StatType.IncreasedFireDamage,
            StatValue1 = 12,
            StatType2 = StatType.CritMultiplier,
            StatValue2 = 25,
            StatType3 = StatType.AttackSpeed,
            StatValue3 = 10,
            StatType4 = StatType.CastSpeed,
            StatValue4 = 5,
            StatType5 = StatType.CooldownRecovery,
            StatValue5 = 8,
        };
        AuraStatModifier[] modifiers =
        [
            new() { AuraId = new AuraId(1), Stat = AuraStat.IncreasedDamage, Kind = AuraModifierKind.Flat, Value = 10f },
            new() { AuraId = new AuraId(2), Stat = AuraStat.IncreasedDamage, Kind = AuraModifierKind.More, Value = 20f },
        ];
        var auras = AuraStatTotals.Of(modifiers, static m => ((IReadOnlyList<AuraStatModifier>)[m], 1u));

        OffenceStats o = CharacterOffence.Of(30, 40, 50, [item], formula, auras);

        Assert.Equal(105f, o.Increased(OffenceStat.PhysicalAttack), precision: 4);
        Assert.Equal(175f, o.Increased(OffenceStat.Spell), precision: 4);
        Assert.Equal(175f, o.Increased(OffenceStat.Healing), precision: 4);
        Assert.Equal(12f, o.Increased(OffenceStat.Fire));
        Assert.Equal(10f, o.Increased(OffenceStat.Damage));
        Assert.Equal(1.2d, o.More(OffenceStat.Damage), precision: 6);
        Assert.Equal(25f, o.CritMultiplierBonus);
        Assert.Equal(14f, o.AttackSpeedPct, precision: 4);
        Assert.Equal(5f, o.CastSpeedPct);
        Assert.Equal(8f, o.CooldownRecoveryPct);
    }
}
