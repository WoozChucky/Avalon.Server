using Avalon.Common;

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
        if ((item.Flags & ItemTemplateFlags.QuestItem) != 0)
            return $"item template {id} is a quest item; quest items are never sold";

        uint price = priceOverride ?? item.BuyPrice;
        if (price < item.SellPrice)
            return $"price {price} is below the item's SellPrice {item.SellPrice}; buying and selling it back would make gold";

        return null;
    }

    /// <summary>Whether a Collect objective may gather <paramref name="item"/>: only a QuestItem can be collected.</summary>
    public static string? CollectObjectiveProblem(ItemTemplate item, uint objectiveId)
    {
        return (item.Flags & ItemTemplateFlags.QuestItem) != 0
            ? null
            : $"objective {objectiveId} collects item template {item.Id.Value}, which is not a QuestItem";
    }

    /// <summary>
    /// Whether a quest may pay <paramref name="item"/> as a reward: one flagged Unique (own one copy) could not always be
    /// paid, and a Unique-rarity item never comes from a quest (#1179).
    /// </summary>
    public static string? QuestRewardProblem(ItemTemplate item)
    {
        if ((item.Flags & ItemTemplateFlags.Unique) != 0)
            return $"reward item template {item.Id.Value} is Unique; a turn-in could not always pay it";

        return item.Rarity == ItemRarity.Unique
            ? $"reward item template {item.Id.Value} is a Unique-rarity item; uniques never come from quests"
            : null;
    }
}
