namespace Avalon.Network.Packets.Combat;

/// <summary>
/// An increased/more key of the offence block (#1178), the sheet's IncreasedPct index: the untyped key, the five damage
/// types, the kinds (Attack, Spell), the deliveries (Area, Projectile, Melee), damage over time, healing, and the
/// physical attack damage Strength gives. Append-only.
/// </summary>
public enum OffenceStat
{
    Damage = 0,
    Physical = 1,
    Fire = 2,
    Cold = 3,
    Lightning = 4,
    Blight = 5,
    Attack = 6,
    Spell = 7,
    Area = 8,
    Projectile = 9,
    Melee = 10,
    DamageOverTime = 11,
    Healing = 12,
    PhysicalAttack = 13,
}
