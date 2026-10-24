using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Trade;
using Avalon.World.Dialogue;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public.Characters;

namespace Avalon.World.Trading;

/// <summary>One side of a trade as it stands.</summary>
/// <param name="Items">In trade-slot order, each as it was when offered: its Slot is the giver's Bag slot.</param>
public readonly record struct TradeOffer(CharacterEntity Character, IReadOnlyList<InventoryItem> Items, ulong Gold);

/// <summary>What a completed trade does to one character.</summary>
/// <param name="Outgoing">Its own items, with their Bag slots and their modifiers (#1179).</param>
/// <param name="Incoming">
/// The partner's items, each with Slot set to its destination slot in this character's Bag, and its modifiers.
/// </param>
public sealed record TradeSidePlan(IReadOnlyList<CarriedItem> Outgoing, IReadOnlyList<CarriedItem> Incoming, ulong GoldOut, ulong GoldIn);

public sealed record TradePlan(TradeSidePlan First, TradeSidePlan Second);

/// <param name="Result">Seen from the failing side.</param>
/// <param name="FailingSide">0 for the first offer, 1 for the second, and -1 on Ok.</param>
public readonly record struct TradeDecision(TradeResult Result, int FailingSide, TradePlan? Plan);

/// <summary>
/// The completion rules of a face-to-face trade (#1175), as pure functions. Plan reads both characters' containers and
/// gold and the item templates, and changes nothing. It checks in order, the first side first at every step, and the
/// first failure answers: the offered items, the offered gold, room in each receiver's Bag, Unique, the gold cap.
/// </summary>
public static class TradeRules
{
    /// <summary>Item slots on each side of a trade.</summary>
    public const int MaxItems = 15;

    /// <summary>Metres. How close the target of a request must stand.</summary>
    public const float RequestRange = 5f;

    /// <summary>Metres. How far apart two traders may drift before their trade is cancelled: the dialogue leash.</summary>
    public const float KeepRange = NpcInteraction.LeashRange;

    /// <summary>
    /// Tradeable unless NoTrade or a quest item. An item whose template is gone is not tradeable.
    /// </summary>
    public static bool IsTradeable(ItemTemplate? template) =>
        template is not null && (template.Flags & (ItemTemplateFlags.NoTrade | ItemTemplateFlags.QuestItem)) == 0;

    /// <summary>
    /// Inclusive, so exactly <paramref name="range" /> is inside. A NaN distance compares false, so it is out of
    /// range, as <see cref="NpcInteraction.IsWithinLeash" /> treats it.
    /// </summary>
    public static bool WithinRange(Vector3 a, Vector3 b, float range) => Vector3.Distance(a, b) <= range;

    /// <summary>
    /// Decides a completion. Each offered item must still be at its Bag slot with the same instance and count, and
    /// tradeable; each giver must hold the gold offered; each receiver's Bag must have a free slot per incoming item,
    /// counting the slots its own outgoing items free (incoming items are placed whole, never merged into a stack);
    /// no receiver may end up owning two of a Unique item, counted across Equipment, Bag and Bank after its outgoing
    /// items leave; and no receiver's balance may pass <paramref name="maxGold" />. Destination slots are the lowest
    /// free ones, ascending, given to incoming items in offer order. Each item carries its giver's live list of
    /// modifiers, which the paired save copies and the swap hands over.
    /// </summary>
    public static TradeDecision Plan(
        TradeOffer first, TradeOffer second, Func<ItemTemplateId, ItemTemplate?> findTemplate, ulong maxGold)
    {
        if (ItemsInPlace(first, findTemplate) is not { } firstHeld)
            return Refuse(TradeResult.OfferInvalid, 0);
        if (ItemsInPlace(second, findTemplate) is not { } secondHeld)
            return Refuse(TradeResult.OfferInvalid, 1);

        if (Balance(first.Character) < first.Gold)
            return Refuse(TradeResult.OfferInvalid, 0);
        if (Balance(second.Character) < second.Gold)
            return Refuse(TradeResult.OfferInvalid, 1);

        if (DestinationSlots(first, second.Items.Count) is not { } firstSlots)
            return Refuse(TradeResult.BagFull, 0);
        if (DestinationSlots(second, first.Items.Count) is not { } secondSlots)
            return Refuse(TradeResult.BagFull, 1);

        if (!UniqueAllowed(first, second.Items, findTemplate))
            return Refuse(TradeResult.UniqueAlreadyOwned, 0);
        if (!UniqueAllowed(second, first.Items, findTemplate))
            return Refuse(TradeResult.UniqueAlreadyOwned, 1);

        if (PassesCap(first, second.Gold, maxGold))
            return Refuse(TradeResult.GoldCapReached, 0);
        if (PassesCap(second, first.Gold, maxGold))
            return Refuse(TradeResult.GoldCapReached, 1);

        // From the items as held, not the copies taken at offer time, so the save and both memories name the same items.
        var plan = new TradePlan(
            new TradeSidePlan(firstHeld, Placed(secondHeld, firstSlots), first.Gold, second.Gold),
            new TradeSidePlan(secondHeld, Placed(firstHeld, secondSlots), second.Gold, first.Gold));
        return new TradeDecision(TradeResult.Ok, -1, plan);
    }

    private static TradeDecision Refuse(TradeResult result, int side) => new(result, side, null);

    private static ulong Balance(CharacterEntity character) => character.Data?.Gold ?? 0;

    /// <summary>The offered items as the Bag holds them now, in offer order, or null when one is not in place.</summary>
    private static CarriedItem[]? ItemsInPlace(TradeOffer offer, Func<ItemTemplateId, ItemTemplate?> findTemplate)
    {
        CharacterInventoryContainer bag = offer.Character.Container(InventoryType.Bag);
        var items = new CarriedItem[offer.Items.Count];
        for (int i = 0; i < items.Length; i++)
        {
            InventoryItem offered = offer.Items[i];
            if (!bag.TryGet(offered.Slot, out InventoryItem held)
                || held.InstanceId != offered.InstanceId
                || held.Count != offered.Count
                || !IsTradeable(findTemplate(held.TemplateId)))
            {
                return null;
            }

            items[i] = new CarriedItem(held, offer.Character.ItemModifiers.BaseStatsOf(held.InstanceId),
                offer.Character.ItemModifiers.Of(held.InstanceId));
        }

        return items;
    }

    /// <summary>The lowest <paramref name="incoming" /> slots that are empty or freed by the offer, or null when too few.</summary>
    private static List<byte>? DestinationSlots(TradeOffer receiver, int incoming)
    {
        CharacterInventoryContainer bag = receiver.Character.Container(InventoryType.Bag);
        var slots = new List<byte>(incoming);
        for (int slot = 0; slot < bag.Capacity && slots.Count < incoming; slot++)
        {
            if (!bag.TryGet((byte)slot, out _) || Offers(receiver.Items, (byte)slot))
                slots.Add((byte)slot);
        }

        return slots.Count == incoming ? slots : null;
    }

    private static bool Offers(IReadOnlyList<InventoryItem> items, byte slot)
    {
        foreach (InventoryItem item in items)
        {
            if (item.Slot == slot)
                return true;
        }

        return false;
    }

    private static CarriedItem[] Placed(IReadOnlyList<CarriedItem> items, List<byte> slots)
    {
        var placed = new CarriedItem[items.Count];
        for (int i = 0; i < placed.Length; i++)
            placed[i] = items[i] with { Item = items[i].Item with { Slot = slots[i] } };

        return placed;
    }

    private static bool UniqueAllowed(
        TradeOffer receiver, IReadOnlyList<InventoryItem> incoming, Func<ItemTemplateId, ItemTemplate?> findTemplate)
    {
        foreach (InventoryItem item in incoming)
        {
            if (findTemplate(item.TemplateId) is not { } template || (template.Flags & ItemTemplateFlags.Unique) == 0)
                continue;

            long owned = HeldAnywhere(receiver.Character, template.Id)
                - CountOf(receiver.Items, template.Id)
                + CountOf(incoming, template.Id);
            if (owned > 1)
                return false;
        }

        return true;
    }

    private static long HeldAnywhere(CharacterEntity character, ItemTemplateId templateId) =>
        HeldIn(character.Container(InventoryType.Equipment), templateId)
        + HeldIn(character.Container(InventoryType.Bag), templateId)
        + HeldIn(character.Container(InventoryType.Bank), templateId);

    private static long HeldIn(CharacterInventoryContainer container, ItemTemplateId templateId)
    {
        long count = 0;
        foreach (InventoryItem item in container.Held)
        {
            if (item.TemplateId == templateId)
                count += item.Count;
        }

        return count;
    }

    private static long CountOf(IReadOnlyList<InventoryItem> items, ItemTemplateId templateId)
    {
        long count = 0;
        foreach (InventoryItem item in items)
        {
            if (item.TemplateId == templateId)
                count += item.Count;
        }

        return count;
    }

    /// <summary>
    /// Balance minus gold out plus gold in, above the cap. The gold check before it holds the balance at or above
    /// the gold out, so the subtraction cannot wrap, and the headroom is taken only when it cannot underflow.
    /// </summary>
    private static bool PassesCap(TradeOffer receiver, ulong goldIn, ulong maxGold)
    {
        ulong after = Balance(receiver.Character) - receiver.Gold;
        return after > maxGold || goldIn > maxGold - after;
    }
}
