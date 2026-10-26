namespace Avalon.Domain.World;

/// <summary>
/// Who a Resonance effect's aura goes on (#1180): the wearer, the unit the wearer's hit landed on, or the unit whose hit
/// struck (or missed) the wearer.
/// </summary>
public enum ResonanceTarget : byte { Wearer = 1, HitTarget = 2, Attacker = 3 }
