namespace Avalon.World.Items.Scripts;

/// <summary>
/// The health potions (items 1 and 56): restores UseValue percent of the maximum health through the combat service,
/// one potion a use. Refused at full health. The item data must set UseValue (the seed does): without it the potion
/// restores nothing and is still spent.
/// </summary>
public sealed class RestoreHealth : ItemScript
{
    public override string? CanUse(IItemUseContext ctx) =>
        ctx.Health >= ctx.MaxHealth ? "You are already at full health." : null;

    public override void OnUse(IItemUseContext ctx)
    {
        ctx.RestoreHealth(ItemUsePercent.Of(ctx.MaxHealth, ctx.UseValue));
        ctx.Consume();
    }
}
