using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.World.Public.Enums;

namespace Avalon.Combat.UnitTests;

/// <summary>
/// The static-data checks the world server's reloads and the balance simulator share. Each one names the
/// row it refuses, so the messages are part of the contract.
/// </summary>
public class StaticDataRulesShould
{
    private static AbilityTemplate Cone() => new()
    {
        Name = "Cleave",
        ScriptName = AbilityRules.ConeScript,
        Shape = AbilityShape.Cone,
        Reach = 4f,
        ArcDegrees = 90f,
        Effects = SpellEffect.Damage,
    };

    private static CombatFormula Formula() => new()
    {
        Id = CombatFormula.SingletonId,
        ArmorBase = 50f,
        ArmorPerLevel = 10f,
        ArmorCap = 0.75f,
        CritMultiplier = 1.5f,
        BlockMultiplier = 0.5f,
        CritCap = 50f,
        DodgeCap = 30f,
        BlockCap = 50f,
        HasteCap = 50f,
        MoveSpeedCap = 35f,
        MoveSpeedFloor = -50f,
    };

    private static ClassStatFactors[] Factors() => Enum.GetValues<CharacterClass>()
        .Select(c => new ClassStatFactors
        {
            Class = c,
            HpPerStamina = 10,
            PowerPerIntellect = 1,
            PowerPerAgility = 0,
            FixedPower = null,
            AttackPerStrength = 2,
            AttackPerAgility = 0,
            AbilityPerIntellect = 0.2,
            BaseBlock = 5f,
            BaseDodge = 3f,
            BaseCrit = 5f,
        })
        .ToArray();

    [Fact]
    public void Accept_a_valid_cone_naming_the_cone_script() =>
        Assert.Null(AbilityRules.Problem(Cone()));

    [Fact]
    public void Refuse_a_negative_scaling_coefficient()
    {
        AbilityTemplate t = Cone();
        t.ScalingCoefficient = -1f;
        Assert.NotNull(AbilityRules.Problem(t));
    }

    [Fact]
    public void Name_the_shape_scripts_by_class_name()
    {
        Assert.Equal("CircleAbilityScript", AbilityRules.CircleScript);
        Assert.Equal("ConeAbilityScript", AbilityRules.ConeScript);
        Assert.Equal("ProjectileAbilityScript", AbilityRules.ProjectileScript);
    }

    [Fact]
    public void Build_the_seeded_combat_data()
    {
        (CombatFormula? formula, IReadOnlyDictionary<CharacterClass, ClassStatFactors>? byClass) = CombatDataRules.Build([Formula()], Factors());
        Assert.Equal(CombatFormula.SingletonId, formula.Id);
        Assert.Equal(Enum.GetValues<CharacterClass>().Length, byClass.Count);
    }

    [Fact]
    public void Refuse_two_formulas() =>
        Assert.Throws<InvalidDataException>(() => CombatDataRules.Build([Formula(), Formula()], Factors()));

    [Fact]
    public void Refuse_a_crit_cap_over_100()
    {
        CombatFormula f = Formula();
        f.CritCap = 101f;
        Assert.Throws<InvalidDataException>(() => CombatDataRules.Build([f], Factors()));
    }

    [Fact]
    public void Refuse_a_creature_swinging_faster_than_the_minimum()
    {
        var t = new CreatureTemplate { Id = 7UL, BaseAttackTime = 0.4f };
        Assert.Throws<InvalidDataException>(() => CreatureTemplateRules.Validate([t]));
        Assert.Equal(0.5f, CreatureTemplateRules.MinBaseAttackTime);
    }

    [Fact]
    public void Give_a_warrior_fury() =>
        Assert.Equal(Avalon.Network.Packets.State.PowerType.Fury, ClassPowerType.Of(CharacterClass.Warrior));
}
