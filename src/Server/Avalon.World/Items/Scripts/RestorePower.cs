namespace Avalon.World.Items.Scripts;

/// <summary>
/// The Mana Potion (item 2): restores UseValue percent of the maximum mana, one potion a use. Every class can drink it
/// (#1181); only a full pool refuses it. The item data must set UseValue (the seed does): without it the potion restores
/// nothing and is still spent.
/// </summary>
public sealed class RestorePower : ItemScript
{
    public override string? CanUse(IItemUseContext ctx) =>
        ctx.Power >= ctx.MaxPower ? "Your mana is already full." : null;

    public override void OnUse(IItemUseContext ctx)
    {
        ctx.RestorePower(ItemUsePercent.Of(ctx.MaxPower, ctx.UseValue));
        ctx.Consume();
    }
}
