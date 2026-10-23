using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Combat;
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
        BlockCap = 50f,
        HasteCap = 50f,
        MoveSpeedCap = 35f,
        MoveSpeedFloor = -50f,
        EvasionBase = 560f,
        EvasionPerLevel = 40f,
        EvasionCap = 30f,
        ResistanceCap = 75f,
        ResistanceFloor = -100f,
        ArmorPerStrength = 0.1f,
        EvasionPerAgility = 1.125f,
        WardPerIntellect = 0.25f,
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
        { "CreatureRarityModifier Boss: BlockPct must be between 0 and 100, not NaN",
            () => CreatureTemplateRules.ValidateRarities([new CreatureRarityModifier { Rarity = CreatureRarity.Boss, BlockPct = float.NaN }]) },
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
            Rarity = CreatureRarity.Boss, CritPct = 100f, BlockPct = 10f,
            StunDurationPct = 0f, RootDurationPct = 50f, SilenceDurationPct = 0f, SlowDurationPct = 100f,
        }]);
    }

    /// <summary>
    /// The defence numbers (#1178): the evasion terms must not divide by zero, the resistance cap stays under the hard
    /// ceiling of 90 and the floor between -100 and 0, a creature's resistance is a percentage either way, and its
    /// evasion and ward modifiers and the rarity's defence multiplier are finite and 0 or more.
    /// </summary>
    public static TheoryData<string, Action> DefenceRows() => new()
    {
        { "ResistanceCap", () => BuildWith(f => f.ResistanceCap = 95f) },
        { "EvasionBase", () => BuildWith(f => f.EvasionBase = f.EvasionPerLevel = 0f) },
        { "ResistanceFloor", () => BuildWith(f => f.ResistanceFloor = -150f) },
        { "FireResistance", () => CreatureTemplateRules.Validate([new CreatureTemplate { Id = 7UL, FireResistance = 150f }]) },
        { "WardModifier", () => CreatureTemplateRules.Validate([new CreatureTemplate { Id = 7UL, WardModifier = -1f }]) },
        { "DefenceMultiplier", () => CreatureTemplateRules.ValidateRarities([new CreatureRarityModifier { Rarity = CreatureRarity.Elite, DefenceMultiplier = float.NaN }]) },
    };

    [Theory]
    [MemberData(nameof(DefenceRows))]
    public void Refuse_a_defence_row_out_of_range_naming_it(string column, Action validate) =>
        Assert.Contains(column, Assert.Throws<InvalidDataException>(validate).Message, StringComparison.Ordinal);

    private static void BuildWith(Action<CombatFormula> change)
    {
        CombatFormula f = Formula();
        change(f);
        CombatDataRules.Build([f], Factors());
    }

    /// <summary>
    /// Damage types (#1178): an Ally ability names none, a class ability that deals damage names its own (as Flame
    /// Burst, 211, must), and so does a class ability that only applies a damaging aura (Ignite, 213, with Burn, 2).
    /// </summary>
    public static TheoryData<string, Func<string?>> DamageTypeMisfits() => new()
    {
        { "an Ally ability has no damage type", () => AbilityRules.Problem(new AbilityTemplate
        {
            Name = "Mending Circle", ScriptName = AbilityRules.CircleScript, Shape = AbilityShape.Circle, Radius = 4f,
            Affects = AbilityAffects.Ally, Effects = SpellEffect.Heal, DamageType = DamageType.Fire,
        }) },
        { "a class ability that deals damage names its damage type", () => AbilityRules.Problem(new AbilityTemplate
        {
            Name = "Flame Burst", ScriptName = AbilityRules.CircleScript, Shape = AbilityShape.Circle, Radius = 3f,
            Effects = SpellEffect.Damage, AllowedClasses = [CharacterClass.Wizard], DamageType = null,
        }) },
        { "a class ability that applies a damaging aura names its damage type", () => AuraRules.DamageTypeProblem(
            new AbilityTemplate
            {
                Name = "Ignite", ScriptName = AbilityRules.CircleScript, Shape = AbilityShape.Circle, Radius = 3f,
                Effects = SpellEffect.Debuff, AllowedClasses = [CharacterClass.Wizard], AuraId = new AuraId(2),
                DamageType = null,
            },
            new AuraTemplate
            {
                Id = new AuraId(2), Name = "Burn", Kind = AuraKind.Harmful, DurationMs = 9000, TickIntervalMs = 3000,
                PeriodicKind = AuraPeriodicKind.Damage, PeriodicBase = 24f, Stacking = AuraStacking.Refresh,
            }) },
    };

    [Theory]
    [MemberData(nameof(DamageTypeMisfits))]
    public void Refuse_an_ability_whose_damage_type_does_not_fit(string message, Func<string?> problem) =>
        Assert.Equal(message, problem());

    [Fact]
    public void Give_a_warrior_fury() =>
        Assert.Equal(Avalon.Network.Packets.State.PowerType.Fury, ClassPowerType.Of(CharacterClass.Warrior));
}
