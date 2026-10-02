using Avalon.Network.Packets.State;

namespace Avalon.World.Items.Scripts;

/// <summary>
/// The Mana Potion (item 2): restores UseValue percent of the maximum Mana or Energy, one potion a use. Fury is never
/// restored by a potion, and a class with no pool cannot drink it; both are refused, as is a full pool.
/// </summary>
public sealed class RestorePower : ItemScript
{
    public override string? CanUse(IItemUseContext ctx)
    {
        if (ctx.PowerType is not (PowerType.Mana or PowerType.Energy))
            return "You cannot drink this.";

        return ctx.Power >= ctx.MaxPower
            ? $"Your {ctx.PowerType.ToString().ToLowerInvariant()} is already full."
            : null;
    }

    public override void OnUse(IItemUseContext ctx)
    {
        ctx.RestorePower(ItemUsePercent.Of(ctx.MaxPower, ctx.UseValue));
        ctx.Consume();
    }
}
