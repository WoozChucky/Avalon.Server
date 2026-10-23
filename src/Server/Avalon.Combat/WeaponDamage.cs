using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;

namespace Avalon.Combat;

/// <summary>
/// What an attack rolls (#1178): a main hand's two damage ranges, each inclusive and typed by the item (null: the hit's
/// type), and its time between basic attacks before attack speed. A creature's is its natural range. Default rolls
/// nothing.
/// </summary>
public readonly record struct WeaponDamage(uint Min1, uint Max1, DamageType? Type1, uint Min2, uint Max2, DamageType? Type2,
    uint AttackTimeMs)
{
    /// <summary>
    /// A character's main hand: its ranges (a low end above its high end counting as the high end) and its attack time.
    /// No main hand, or one whose first range deals nothing, rolls the formula's unarmed range as physical damage; a main
    /// hand with no attack time paces as unarmed.
    /// </summary>
    public static WeaponDamage Of(ItemTemplate? mainHand, CombatFormula f)
    {
        uint unarmedTime = f.UnarmedAttackTimeMs;
        uint max1 = mainHand?.DamageMax1 ?? 0;
        if (mainHand is null || max1 == 0)
        {
            return new WeaponDamage(Math.Min(f.UnarmedDamageMin, f.UnarmedDamageMax), f.UnarmedDamageMax,
                DamageType.Physical, 0, 0, null, unarmedTime);
        }

        uint max2 = mainHand.DamageMax2 ?? 0;
        uint time = mainHand.AttackTimeMs is { } attackTime and > 0 ? attackTime : unarmedTime;
        return new WeaponDamage(Math.Min(mainHand.DamageMin1 ?? 0, max1), max1, mainHand.DamageType1,
            Math.Min(mainHand.DamageMin2 ?? 0, max2), max2, mainHand.DamageType2, time);
    }
}
