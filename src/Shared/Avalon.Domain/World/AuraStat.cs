namespace Avalon.Domain.World;

/// <summary>
/// The stats an aura can modify: the stored numbers, so values are only ever appended. CritPct, BlockPct, HastePct and
/// MovementSpeed are percentage points, Evasion a rating (#1178, formerly the dodge chance), and the resistances percent;
/// a negative flat resistance is how a debuff lowers one.
/// </summary>
public enum AuraStat : byte
{
    Armor = 1, AttackDamage = 2, AbilityDamage = 3, CritPct = 4, Evasion = 5, BlockPct = 6, HastePct = 7,
    MovementSpeed = 8, MaxHealth = 9, MaxPower = 10, FireResistance = 11, ColdResistance = 12, LightningResistance = 13,
    BlightResistance = 14, MaxWard = 15,
}
