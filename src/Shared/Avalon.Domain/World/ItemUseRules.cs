namespace Avalon.Domain.World;

/// <summary>
/// The rules the world's vendor and quest catalogs hold an item template to, as pure functions. The catalogs call
/// them when they load, and the API calls the same ones before it saves an item edit, so an edit the API accepts
/// is never one the world would drop a vendor row or a quest for. A null result means the item passes.
/// </summary>
public static class ItemUseRules
{
    /// <summary>
    /// Whether <paramref name="item"/> may be sold by a stock row priced at <paramref name="priceOverride"/>
    /// (null: the item's BuyPrice). A quest item comes only from its quest, and a price below the SellPrice would
    /// let a player buy and sell it forever for a profit.
    /// </summary>
    public static string? VendorStockProblem(ItemTemplate item, uint? priceOverride)
    {
        ulong id = item.Id.Value;
        if (item.Flags.HasFlag(ItemTemplateFlags.QuestItem))
            return $"item template {id} is a quest item; quest items are never sold";

        uint price = priceOverride ?? item.BuyPrice;
        if (price < item.SellPrice)
            return $"price {price} is below the item's SellPrice {item.SellPrice}; buying and selling it back would make gold";

        return null;
    }

    /// <summary>Whether a Collect objective may gather <paramref name="item"/>: only a QuestItem can be collected.</summary>
    public static string? CollectObjectiveProblem(ItemTemplate item, uint objectiveId)
    {
        return item.Flags.HasFlag(ItemTemplateFlags.QuestItem)
            ? null
            : $"objective {objectiveId} collects item template {item.Id.Value}, which is not a QuestItem";
    }

    /// <summary>Whether a quest may pay <paramref name="item"/> as a reward: a Unique one could not always be paid.</summary>
    public static string? QuestRewardProblem(ItemTemplate item)
    {
        return item.Flags.HasFlag(ItemTemplateFlags.Unique)
            ? $"reward item template {item.Id.Value} is Unique; a turn-in could not always pay it"
            : null;
    }
}
