namespace Avalon.World.Items;

/// <summary>
/// What a usable item does (item use, 2026-10-02): one subclass per behaviour, named by ItemTemplate.UseScript and
/// found by class name in ScriptManager, once, at Load (never hot reloaded). Built once per type through the narrowed
/// QuestScriptServices provider (loggers and the clock only), with no runtime arguments, and shared by every use and
/// every character, so it keeps no per-use state. World-side, not the modding API. Every hook runs on the tick,
/// synchronously, and is contained: a throw is logged at Error and the use is answered InternalError. A script reaches
/// the world only through <see cref="IItemUseContext" />.
/// </summary>
public abstract class ItemScript
{
    /// <summary>Null to allow the use; otherwise the line the player is shown, answered Refused. Asked again when a cast completes.</summary>
    public virtual string? CanUse(IItemUseContext ctx) => null;

    /// <summary>
    /// The effect. Runs at once, or when the cast completes. <see cref="IItemUseContext.Consume" /> marks what the
    /// use spends; it is taken, and the cooldown started, only once this returns without throwing.
    /// </summary>
    public abstract void OnUse(IItemUseContext ctx);

    /// <summary>A cast-time use's bar has started. A throw ends the cast and answers InternalError.</summary>
    public virtual void OnCastStart(IItemUseContext ctx)
    {
    }

    /// <summary>
    /// A cast-time use's bar ended early; the use is answered Interrupted and nothing is spent. A throw is logged at
    /// Error and answers InternalError instead, still spending nothing and starting no cooldown.
    /// </summary>
    public virtual void OnInterrupted(IItemUseContext ctx)
    {
    }
}
