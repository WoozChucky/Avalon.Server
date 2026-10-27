using Avalon.Domain.World;

namespace Avalon.Combat;

/// <summary>
/// The checks on a talent node's stat line (#1181), shared by the world's talent catalog and the tools that read the
/// seed. A line speaks the gear's <see cref="StatType" /> vocabulary, but only its global half: a stat local to the item
/// it sits on (35-44) has no item to scale on a node.
/// <list type="bullet">
/// <item><see cref="TalentStatKind.Added" /> sums with the gear's lines of the same stat: the attributes, the flat
/// defences, life and mana, block, crit, the speeds, the resistances, every increase and every global added damage.</item>
/// <item><see cref="TalentStatKind.More" /> is a multiplier of its own, allowed only on the thirteen offence increases
/// and on increased maximum life and mana.</item>
/// <item>A high end (<c>ValueMax</c>) belongs only on an "adds X to Y" line, and is at least its low end.</item>
/// </list>
/// </summary>
public static class TalentStatRules
{
    /// <summary>Why the line cannot load, or null when it can.</summary>
    public static string? Problem(StatType stat, TalentStatKind kind, int value, int? valueMax)
    {
        if (kind is not (TalentStatKind.Added or TalentStatKind.More))
            return $"unknown stat kind {(int)kind}";

        if (stat is >= StatType.AddedPhysicalDamage and <= StatType.LocalAttackSpeed)
            return $"{stat} is local to the item it sits on, and a talent node has none";

        if (kind == TalentStatKind.More ? !AllowsMore(stat) : !AllowsAdded(stat))
            return $"{stat} cannot be {kind} on a talent node";

        if (valueMax is { } max)
        {
            if (!IsAddedDamage(stat))
                return $"{stat} cannot have a range: only an added damage line has a ValueMax";
            if (max < value)
                return $"{stat} has a ValueMax {max} below its Value {value}";
        }

        return null;
    }

    private static bool AllowsAdded(StatType stat) => stat switch
    {
        StatType.Stamina or StatType.Strength or StatType.Agility or StatType.Intellect => true,
        StatType.Armor or StatType.Evasion or StatType.Ward or StatType.Health or StatType.Power => true,
        StatType.BlockPct or StatType.CritPct or StatType.CritMultiplier => true,
        StatType.AttackSpeed or StatType.CastSpeed or StatType.CooldownRecovery or StatType.MovementSpeed => true,
        StatType.FireResistance or StatType.ColdResistance or StatType.LightningResistance or StatType.BlightResistance => true,
        StatType.IncreasedMaximumLife or StatType.IncreasedMaximumMana or StatType.IncreasedManaRegeneration => true,
        StatType.IncreasedArmour or StatType.IncreasedEvasion or StatType.IncreasedWard => true,
        _ => IsOffenceIncrease(stat) || IsAddedDamage(stat),
    };

    private static bool AllowsMore(StatType stat) =>
        IsOffenceIncrease(stat) || stat is StatType.IncreasedMaximumLife or StatType.IncreasedMaximumMana;

    /// <summary>The thirteen increases of damage or healing, <see cref="StatType.IncreasedDamage" /> to <see cref="StatType.IncreasedHealing" />.</summary>
    private static bool IsOffenceIncrease(StatType stat) =>
        stat is >= StatType.IncreasedDamage and <= StatType.IncreasedHealing;

    /// <summary>The global "adds X to Y" lines: to every attack by type, then to every spell by element.</summary>
    private static bool IsAddedDamage(StatType stat) =>
        stat is >= StatType.AddedPhysicalDamageToAttacks and <= StatType.AddedLightningDamageToSpells;
}
