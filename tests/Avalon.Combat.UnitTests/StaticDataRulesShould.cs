using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Combat;
using Avalon.World.Public.Abilities;
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
        Tags = AbilityTags.Attack | AbilityTags.Area,
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
        IncreasedPhysicalAttackPerStrength = 3.5f,
        AttackSpeedPerAgility = 0.1f,
        IncreasedSpellPerIntellect = 3.5f,
        IncreasedHealingPerIntellect = 3.5f,
        LifePerStamina = 5,
        BaseCritPct = 5f,
        AttackSpeedCap = 50f,
        CastSpeedCap = 50f,
        CooldownRecoveryCap = 50f,
        UnarmedDamageMin = 3,
        UnarmedDamageMax = 6,
        UnarmedAttackTimeMs = 816,
    };

    [Fact]
    public void Accept_a_valid_cone_naming_the_cone_script() =>
        Assert.Null(AbilityRules.Problem(Cone(), granted: false));

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
        CombatFormula formula = CombatDataRules.Build([Formula()]);
        Assert.Equal(CombatFormula.SingletonId, formula.Id);
    }

    [Fact]
    public void Refuse_two_formulas() =>
        Assert.Throws<InvalidDataException>(() => CombatDataRules.Build([Formula(), Formula()]));

    [Fact]
    public void Refuse_a_crit_cap_over_100()
    {
        CombatFormula f = Formula();
        f.CritCap = 101f;
        Assert.Throws<InvalidDataException>(() => CombatDataRules.Build([f]));
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
        CombatDataRules.Build([f]);
    }

    /// <summary>
    /// Damage types (#1178): an Ally ability names none, a class ability that deals damage names its own (as Flame
    /// Burst, 211, must), and so does a class ability that only applies a damaging aura (Ignite, 213, with Burn, 2). A
    /// class ability is one the talent tree grants (#1181): the same row the tree does not grant is a creature's, which
    /// may leave its type null, and loads.
    /// </summary>
    public static TheoryData<string?, Func<string?>> DamageTypeMisfits() => new()
    {
        { "an Ally ability has no damage type", () => AbilityRules.Problem(new AbilityTemplate
        {
            Name = "Mending Circle", ScriptName = AbilityRules.CircleScript, Shape = AbilityShape.Circle, Radius = 4f,
            Affects = AbilityAffects.Ally, Effects = SpellEffect.Heal, DamageType = DamageType.Fire,
        }, granted: true) },
        { "a class ability that deals damage names its damage type", () => AbilityRules.Problem(UntypedFlameBurst(), granted: true) },
        { null, () => AbilityRules.Problem(UntypedFlameBurst(), granted: false) },
        { "a class ability that applies a damaging aura names its damage type",
            () => AuraRules.DamageTypeProblem(UntypedIgnite(), Burn(), granted: true) },
        { null, () => AuraRules.DamageTypeProblem(UntypedIgnite(), Burn(), granted: false) },
    };

    [Theory]
    [MemberData(nameof(DamageTypeMisfits))]
    public void Refuse_an_ability_whose_damage_type_does_not_fit(string? message, Func<string?> problem) =>
        Assert.Equal(message, problem());

    private static AbilityTemplate UntypedFlameBurst() => new()
    {
        Name = "Flame Burst",
        ScriptName = AbilityRules.CircleScript,
        Shape = AbilityShape.Circle,
        Radius = 3f,
        Effects = SpellEffect.Damage,
        Tags = AbilityTags.Spell | AbilityTags.Area,
        SpellBaseMin = 21,
        SpellBaseMax = 32,
        DamageType = null,
    };

    private static AbilityTemplate UntypedIgnite() => new()
    {
        Name = "Ignite",
        ScriptName = AbilityRules.CircleScript,
        Shape = AbilityShape.Circle,
        Radius = 3f,
        Effects = SpellEffect.Debuff,
        AuraId = new AuraId(2),
        DamageType = null,
    };

    /// <summary>The seeded aura 2, Burn: a harmful damage over time.</summary>
    private static AuraTemplate Burn() => new()
    {
        Id = new AuraId(2),
        Name = "Burn",
        Kind = AuraKind.Harmful,
        DurationMs = 9000,
        TickIntervalMs = 3000,
        PeriodicKind = AuraPeriodicKind.Damage,
        PeriodicBase = 24f,
        Stacking = AuraStacking.Refresh,
    };

    /// <summary>
    /// The offence model (#1178): each row is a seeded ability (or the formula) with one column changed, refused by the
    /// rule that change breaks. The seeded rows themselves load (<c>SeedIntegrityShould</c>). A row whose rule holds only
    /// a class ability, one the talent tree grants (#1181), is followed by the same row the tree does not grant, which
    /// loads.
    /// </summary>
    public static TheoryData<string?, Func<string?>> OffenceMisfits() => new()
    {
        { "an ability is at most one of Attack, Spell and Heal",
            () => AbilityRules.Problem(Seeded(210, t => t.Tags = AbilityTags.Attack | AbilityTags.Spell | AbilityTags.Projectile), granted: true) },
        { "Projectile is the tag of a projectile, and only of one",
            () => AbilityRules.Problem(Seeded(222, t => t.Tags = AbilityTags.Attack | AbilityTags.Projectile), granted: true) },
        { "an Ally ability that heals is a Heal", () => AbilityRules.Problem(Seeded(232, t => t.Tags = AbilityTags.Area), granted: true) },
        { "DamageOverTime is an aura's tag, never an ability's",
            () => AbilityRules.Problem(Seeded(211, t => t.Tags = AbilityTags.Spell | AbilityTags.Area | AbilityTags.DamageOverTime), granted: true) },
        { "a class attack deals its weapon's physical damage: an element comes from its conversion or added damage",
            () => AbilityRules.Problem(Seeded(220, t => t.DamageType = DamageType.Cold), granted: true) },
        { null, () => AbilityRules.Problem(Seeded(220, t => t.DamageType = DamageType.Cold), granted: false) },
        { "a basic attack is paced by its weapon: its Cooldown is 0",
            () => AbilityRules.Problem(Seeded(220, t => (t.Flags, t.Cooldown) = (AbilityFlags.Basic, 800u)), granted: true) },
        { "ConvertPct 150 is not a percentage from 0 to 100", () => AbilityRules.Problem(Seeded(223, t => t.ConvertPct = 150f), granted: true) },
        { "a conversion names an element to convert to",
            () => AbilityRules.Problem(Seeded(223, t => (t.ConvertPct, t.ConvertToType) = (0f, DamageType.Physical)), granted: true) },
        { "a Spell or a Heal that deals or heals needs a SpellBaseMax above 0",
            () => AbilityRules.Problem(Seeded(210, t => t.SpellBaseMin = t.SpellBaseMax = 0), granted: true) },
        { "added damage names its type", () => AbilityRules.Problem(Seeded(224, t => t.AddedType = null), granted: true) },
        { "an Ally ability that applies a healing aura is a Heal",
            () => AuraRules.SourceProblem(Seeded(233, t => t.Tags = AbilityTags.Area), Renew(), granted: true) },
        { "a class ability that applies a damaging aura is an Attack or a Spell",
            () => AuraRules.SourceProblem(Seeded(211, t => t.Tags = AbilityTags.Area), Burn(), granted: true) },
        { null, () => AuraRules.SourceProblem(Seeded(211, t => t.Tags = AbilityTags.Area), Burn(), granted: false) },
        { "CombatFormula 1: UnarmedDamageMin 7 is above UnarmedDamageMax 6", () => Refusal(f => f.UnarmedDamageMin = 7) },
    };

    [Theory]
    [MemberData(nameof(OffenceMisfits))]
    public void Refuse_an_offence_row_that_does_not_fit_naming_why(string? message, Func<string?> problem) =>
        Assert.Equal(message, problem());

    /// <summary>
    /// The seeded ability <paramref name="id" /> as the offence model authors it, with <paramref name="change" /> made: a
    /// copy of the World seed's columns the rules read, not the seed itself.
    /// </summary>
    private static AbilityTemplate Seeded(uint id, Action<AbilityTemplate> change)
    {
        AbilityTemplate t = id switch
        {
            210 => Projectile(DamageType.Lightning, AbilityTags.Spell | AbilityTags.Projectile),
            220 or 223 or 224 => Projectile(DamageType.Physical, AbilityTags.Attack | AbilityTags.Projectile),
            211 => Circle(AbilityAffects.Hostile, SpellEffect.Damage, AbilityTags.Spell | AbilityTags.Area),
            222 => new AbilityTemplate
            {
                ScriptName = AbilityRules.ConeScript,
                Shape = AbilityShape.Cone,
                AimMode = AbilityAimMode.Cursor,
                Reach = 8f,
                ArcDegrees = 45f,
                Effects = SpellEffect.Damage,
                DamageType = DamageType.Physical,
                Tags = AbilityTags.Attack | AbilityTags.Area,
            },
            232 => Circle(AbilityAffects.Ally, SpellEffect.Heal, AbilityTags.Heal | AbilityTags.Area),
            233 => Circle(AbilityAffects.Ally, SpellEffect.Buff, AbilityTags.Heal | AbilityTags.Area),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "not a row these cases use"),
        };

        t.Id = id;
        (t.DamageEffectiveness, t.SpellBaseMin, t.SpellBaseMax, t.SpellBasePerLevelPct) = id switch
        {
            210 => (100f, 14u, 20u, 0.70f),
            211 => (100f, 21u, 32u, 0.75f),
            220 => (363f, 0u, 0u, 0f),
            222 => (516f, 0u, 0u, 0f),
            223 => (495f, 0u, 0u, 0f),
            224 => (168f, 0u, 0u, 0f),
            232 => (100f, 30u, 44u, 0f),
            233 => (100f, 19u, 28u, 0f),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "not a row these cases use"),
        };
        if (id == 223) (t.ConvertToType, t.ConvertPct) = (DamageType.Cold, 50f);
        if (id == 224) (t.AddedType, t.AddedMin, t.AddedMax) = (DamageType.Blight, 2u, 4u);
        if (id == 233) t.AuraId = new AuraId(4);

        Assert.Null(AbilityRules.Problem(t, granted: true));
        change(t);
        return t;

        static AbilityTemplate Projectile(DamageType type, AbilityTags tags) => new()
        {
            ScriptName = AbilityRules.ProjectileScript,
            Shape = AbilityShape.Projectile,
            AimMode = AbilityAimMode.Cursor,
            Reach = 25f,
            ProjectileSpeed = 14f,
            Effects = SpellEffect.Damage,
            DamageType = type,
            Tags = tags,
        };

        static AbilityTemplate Circle(AbilityAffects affects, SpellEffect effects, AbilityTags tags) => new()
        {
            ScriptName = AbilityRules.CircleScript,
            Shape = AbilityShape.Circle,
            AimMode = AbilityAimMode.Cursor,
            Anchor = AbilityAnchor.AimPoint,
            Reach = 18f,
            Radius = 3f,
            Effects = effects,
            Affects = affects,
            DamageType = affects == AbilityAffects.Hostile ? DamageType.Fire : null,
            Tags = tags,
        };
    }

    /// <summary>The seeded aura 4, Renew: a helpful heal over time that draws its whole source base.</summary>
    private static AuraTemplate Renew() => new()
    {
        Id = new AuraId(4),
        Name = "Renew",
        Kind = AuraKind.Helpful,
        DurationMs = 12000,
        TickIntervalMs = 3000,
        PeriodicKind = AuraPeriodicKind.Heal,
        PeriodicBase = 24f,
        PeriodicEffectiveness = 100f,
        Stacking = AuraStacking.Refresh,
    };

    /// <summary>Why the seeded formula, with <paramref name="change" /> made, is refused, or null when it builds.</summary>
    private static string? Refusal(Action<CombatFormula> change)
    {
        CombatFormula f = Formula();
        change(f);
        try
        {
            CombatDataRules.Build([f]);
            return null;
        }
        catch (InvalidDataException ex)
        {
            return ex.Message;
        }
    }
}
