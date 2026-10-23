using Avalon.Network.Packets.Combat;

namespace Avalon.Combat;

/// <summary>
/// What a tick-giving aura deals or heals per tick and per stack, taken once from its caster when it is applied, with
/// the caster's crit chance and level, which each tick rolls crit and meets armour with, even once the caster is gone.
/// <paramref name="Type" /> is the damage type its ticks deal (#1178), fixed when it was applied.
/// </summary>
public readonly record struct AuraSnapshot(float PerTickPerStack, float CritPct, ushort CasterLevel,
    DamageType Type = DamageType.Physical)
{
    /// <summary>The caster as a tick resolves against it: its level, crit and the ticks' type, no damage stats and no weapon.</summary>
    public AttackerCombat Attacker => new(CasterLevel, 0, 0, CritPct, 0, 0, Type);
}
