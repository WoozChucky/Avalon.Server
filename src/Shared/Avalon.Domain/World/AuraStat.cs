namespace Avalon.Domain.World;

/// <summary>The stats an aura can modify. CritPct, DodgePct, BlockPct, HastePct and MovementSpeed are percentage points.</summary>
public enum AuraStat : byte
{
    Armor = 1, AttackDamage = 2, AbilityDamage = 3, CritPct = 4, DodgePct = 5, BlockPct = 6, HastePct = 7,
    MovementSpeed = 8, MaxHealth = 9, MaxPower = 10,
}
