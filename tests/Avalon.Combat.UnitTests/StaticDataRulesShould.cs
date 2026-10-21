using Avalon.Common;
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

    private static ClassStatFactors[] Factors() => CharacterClasses.Playable
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
        Assert.Equal(CharacterClasses.Playable.Count, byClass.Count);
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

    /// <summary>
    /// A crowd-control duration percentage scales a creature's CC; 0 is immune, 100 the full duration, and nothing else
    /// is a percentage. A creature's override may also be null (the rarity's value). A rarity's crit, dodge and block
    /// chances are percentages too (#1145).
    /// </summary>
    public static TheoryData<string, Action> RarityAndTemplatePercentages() => new()
    {
        { "CreatureTemplate 7: StunDurationPct must be between 0 and 100, not -1",
            () => CreatureTemplateRules.Validate([new CreatureTemplate { Id = 7UL, StunDurationPct = -1f }]) },
        { "CreatureTemplate 7: SlowDurationPct must be between 0 and 100, not NaN",
            () => CreatureTemplateRules.Validate([new CreatureTemplate { Id = 7UL, SlowDurationPct = float.NaN }]) },
        { "CreatureRarityModifier Boss: RootDurationPct must be between 0 and 100, not 101",
            () => CreatureTemplateRules.ValidateRarities([new CreatureRarityModifier { Rarity = CreatureRarity.Boss, RootDurationPct = 101f }]) },
        { "CreatureRarityModifier Rare: SilenceDurationPct must be between 0 and 100, not -1",
            () => CreatureTemplateRules.ValidateRarities([new CreatureRarityModifier { Rarity = CreatureRarity.Rare, SilenceDurationPct = -1f }]) },
        { "CreatureRarityModifier Elite: CritPct must be between 0 and 100, not 101",
            () => CreatureTemplateRules.ValidateRarities([new CreatureRarityModifier { Rarity = CreatureRarity.Elite, CritPct = 101f }]) },
        { "CreatureRarityModifier Boss: DodgePct must be between 0 and 100, not NaN",
            () => CreatureTemplateRules.ValidateRarities([new CreatureRarityModifier { Rarity = CreatureRarity.Boss, DodgePct = float.NaN }]) },
    };

    [Theory]
    [MemberData(nameof(RarityAndTemplatePercentages))]
    public void Refuse_a_percentage_outside_0_to_100(string reason, Action validate) =>
        Assert.Equal(reason, Assert.Throws<InvalidDataException>(validate).Message);

    [Fact]
    public void Accept_percentages_from_0_to_100_and_a_null_override()
    {
        CreatureTemplateRules.Validate([new CreatureTemplate { Id = 7UL, StunDurationPct = 0f, RootDurationPct = 100f }]);
        CreatureTemplateRules.ValidateRarities([new CreatureRarityModifier
        {
            Rarity = CreatureRarity.Boss, CritPct = 100f, DodgePct = 0f, BlockPct = 10f,
            StunDurationPct = 0f, RootDurationPct = 50f, SilenceDurationPct = 0f, SlowDurationPct = 100f,
        }]);
    }

    [Fact]
    public void Give_a_warrior_fury() =>
        Assert.Equal(Avalon.Network.Packets.State.PowerType.Fury, ClassPowerType.Of(CharacterClass.Warrior));
}
