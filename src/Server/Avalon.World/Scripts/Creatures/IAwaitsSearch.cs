namespace Avalon.World.Scripts.Creatures;

/// <summary>
/// A built-in script whose only work while it waits is its range detector's timer (#916, #979). World-side, not on the
/// modding API: the instance's <c>CreatureScheduler</c> keeps that timer for it while it waits, exactly as the script's
/// per-tick visits advanced it, and hands it back when the script runs again.
/// </summary>
internal interface IAwaitsSearch
{
    /// <summary>
    /// True when every Update until the next search would only add the tick to the detector's timer; then
    /// <paramref name="elapsed" /> is that timer, in seconds. False means the script is active, needing the next tick:
    /// the scheduler reads nothing else (not <c>NextUpdateIn</c>) from a script that implements this.
    /// </summary>
    bool AwaitsSearch(out float elapsed);

    /// <summary>Puts the detector's timer back to <paramref name="elapsed" />, where the per-tick visits would have it.</summary>
    void ResumeSearchWait(float elapsed);
}
