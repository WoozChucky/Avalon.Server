using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.State;
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

        // With neither a direct amount nor an aura the ability would spend its cost and change nothing.
        if (!HasDirectEffect(t.Effects, t.Affects) && t.AuraId is null)
        {
            return t.Affects == AbilityAffects.Ally
                ? "the ability does nothing: Effects has no Heal for an Ally ability, and it applies no aura"
                : "the ability does nothing: Effects has no Damage for a Hostile ability, and it applies no aura";
        }

        return ShapeProblem(t);
    }

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
