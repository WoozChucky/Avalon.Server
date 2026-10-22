using Avalon.Domain.World;

namespace Avalon.Combat;

/// <summary>
/// The checks on creature templates and rarity tiers, shared by the world server's Creatures reload and the balance
/// simulator.
/// </summary>
public static class CreatureTemplateRules
{
    /// <summary>The shortest swing interval a template may have, in seconds (#627); a database check says the same.</summary>
    public const float MinBaseAttackTime = 0.5f;

    /// <summary>
    /// #627: refuses the whole area, naming the template, when one swings more often than every half second
    /// or at an interval that is not finite, so a bad row leaves the previous generation live. #770: the same for a
    /// crowd-control duration override that is neither null nor a percentage from 0 to 100. #1178: the same for an undefined
    /// damage type, a resistance outside -100 to 100, or an evasion or ward modifier that is not finite and 0 or more.
    /// </summary>
    /// <exception cref="InvalidDataException">A template is out of range; the message names it.</exception>
    public static void Validate(IEnumerable<CreatureTemplate> templates)
    {
        foreach (CreatureTemplate t in templates)
        {
            if (!float.IsFinite(t.BaseAttackTime) || t.BaseAttackTime < MinBaseAttackTime)
            {
                throw new InvalidDataException(
                    $"CreatureTemplate {t.Id.Value}: BaseAttackTime must be finite and {MinBaseAttackTime} s or more, not {t.BaseAttackTime}");
            }

            string row = $"CreatureTemplate {t.Id.Value}";
            if (t.StunDurationPct is { } stun) RequirePercentage(row, nameof(t.StunDurationPct), stun);
            if (t.RootDurationPct is { } root) RequirePercentage(row, nameof(t.RootDurationPct), root);
            if (t.SilenceDurationPct is { } silence) RequirePercentage(row, nameof(t.SilenceDurationPct), silence);
            if (t.SlowDurationPct is { } slow) RequirePercentage(row, nameof(t.SlowDurationPct), slow);

            // #1178: a resistance is a percentage either way, negative taking more; the modifiers scale a rating.
            if (!Enum.IsDefined(t.DamageType))
                throw new InvalidDataException($"{row}: DamageType {(int)t.DamageType} is not a damage type");
            RequireResistance(row, nameof(t.FireResistance), t.FireResistance);
            RequireResistance(row, nameof(t.ColdResistance), t.ColdResistance);
            RequireResistance(row, nameof(t.LightningResistance), t.LightningResistance);
            RequireResistance(row, nameof(t.BlightResistance), t.BlightResistance);
            RequireNonNegative(row, nameof(t.EvasionModifier), t.EvasionModifier);
            RequireNonNegative(row, nameof(t.WardModifier), t.WardModifier);
        }
    }

    /// <summary>
    /// #770: refuses the whole area, naming the tier, when a crowd-control duration percentage is not a finite value
    /// from 0 (immune) to 100 (the full duration). #1145: the same for the tier's crit, dodge and block chances.
    /// #1178: and for a defence multiplier that is not finite and 0 or more.
    /// </summary>
    /// <exception cref="InvalidDataException">A tier is out of range; the message names it.</exception>
    public static void ValidateRarities(IEnumerable<CreatureRarityModifier> rarities)
    {
        foreach (CreatureRarityModifier r in rarities)
        {
            string row = $"CreatureRarityModifier {r.Rarity}";
            RequirePercentage(row, nameof(r.CritPct), r.CritPct);
            RequirePercentage(row, nameof(r.DodgePct), r.DodgePct);
            RequirePercentage(row, nameof(r.BlockPct), r.BlockPct);
            RequirePercentage(row, nameof(r.StunDurationPct), r.StunDurationPct);
            RequirePercentage(row, nameof(r.RootDurationPct), r.RootDurationPct);
            RequirePercentage(row, nameof(r.SilenceDurationPct), r.SilenceDurationPct);
            RequirePercentage(row, nameof(r.SlowDurationPct), r.SlowDurationPct);
            RequireNonNegative(row, nameof(r.DefenceMultiplier), r.DefenceMultiplier);
        }
    }

    private static void RequireResistance(string row, string column, float value)
    {
        if (!float.IsFinite(value) || value < -100f || value > 100f)
        {
            throw new InvalidDataException(
                FormattableString.Invariant($"{row}: {column} must be between -100 and 100, not {value}"));
        }
    }

    private static void RequireNonNegative(string row, string column, float value)
    {
        if (!float.IsFinite(value) || value < 0f)
        {
            throw new InvalidDataException(
                FormattableString.Invariant($"{row}: {column} must be finite and 0 or more, not {value}"));
        }
    }

    private static void RequirePercentage(string row, string column, float value)
    {
        if (!float.IsFinite(value) || value < 0f || value > 100f)
        {
            throw new InvalidDataException(
                FormattableString.Invariant($"{row}: {column} must be between 0 and 100, not {value}"));
        }
    }
}
