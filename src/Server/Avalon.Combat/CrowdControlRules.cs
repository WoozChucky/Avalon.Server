using Avalon.Domain.World;

namespace Avalon.Combat;

/// <summary>
/// The crowd-control rules (#770): how much of a CC aura's duration lands on a creature that resists it, and how much
/// on a character already controlled by other characters. Pure, and allocation-free: the world asks them on every
/// application.
/// </summary>
public static class CrowdControlRules
{
    /// <summary>
    /// The duration percentage a unit takes of <paramref name="t" />: the smallest among the types it grants (its
    /// Control flags, and slow when <see cref="AuraRules.IsSlow" />), each read from the matching argument; 100 when
    /// it grants none. An aura granting stun and root on a unit that resists only stun is resisted as a stun.
    /// </summary>
    public static float ResistPct(AuraTemplate t, float stun, float root, float silence, float slow)
    {
        float pct = 100f;
        if ((t.Control & CrowdControl.Stun) != 0) pct = Math.Min(pct, stun);
        if ((t.Control & CrowdControl.Root) != 0) pct = Math.Min(pct, root);
        if ((t.Control & CrowdControl.Silence) != 0) pct = Math.Min(pct, silence);
        if (AuraRules.IsSlow(t)) pct = Math.Min(pct, slow);
        return pct;
    }

    /// <summary>
    /// <c>floor(durationMs x pct / 100)</c>: the milliseconds that land after a resistance or diminishing percentage.
    /// 0 means the application is refused. A percentage that is not finite, or 0 or less, gives 0; one above 100 gives
    /// the whole duration (the data refuses both).
    /// </summary>
    public static uint Scale(uint durationMs, float pct)
    {
        if (!float.IsFinite(pct) || pct <= 0f) return 0u;
        if (pct >= 100f) return durationMs;
        return (uint)Math.Floor(durationMs * (double)pct / 100d);
    }

    /// <summary>
    /// The PvP diminishing step (#770): the percentage of its duration a stun, root or silence takes on a character
    /// after <paramref name="priorLanded" /> applications of that type already landed within the window: 100, then
    /// 50, then 25, then 0 (immune) for 3 or more.
    /// </summary>
    public static float DiminishPct(int priorLanded) => priorLanded switch
    {
        <= 0 => 100f,
        1 => 50f,
        2 => 25f,
        _ => 0f,
    };
}
