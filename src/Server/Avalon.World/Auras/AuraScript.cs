namespace Avalon.World.Auras;

/// <summary>
/// Custom behaviour for one aura, named by AuraTemplate.ScriptName and found by class name in ScriptManager, once, at
/// Load (never hot reloaded). Built once per type through the narrowed QuestScriptServices provider (loggers and the
/// clock only), with no runtime arguments, and shared by every aura that names it and every unit holding one, so it
/// keeps no per-aura state: everything per aura is in the IAuraContext a hook is handed. Every hook runs on the tick and
/// is contained: a throw is logged and the aura goes on. World-side, not the modding API.
/// </summary>
public abstract class AuraScript
{
    /// <summary>The aura was applied anew (not refreshed, not stacked).</summary>
    public virtual void OnApply(IAuraContext ctx)
    {
    }

    /// <summary>One of its ticks happened, after its damage or heal.</summary>
    public virtual void OnTick(IAuraContext ctx)
    {
    }

    /// <summary>It gained a stack; <see cref="IAuraContext.Stacks" /> is the new count.</summary>
    public virtual void OnStack(IAuraContext ctx)
    {
    }

    /// <summary>It ended, for <paramref name="reason" />; the unit no longer holds it.</summary>
    public virtual void OnRemove(IAuraContext ctx, AuraRemoveReason reason)
    {
    }
}
