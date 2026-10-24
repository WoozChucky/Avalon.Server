using Avalon.Domain.World;

namespace Avalon.Combat;

/// <summary>
/// The speeds a caster casts with (#1178), each in percentage points and already bounded to 0 through its cap, and
/// the time between its basic attacks before attack speed, in seconds: a character's main hand's (or the bare hand's), a
/// creature's BaseAttackTime. Fixed at a character's stats refresh and read once per cast, so a gear change mid-cast
/// changes nothing. Default paces nothing: every speed none and no attack time.
/// </summary>
public readonly record struct CasterPace(float AttackSpeedPct, float CastSpeedPct, float CooldownRecoveryPct, float AttackTimeSeconds)
{
    /// <summary>A character's pace: its offence block's raw speeds, each bounded by <paramref name="f" />'s cap.</summary>
    public static CasterPace From(in OffenceStats o, float attackTimeSeconds, CombatFormula f) => new(
        Bound(o.AttackSpeedPct, f.AttackSpeedCap),
        Bound(o.CastSpeedPct, f.CastSpeedCap),
        Bound(o.CooldownRecoveryPct, f.CooldownRecoveryCap),
        attackTimeSeconds);

    /// <summary>
    /// A raw speed counted from 0 to <paramref name="cap" />: a negative or unreadable one is none, so no speed lengthens a
    /// time, and a negative or unreadable cap allows none.
    /// </summary>
    public static float Bound(float pct, float cap) =>
        pct > 0f ? MathF.Min(pct, cap > 0f ? cap : 0f) : 0f;
}
