using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;

namespace Avalon.Combat;

/// <summary>
/// What an attack rolls (#1178): a main hand's two damage ranges, each inclusive and typed by the item (null: the hit's
/// type), its time between basic attacks before attack speed, and its own added damage by type (#1179), rolled after
/// the two ranges. A creature's is its natural range. Default rolls nothing.
/// </summary>
public readonly record struct WeaponDamage(uint Min1, uint Max1, DamageType? Type1, uint Min2, uint Max2, DamageType? Type2,
    uint AttackTimeMs, DamageRanges Added = default)
{
    /// <summary>
    /// A character's main hand: the damage range and attack time its copy rolled (#1179, its base stats; a low end above
    /// its high end counting as the high end), typed by its template, with the lines local to it folded in: its added
    /// damage by type goes to <see cref="Added" />; its local increased physical damage scales each end of a physical
    /// range and of the added physical damage by <c>max(0, 1 + sum / 100)</c>; its local attack speed makes the attack time
    /// <c>time / (1 + sum / 100)</c>, a sum of 0 or below counting as none, as every speed does (<see cref="Speed" />).
    /// Each result is worked out in whole numbers and rounded exactly half away from zero (<see cref="LocalScaling" />). An
    /// item's second range is empty and untyped. No main hand, or one whose copy rolled no damage above 0, rolls the
    /// formula's unarmed range as physical damage; a main hand with no attack time paces as unarmed.
    /// </summary>
    public static WeaponDamage Of(WornItem? mainHand, CombatFormula f)
    {
        uint unarmedTime = f.UnarmedAttackTimeMs;
        if (mainHand is not { } item || item.BaseStats.DamageMax is not > 0)
        {
            return new WeaponDamage(Math.Min(f.UnarmedDamageMin, f.UnarmedDamageMax), f.UnarmedDamageMax,
                DamageType.Physical, 0, 0, null, unarmedTime);
        }

        DamageRanges added = default;
        long increasedPhysical = 0;
        long attackSpeed = 0;
        foreach (ItemLine line in ItemLines.Of(item))
        {
            switch (line.Stat)
            {
                case >= StatType.AddedPhysicalDamage and <= StatType.AddedBlightDamage:
                    added.Add((DamageType)(line.Stat - StatType.AddedPhysicalDamage), line);
                    break;
                case StatType.LocalIncreasedPhysicalDamage: increasedPhysical += line.Min; break;
                case StatType.LocalAttackSpeed: attackSpeed += line.Min; break;
                default: break;
            }
        }

        added.Increase(DamageType.Physical, increasedPhysical);

        DamageType? type = item.Template.DamageType;
        uint max = (uint)item.BaseStats.DamageMax.GetValueOrDefault();
        uint min = Math.Min((uint)Math.Max(0, item.BaseStats.DamageMin.GetValueOrDefault()), max);
        long increased = type == DamageType.Physical ? increasedPhysical : 0;
        uint time = item.BaseStats.AttackTime is { } attackTime and > 0 ? (uint)attackTime : unarmedTime;

        return new WeaponDamage(LocalScaling.Increase(min, increased), LocalScaling.Increase(max, increased), type,
            0, 0, null, LocalScaling.Hasten(time, attackSpeed), added);
    }
}
