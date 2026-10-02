namespace Avalon.World.Items.Scripts;

/// <summary>
/// The Town Portal Scroll (item 3): after its cast, sends its reader to the town of the map it is on, the way a
/// respawn resolves it, and is consumed only once the return has started. Refused in a town and while a return is
/// already under way.
/// </summary>
public sealed class TownPortalScroll : ItemScript
{
    public override string? CanUse(IItemUseContext ctx)
    {
        if (ctx.InTown)
            return "You are already in town.";

        return ctx.ReturningToTown ? "You are already on your way to town." : null;
    }

    public override void OnUse(IItemUseContext ctx)
    {
        if (ctx.ReturnToTown())
            ctx.Consume();
    }
}
