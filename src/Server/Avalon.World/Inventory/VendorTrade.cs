using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Vendor;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Quests;
using Avalon.World.Vendors;

namespace Avalon.World.Inventory;

/// <summary>
/// The vendor service operations (spec #432), beside the inventory service and the wallet. Each
/// asks VendorRules, then applies the plan it accepted through IInventoryService and IWallet, so
/// every change carries the usual SaveState and ClientChanges marks. Tick thread only. Every step
/// is checked, against this same state on this same thread, before anything changes, so no client
/// input can make a step fail part way; if one does, it throws rather than carry on. The apply
/// order takes value from the player before giving any, so a failure caused by a bug loses value,
/// never creates it, and is not rolled back. A sale pays by the copy's rarity (<paramref name="sellFactors" />, #1179), and
/// its buyback costs exactly what it paid.
/// </summary>
public sealed class VendorTrade(
    CharacterEntity owner,
    IInventoryService inventory,
    IWallet wallet,
    Func<ItemTemplateId, ItemTemplate?> findTemplate,
    ulong maxGold,
    SellFactors sellFactors)
{
    /// <summary>Takes each cost item, then the gold, then adds the item, then takes the stock.</summary>
    public VendorResult TryBuy(
        bool shopOpen, VendorStockState? stock, uint sequence, uint? count, IQuestProgress quests, DateTime now)
    {
        BuyDecision decision = VendorRules.DecideBuy(owner, shopOpen, stock, sequence, count, findTemplate, quests);
        if (decision.Plan is not { } plan)
            return decision.Result;

        foreach (VendorCostLine cost in plan.Costs)
            Expect(inventory.TryRemove(cost.ItemTemplateId, cost.Count) == InventoryRemoveResult.Ok, "cost item");

        Expect(wallet.TrySpend(plan.TotalPrice) == WalletResult.Ok, "price");
        Expect(inventory.TryAdd(plan.Item.Id, plan.Count) == InventoryAddResult.Ok, "item");
        stock!.Take(plan.Row, plan.Count, now);
        return VendorResult.Ok;
    }

    /// <summary>
    /// Takes the item out, pays for it, and puts what left (the instance with its modifiers, or for a
    /// partial sale a copy with a new id) at the front of the buyback list.
    /// </summary>
    public VendorResult TrySell(bool shopOpen, byte bagSlot, uint? count)
    {
        SellDecision decision = VendorRules.DecideSell(owner, shopOpen, bagSlot, count, findTemplate, maxGold, sellFactors);
        if (decision.Plan is not { } plan)
            return decision.Result;

        CarriedItem sold = inventory.TakeOut(new SlotRef(InventoryType.Bag, plan.Slot), plan.Count);
        Expect(wallet.TryAddGold(plan.Payment) == WalletResult.Ok, "payment");
        owner.Buyback.Push(new BuybackEntry(sold, plan.Payment));
        owner.VendorListOwed = true;
        return VendorResult.Ok;
    }

    /// <summary>Charges what it sold for and re-adds the exact instance, under its own id, with its modifiers.</summary>
    public VendorResult TryBuyback(bool shopOpen, uint index)
    {
        BuybackDecision decision = VendorRules.DecideBuyback(owner, shopOpen, index, findTemplate);
        if (decision.Plan is not { } plan)
            return decision.Result;

        Expect(wallet.TrySpend(plan.Entry.Price) == WalletResult.Ok, "buyback price");
        Expect(inventory.TryAddInstance(plan.Entry.Item) == InventoryAddResult.Ok, "bought-back item");
        owner.Buyback.RemoveAt(plan.Index);
        owner.VendorListOwed = true;
        return VendorResult.Ok;
    }

    private static void Expect(bool applied, string what)
    {
        if (!applied)
            throw new InvalidOperationException($"VendorRules accepted a trade whose {what} then failed to apply.");
    }
}
