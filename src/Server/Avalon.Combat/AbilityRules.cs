using System.Numerics;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;

namespace Avalon.Combat;

/// <summary>
/// The checks an ability row must pass to load (#164), shared by the world server's catalog and the balance
/// simulator. Pure: they read the row and nothing else.
/// </summary>
public static class AbilityRules
{
    /// <summary>The class name of the script that resolves a circle (the world server guards this against the type).</summary>
    public const string CircleScript = "CircleAbilityScript";

    /// <summary>The class name of the script that resolves a cone.</summary>
    public const string ConeScript = "ConeAbilityScript";

    /// <summary>The class name of the script that resolves a projectile.</summary>
    public const string ProjectileScript = "ProjectileAbilityScript";

    /// <summary>Why a row cannot load, or null when it can.</summary>
    public static string? Problem(AbilityTemplate t)
    {
        foreach ((string name, float value) in new[]
                 {
                     ("Reach", t.Reach), ("Radius", t.Radius), ("ArcDegrees", t.ArcDegrees),
                     ("ProjectileSpeed", t.ProjectileSpeed),
                     // #529. A NaN or infinite threat value would spread into every threat total it
                     // touches, and a hostile's threat list would stop ordering anything.
                     ("ThreatMultiplier", t.ThreatMultiplier), ("HealThreatPerHp", t.HealThreatPerHp),
                     // #506. A negative or NaN coefficient would turn a hit into a heal or a NaN damage.
                     ("ScalingCoefficient", t.ScalingCoefficient), ("BaseDamageCoefficient", t.BaseDamageCoefficient),
                     // #1178: a negative effectiveness or growth would turn a hit into a heal.
                     ("DamageEffectiveness", t.DamageEffectiveness), ("SpellBasePerLevelPct", t.SpellBasePerLevelPct),
                 })
        {
            if (!float.IsFinite(value) || value < 0f)
                return $"{name} {value} is not a finite value of 0 or more";
        }

        // #526. A negative gain would drain the caster's pool on every hit; the database refuses it too.
        if (t.PowerGainPerHit < 0) return $"PowerGainPerHit {t.PowerGainPerHit} is below 0";

        // #652: a cost is spent from the pool the row names, never from whichever pool the caster has.
        if (!Enum.IsDefined(t.CostPowerType)) return $"unknown cost power type {(int)t.CostPowerType}";
        if (t.Cost > 0 && t.CostPowerType == PowerType.None) return $"Cost {t.Cost} names no power type to spend it from";

        if (!Enum.IsDefined(t.ScalingStat)) return $"unknown scaling stat {(byte)t.ScalingStat}";
        if (!Enum.IsDefined(t.AimMode)) return $"unknown aim mode {(byte)t.AimMode}";
        if (!Enum.IsDefined(t.Shape)) return $"unknown shape {(byte)t.Shape}";
        if (!Enum.IsDefined(t.Anchor)) return $"unknown anchor {(byte)t.Anchor}";
        if (!Enum.IsDefined(t.Affects)) return $"unknown affects {(byte)t.Affects}";

        if (t.Affects == AbilityAffects.Ally && t.Shape != AbilityShape.Circle)
            return "only a circle may affect allies";

        // #1164: an Ally ability is never dodged, so the flag would claim what it already is.
        if (t.Affects == AbilityAffects.Ally && (t.Flags & AbilityFlags.Unavoidable) != AbilityFlags.None)
            return "only a Hostile ability may be Unavoidable: an Ally ability is never dodged";

        // #1178: an Ally ability heals or helps and deals nothing. A class ability that deals damage names its type; a
        // creature-only one may leave it null and deal its creature's base type.
        if (t.DamageType is { } type && !Enum.IsDefined(type)) return $"unknown damage type {(int)type}";
        if (t.Affects == AbilityAffects.Ally && t.DamageType is not null) return "an Ally ability has no damage type";
        if (t.Affects == AbilityAffects.Hostile && t.AllowedClasses is { Count: > 0 }
            && (t.Effects & SpellEffect.Damage) != SpellEffect.None && t.DamageType is null)
        {
            return "a class ability that deals damage names its damage type";
        }

        if (OffenceProblem(t) is { } offence) return offence;

        // With neither a direct amount nor an aura the ability would spend its cost and change nothing.
        if (!HasDirectEffect(t.Effects, t.Affects) && t.AuraId is null)
        {
            return t.Affects == AbilityAffects.Ally
                ? "the ability does nothing: Effects has no Heal for an Ally ability, and it applies no aura"
                : "the ability does nothing: Effects has no Damage for a Hostile ability, and it applies no aura";
        }

        return ShapeProblem(t);
    }

    /// <summary>Every tag <see cref="AbilityTags" /> names.</summary>
    private const AbilityTags KnownTags = AbilityTags.Attack | AbilityTags.Spell | AbilityTags.Heal | AbilityTags.Area
                                          | AbilityTags.Projectile | AbilityTags.Melee | AbilityTags.DamageOverTime;

    /// <summary>What an ability is: an Attack, a Spell or a Heal.</summary>
    private const AbilityTags Kinds = AbilityTags.Attack | AbilityTags.Spell | AbilityTags.Heal;

    /// <summary>
    /// The offence model's checks (#1178), or null when they pass: the tags fit what the row affects, deals and is
    /// shaped as; a class attack deals its weapon's physical damage, an element arriving only by its conversion or added
    /// damage; and the amounts a hit rolls are in order.
    /// </summary>
    private static string? OffenceProblem(AbilityTemplate t)
    {
        AbilityTags tags = t.Tags;
        if ((tags & ~KnownTags) != AbilityTags.None) return $"Tags {(int)tags} names an unknown tag";
        if (BitOperations.PopCount((uint)(tags & Kinds)) > 1) return "an ability is at most one of Attack, Spell and Heal";
        if (Has(tags, AbilityTags.DamageOverTime)) return "DamageOverTime is an aura's tag, never an ability's";

        bool ally = t.Affects == AbilityAffects.Ally;
        if (ally ? Has(tags, AbilityTags.Attack | AbilityTags.Spell) : Has(tags, AbilityTags.Heal))
            return "only a Hostile ability is an Attack or a Spell, and only an Ally ability a Heal";

        bool direct = HasDirectEffect(t.Effects, t.Affects);
        if (direct && !ally && !Has(tags, AbilityTags.Attack | AbilityTags.Spell))
            return "a Hostile ability that deals damage is an Attack or a Spell";
        if (direct && ally && !Has(tags, AbilityTags.Heal)) return "an Ally ability that heals is a Heal";

        if (Has(tags, AbilityTags.Projectile) != (t.Shape == AbilityShape.Projectile))
            return "Projectile is the tag of a projectile, and only of one";
        if (Has(tags, AbilityTags.Melee) && !Has(tags, AbilityTags.Attack)) return "only an Attack is Melee";

        bool attack = Has(tags, AbilityTags.Attack);
        if (attack && (t.Flags & AbilityFlags.Basic) != AbilityFlags.None && t.Cooldown > 0)
            return "a basic attack is paced by its weapon: its Cooldown is 0";
        if (attack && t.AllowedClasses is { Count: > 0 } && t.DamageType is { } type && type != DamageType.Physical)
            return "a class attack deals its weapon's physical damage: an element comes from its conversion or added damage";

        if (!(t.ConvertPct >= 0f && t.ConvertPct <= 100f))
            return FormattableString.Invariant($"ConvertPct {t.ConvertPct} is not a percentage from 0 to 100");
        if (t.ConvertPct > 0f && t.ConvertToType is not (DamageType.Fire or DamageType.Cold or DamageType.Lightning or DamageType.Blight))
            return "a conversion names an element to convert to";
        if (t.ConvertPct > 0f && !attack) return "only an Attack converts its weapon's physical damage";

        if (t.AddedMin > t.AddedMax) return $"AddedMin {t.AddedMin} is above AddedMax {t.AddedMax}";
        if (t.AddedType is { } added && !Enum.IsDefined(added)) return $"unknown damage type {(int)added}";
        if (t.AddedMax > 0 && t.AddedType is null) return "added damage names its type";

        if (t.SpellBaseMin > t.SpellBaseMax) return $"SpellBaseMin {t.SpellBaseMin} is above SpellBaseMax {t.SpellBaseMax}";
        if (direct && Has(tags, AbilityTags.Spell | AbilityTags.Heal) && t.SpellBaseMax == 0) return NoSpellBase;
        if (direct && attack && !(t.DamageEffectiveness > 0f))
            return "an Attack that deals damage needs a DamageEffectiveness above 0";

        return null;
    }

    /// <summary>The refusal of a Spell or a Heal with nothing to roll, shared with <see cref="AuraRules.SourceProblem" />.</summary>
    internal const string NoSpellBase = "a Spell or a Heal that deals or heals needs a SpellBaseMax above 0";

    /// <summary>Whether <paramref name="tags" /> has any of <paramref name="any" />.</summary>
    internal static bool Has(AbilityTags tags, AbilityTags any) => (tags & any) != AbilityTags.None;

    /// <summary>The checks of the row's own shape, or null when they pass.</summary>
    private static string? ShapeProblem(AbilityTemplate t)
    {
        switch (t.Shape)
        {
            case AbilityShape.Circle:
                if (ScriptMismatch(t, CircleScript, "a circle") is { } circleScript) return circleScript;
                if (t.Radius <= 0f) return "a circle needs a Radius above 0";
                if (t.Anchor == AbilityAnchor.AimPoint && t.AimMode != AbilityAimMode.Cursor)
                    return "a circle on the aim point must aim with the cursor";
                if (t.Anchor == AbilityAnchor.AimPoint && t.Reach <= 0f)
                    return "a circle on the aim point needs a Reach above 0";
                if (t.Anchor == AbilityAnchor.Caster && t.Reach > 0f)
                    return "a circle on the caster must have Reach 0";
                return null;

            case AbilityShape.Cone:
                if (ScriptMismatch(t, ConeScript, "a cone") is { } coneScript) return coneScript;
                if (t.Reach <= 0f) return "a cone needs a Reach above 0";
                if (t.ArcDegrees <= 0f || t.ArcDegrees > 360f) return "a cone needs ArcDegrees above 0 and at most 360";
                return null;

            case AbilityShape.Projectile:
                if (ScriptMismatch(t, ProjectileScript, "a projectile") is { } projectileScript)
                    return projectileScript;
                if (t.Reach <= 0f) return "a projectile needs a Reach above 0";
                if (t.ProjectileSpeed <= 0f) return "a projectile needs a ProjectileSpeed above 0";
                if (t.AimMode != AbilityAimMode.Cursor) return "a projectile must aim with the cursor";
                return null;

            default:
                return $"unknown shape {(byte)t.Shape}";
        }
    }

    /// <summary>
    /// Whether the ability deals or heals its own amount: a Hostile ability when <paramref name="effects" /> has Damage,
    /// an Ally one when it has Heal. Without it the ability only applies its aura.
    /// </summary>
    public static bool HasDirectEffect(SpellEffect effects, AbilityAffects affects) =>
        (effects & (affects == AbilityAffects.Ally ? SpellEffect.Heal : SpellEffect.Damage)) != SpellEffect.None;

    /// <summary>
    /// Each shape has exactly one script that resolves it, so a row naming another would fire a shape
    /// the row's other columns were never checked for.
    /// </summary>
    private static string? ScriptMismatch(AbilityTemplate t, string expected, string shape) =>
        string.Equals(t.ScriptName, expected, StringComparison.Ordinal)
            ? null
            : $"{shape} must use {expected}, not '{t.ScriptName}'";
}
