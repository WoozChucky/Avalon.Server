namespace Avalon.Combat;

/// <summary>
/// What a tick-giving aura deals or heals per tick and per stack, taken once from its caster when it is applied, with
/// the caster's crit chance and level, which each tick rolls crit and meets armour with, even once the caster is gone.
/// </summary>
public readonly record struct AuraSnapshot(float PerTickPerStack, float CritPct, ushort CasterLevel)
{
    /// <summary>The caster as a tick resolves against it: its level and crit, no damage stats and no weapon.</summary>
    public AttackerCombat Attacker => new(CasterLevel, 0, 0, CritPct, 0, 0);
}
