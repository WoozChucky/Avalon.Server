using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Vendor;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Quests;

namespace Avalon.World.Vendors;

/// <summary>One cost item, multiplied out for the whole purchase.</summary>
public readonly record struct VendorCostLine(ItemTemplateId ItemTemplateId, uint Count);

/// <param name="TotalPrice">The unit price times <paramref name="Count" />.</param>
/// <param name="Costs">Each cost item's count times <paramref name="Count" />.</param>
public sealed record BuyPlan(VendorStockView Row, ItemTemplate Item, uint Count, ulong TotalPrice, IReadOnlyList<VendorCostLine> Costs);

public readonly record struct BuyDecision(VendorResult Result, BuyPlan? Plan);

/// <param name="Slot">The Bag slot the item is taken from.</param>
/// <param name="Payment">SellPrice times <paramref name="Count" />, which is also the buyback price.</param>
public sealed record SellPlan(ushort Slot, uint Count, ulong Payment);

public readonly record struct SellDecision(VendorResult Result, SellPlan? Plan);

public sealed record BuybackPlan(uint Index, BuybackEntry Entry);

public readonly record struct BuybackDecision(VendorResult Result, BuybackPlan? Plan);

/// <summary>
/// The spec #432 trade rules, as pure functions. They read the character's containers, gold and
/// buyback list, the stock, the item templates and the quest hook, and change nothing. Each checks
/// in the spec's order, and the first failure answers. VendorTrade applies an accepted plan.
/// </summary>
public static class VendorRules
{
    /// <summary>
    /// Copper per unit: the row's override, or the item's BuyPrice, and never less than the item's
    /// SellPrice. VendorCatalog refuses a row priced below SellPrice, but a later /reload items can
    /// raise a SellPrice without a vendor reload, so the floor is enforced here too: buying an item
    /// must never cost less than selling it back pays (#432).
    /// </summary>
    public static ulong UnitPrice(VendorStockView row, ItemTemplate item) =>
        Math.Max(row.PriceOverride ?? item.BuyPrice, item.SellPrice);

    /// <summary>A row with a quest requirement is shown, and sold, only once IQuestProgress says it is met.</summary>
    public static bool IsVisible(VendorStockView row, CharacterEntity character, IQuestProgress quests) =>
        row.Requirement is not { } requirement || quests.IsMet(character, requirement.QuestId, requirement.State);

    /// <summary>Below full durability. A consumable's full durability is 0, so a potion is never damaged.</summary>
    public static bool IsDamaged(InventoryItem item, ItemTemplate template) =>
        item.Durability < ItemInstanceDefaults.FullDurability(template);

    /// <summary>
    /// Buy: alive; shop open; the row exists and its gate passes; the count; the stock; the gold;
    /// the cost items from the Bag; room once the costs are out; unique.
    /// </summary>
    public static BuyDecision DecideBuy(
        CharacterEntity character,
        bool shopOpen,
        VendorStockState? stock,
        uint sequence,
        uint? count,
        Func<ItemTemplateId, ItemTemplate?> findTemplate,
        IQuestProgress quests)
    {
        if (character.IsDead)
            return new BuyDecision(VendorResult.Dead, null);

        if (!shopOpen || stock is null)
            return new BuyDecision(VendorResult.ShopClosed, null);

        VendorStockView? row = null;
        foreach (VendorStockView candidate in stock.Rows)
        {
            if (candidate.Sequence == sequence)
            {
                row = candidate;
                break;
            }
        }

        if (row is null || !IsVisible(row, character, quests) || findTemplate(row.ItemTemplateId) is not { } item)
            return new BuyDecision(VendorResult.NotFound, null);

        uint buying = count ?? 1;
        if (buying == 0 || buying > InventoryMove.MaxStack(item))
            return new BuyDecision(VendorResult.InvalidCount, null);

        if (stock.Available(row) is { } available && available < buying)
            return new BuyDecision(VendorResult.OutOfStock, null);

        // A uint price times a uint count cannot pass a ulong.
        ulong total = UnitPrice(row, item) * buying;
        if (Balance(character) < total)
            return new BuyDecision(VendorResult.NotEnoughGold, null);

        var costs = new List<VendorCostLine>(row.Costs.Count);
        foreach (VendorCostView cost in row.Costs)
        {
            ulong needed = (ulong)cost.Count * buying;
            if (needed > uint.MaxValue || HeldIn(character, InventoryType.Bag, cost.ItemTemplateId) < (long)needed)
                return new BuyDecision(VendorResult.MissingItems, null);

            costs.Add(new VendorCostLine(cost.ItemTemplateId, (uint)needed));
        }

        // Room is judged against the Bag as it will be once the costs are out: a cost stack that
        // empties frees its slot, and one that survives is not room.
        Dictionary<ushort, InventoryItem> bagAfter = BagAfter(character, costs);
        if (RoomFor(item, bagAfter, character.Container(InventoryType.Bag).Capacity) < buying)
            return new BuyDecision(VendorResult.InventoryFull, null);

        if (item.Flags.HasFlag(ItemTemplateFlags.Unique) && OwnedAfter(character, item.Id, bagAfter) + buying > 1)
            return new BuyDecision(VendorResult.UniqueAlreadyOwned, null);

        return new BuyDecision(VendorResult.Ok, new BuyPlan(row, item, buying, total, costs));
    }

    /// <summary>
    /// Sell: alive; shop open; a Bag slot; an item there of a sellable template; not damaged; the
    /// count; the money cap. Only the Bag is ever named, so nothing is sold from Equipment or the Bank.
    /// </summary>
    public static SellDecision DecideSell(
        CharacterEntity character,
        bool shopOpen,
        uint bagSlot,
        uint? count,
        Func<ItemTemplateId, ItemTemplate?> findTemplate,
        ulong maxMoney)
    {
        if (character.IsDead)
            return new SellDecision(VendorResult.Dead, null);

        if (!shopOpen)
            return new SellDecision(VendorResult.ShopClosed, null);

        CharacterInventoryContainer bag = character.Container(InventoryType.Bag);
        if (bagSlot >= bag.Capacity || !bag.TryGet((ushort)bagSlot, out InventoryItem item))
            return new SellDecision(VendorResult.NotFound, null);

        if (findTemplate(item.TemplateId) is not { } template
            || template.Flags.HasFlag(ItemTemplateFlags.NoSell)
            || template.SellPrice == 0)
            return new SellDecision(VendorResult.NotSellable, null);

        if (IsDamaged(item, template))
            return new SellDecision(VendorResult.Damaged, null);

        uint selling = count ?? item.Count;
        if (selling == 0 || selling > item.Count)
            return new SellDecision(VendorResult.InvalidCount, null);

        // Written as CharacterWallet.TryAddMoney refuses, so the apply cannot disagree.
        ulong payment = (ulong)template.SellPrice * selling;
        ulong balance = Balance(character);
        if (balance >= maxMoney || payment > maxMoney - balance)
            return new SellDecision(VendorResult.MoneyCapReached, null);

        return new SellDecision(VendorResult.Ok, new SellPlan((ushort)bagSlot, selling, payment));
    }

    /// <summary>
    /// Buyback: alive; shop open; the index; the gold it sold for; a free Bag slot, because the
    /// exact instance is never merged into a stack. Also unique (#432), which the spec's list
    /// leaves out: re-adding a sold unique item beside a newer copy would leave two.
    /// </summary>
    public static BuybackDecision DecideBuyback(
        CharacterEntity character,
        bool shopOpen,
        uint index,
        Func<ItemTemplateId, ItemTemplate?> findTemplate)
    {
        if (character.IsDead)
            return new BuybackDecision(VendorResult.Dead, null);

        if (!shopOpen)
            return new BuybackDecision(VendorResult.ShopClosed, null);

        if (!character.Buyback.TryGet(index, out BuybackEntry? entry))
            return new BuybackDecision(VendorResult.NotFound, null);

        if (Balance(character) < entry.Price)
            return new BuybackDecision(VendorResult.NotEnoughGold, null);

        if (!character.Container(InventoryType.Bag).FreeSlots().Any())
            return new BuybackDecision(VendorResult.InventoryFull, null);

        if (findTemplate(entry.Item.TemplateId) is { } template
            && template.Flags.HasFlag(ItemTemplateFlags.Unique)
            && HeldAnywhere(character, template.Id) + entry.Item.Count > 1)
            return new BuybackDecision(VendorResult.UniqueAlreadyOwned, null);

        return new BuybackDecision(VendorResult.Ok, new BuybackPlan(index, entry));
    }

    private static ulong Balance(CharacterEntity character) => character.Data?.Money ?? 0;

    private static long HeldIn(CharacterEntity character, InventoryType container, ItemTemplateId templateId) =>
        character.Container(container).Items.Where(i => i.TemplateId == templateId).Sum(i => (long)i.Count);

    private static long HeldAnywhere(CharacterEntity character, ItemTemplateId templateId) =>
        HeldIn(character, InventoryType.Equipment, templateId)
        + HeldIn(character, InventoryType.Bag, templateId)
        + HeldIn(character, InventoryType.Bank, templateId);

    /// <summary>The Bag as TryRemove would leave it once every cost is out: each from the highest slot first.</summary>
    private static Dictionary<ushort, InventoryItem> BagAfter(CharacterEntity character, IReadOnlyList<VendorCostLine> costs)
    {
        Dictionary<ushort, InventoryItem> bag = character.Container(InventoryType.Bag).Items.ToDictionary(i => i.Slot);

        foreach (VendorCostLine cost in costs)
        {
            uint remaining = cost.Count;
            foreach (InventoryItem stack in bag.Values
                         .Where(i => i.TemplateId == cost.ItemTemplateId)
                         .OrderByDescending(i => i.Slot)
                         .ToList())
            {
                if (remaining == 0)
                    break;

                uint take = Math.Min(stack.Count, remaining);
                if (take == stack.Count)
                    bag.Remove(stack.Slot);
                else
                    bag[stack.Slot] = stack with { Count = stack.Count - take };

                remaining -= take;
            }
        }

        return bag;
    }

    /// <summary>What TryAdd could fit: room in partial stacks of the item, plus a full stack per free slot.</summary>
    private static long RoomFor(ItemTemplate item, Dictionary<ushort, InventoryItem> bag, ushort capacity)
    {
        uint maxStack = InventoryMove.MaxStack(item);
        long partial = bag.Values
            .Where(i => i.TemplateId == item.Id && i.Count < maxStack)
            .Sum(i => (long)(maxStack - i.Count));

        return partial + (long)(capacity - bag.Count) * maxStack;
    }

    private static long OwnedAfter(CharacterEntity character, ItemTemplateId templateId, Dictionary<ushort, InventoryItem> bagAfter) =>
        bagAfter.Values.Where(i => i.TemplateId == templateId).Sum(i => (long)i.Count)
        + HeldIn(character, InventoryType.Equipment, templateId)
        + HeldIn(character, InventoryType.Bank, templateId);
}
