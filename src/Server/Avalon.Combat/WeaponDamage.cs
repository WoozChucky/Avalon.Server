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
    /// A character's main hand: its ranges (a low end above its high end counting as the high end) and its attack time,
    /// with the lines local to it folded in (#1179): its added damage by type goes to <see cref="Added" />; its local
    /// increased physical damage scales each end of a physical range and of the added physical damage by
    /// <c>max(0, 1 + sum / 100)</c>; its local attack speed makes the attack time <c>time / (1 + sum / 100)</c>, a sum of
    /// 0 or below counting as none, as every speed does (<see cref="Speed" />). Each result is rounded half away from
    /// zero. No main hand, or one whose first range deals nothing, rolls the formula's unarmed range as physical damage; a
    /// main hand with no attack time paces as unarmed.
    /// </summary>
    public static WeaponDamage Of(WornItem? mainHand, CombatFormula f)
    {
        uint unarmedTime = f.UnarmedAttackTimeMs;
        if (mainHand is not { } item || (item.Template.DamageMax1 ?? 0) == 0)
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

        ItemTemplate t = item.Template;
        double physical = Math.Max(0d, 1d + (increasedPhysical / 100d));
        added.Scale(DamageType.Physical, physical);

        uint max1 = t.DamageMax1 ?? 0;
        uint max2 = t.DamageMax2 ?? 0;
        double factor1 = t.DamageType1 == DamageType.Physical ? physical : 1d;
        double factor2 = t.DamageType2 == DamageType.Physical ? physical : 1d;
        uint time = t.AttackTimeMs is { } attackTime and > 0 ? attackTime : unarmedTime;
        if (attackSpeed > 0) time = Math.Max(1u, DamageRanges.Round(time / (1d + (attackSpeed / 100d))));

        return new WeaponDamage(
            DamageRanges.Round(Math.Min(t.DamageMin1 ?? 0, max1) * factor1), DamageRanges.Round(max1 * factor1), t.DamageType1,
            DamageRanges.Round(Math.Min(t.DamageMin2 ?? 0, max2) * factor2), DamageRanges.Round(max2 * factor2), t.DamageType2,
            time, added);
    }
}
