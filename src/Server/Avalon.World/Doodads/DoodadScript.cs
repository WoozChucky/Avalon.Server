namespace Avalon.World.Doodads;

/// <summary>
/// What a doodad does beyond its template's data: one subclass per behaviour, named by DoodadTemplate.ScriptName and
/// found by class name in ScriptManager, once, at Load (never hot reloaded). Built once per type through the narrowed
/// QuestScriptServices provider (loggers and the clock only), with no runtime arguments (<see cref="DoodadScripts" />),
/// and shared by every doodad that names it, in every instance, so it keeps no per-doodad or per-use state. World-side,
/// not the modding API. Every hook is contained: a throw is logged at Error, at most once per script and hook per
/// ThrottledErrorLog.Interval, and the caller goes on. A script reaches the world only through the context it is
/// handed, which is valid during the call alone: one kept and used later throws.
/// </summary>
public abstract class DoodadScript
{
    /// <summary>
    /// The doodad was placed, as its instance is built, off the tick: it may read and <see cref="IDoodadContext.SetState" />;
    /// the live acts (despawn, respawn, loot, auras, restore, lines) do nothing there.
    /// </summary>
    public virtual void OnSpawn(IDoodadContext ctx)
    {
    }

    /// <summary>
    /// Null to allow the use; otherwise the reason it is refused, logged. Read only: it is asked before the use and again
    /// when a cast completes, so it must not change anything; effects belong in <see cref="OnUse" />.
    /// </summary>
    public virtual string? CanUse(IDoodadUseContext ctx) => null;

    /// <summary>The effect of a use that passed every check: at once, or when its cast completes. On the tick.</summary>
    public abstract void OnUse(IDoodadUseContext ctx);

    /// <summary>The doodad was despawned, by its spent look or a script. On the tick.</summary>
    public virtual void OnDespawn(IDoodadContext ctx)
    {
    }

    /// <summary>A spent doodad is ready again. On the tick.</summary>
    public virtual void OnRespawn(IDoodadContext ctx)
    {
    }

    /// <summary>
    /// Whether one viewer is told the doodad exists: null keeps what its data gate answered
    /// (<see cref="IDoodadViewContext.DataGateAllows" />), true or false overrides it. Asked only when the gate is asked
    /// again (the first view decision that finds the doodad in range, one after the viewer's quests or level moved, one
    /// after the doodad did, <see cref="IDoodadContext.RefreshVisibility" /> included) and when the viewer uses the
    /// doodad (each interact, and again as its cast completes), never per tick, and never for a solid doodad, which
    /// everyone sees. Read only, and it must not allocate: it runs inside a view decision and an interact. A doodad
    /// hidden from a viewer is never usable by it.
    /// </summary>
    public virtual bool? CanSee(IDoodadViewContext ctx) => null;
}
