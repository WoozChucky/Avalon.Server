using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.World.Inventory;
using Avalon.World.Public.Characters;
using Avalon.World.Trading;

namespace Avalon.World.Persistence;

/// <summary>
/// Save first, then swap (#1175): one character's save batch as it will be once its side of a trade is applied, built
/// from its snapshot before memory changes. The live row and containers are never touched.
/// </summary>
public static class TradeSaveBatch
{
    /// <summary>
    /// A trade changes an item's owner (#1179), so an outgoing item is never deleted: this side drops its Bag slot
    /// (unless an incoming item lands there) and whatever its snapshot held for the item, its row or its modifier write,
    /// and the receiver's side writes it. Each incoming item is upserted as this character's (an update of its owner when
    /// the row exists, an insert when it was never saved), stamped <paramref name="now" />, with a slot row at its
    /// destination and its whole modifier set, copied here on the tick. The row is a new copy with the gold given and
    /// received. Each item id and each slot key appears at most once, as the repository requires.
    /// </summary>
    /// <exception cref="OverflowException">The side gives more gold than the row holds: the plan was not checked.</exception>
    public static CharacterSaveBatch Apply(CharacterSaveBatch batch, TradeSidePlan side, DateTime now)
    {
        Character row = batch.Row.Copy();
        row.Gold = checked(row.Gold - side.GoldOut + side.GoldIn);

        HashSet<ItemInstanceId> touchedItems = [];
        HashSet<(InventoryType Container, byte Slot)> touchedSlots = [];
        foreach (CarriedItem carried in side.Outgoing)
        {
            touchedItems.Add(carried.Item.InstanceId);
            touchedSlots.Add((InventoryType.Bag, carried.Item.Slot));
        }

        foreach (CarriedItem carried in side.Incoming)
        {
            touchedItems.Add(carried.Item.InstanceId);
            touchedSlots.Add((InventoryType.Bag, carried.Item.Slot));
        }

        // Whatever the snapshot said about a key the trade touches is replaced by what the trade makes of it.
        List<ItemInstance> upsertItems = [.. batch.UpsertItems.Where(i => !touchedItems.Contains(i.Id))];
        List<ItemInstanceId> deleteItems = [.. batch.DeleteItems.Where(id => !touchedItems.Contains(id))];
        List<CharacterInventory> upsertSlots = [.. batch.UpsertSlots.Where(s => !touchedSlots.Contains((s.Container, s.Slot)))];
        List<(InventoryType Container, byte Slot)> deleteSlots = [.. batch.DeleteSlots.Where(k => !touchedSlots.Contains(k))];
        var modifiers = new ModifierRows();
        if (batch.Modifiers is { } write)
            modifiers.AddAllBut(write, touchedItems);

        HashSet<byte> landing = [];
        foreach (CarriedItem carried in side.Incoming)
        {
            InventoryItem item = carried.Item;
            landing.Add(item.Slot);
            upsertItems.Add(CharacterSaveSnapshot.ItemRow(item, row.Id, now));
            upsertSlots.Add(new CharacterInventory
            {
                CharacterId = row.Id,
                Container = InventoryType.Bag,
                Slot = item.Slot,
                ItemId = item.InstanceId,
            });

            // Every incoming item, one without modifiers too: its set as the giver holds it, which the giver's own saves
            // may not have written yet.
            modifiers.Add(item.InstanceId, carried.Modifiers);
        }

        foreach (CarriedItem carried in side.Outgoing)
        {
            if (!landing.Contains(carried.Item.Slot))
                deleteSlots.Add((InventoryType.Bag, carried.Item.Slot));
        }

        return batch with
        {
            Row = row,
            UpsertItems = upsertItems,
            DeleteItems = deleteItems,
            UpsertSlots = upsertSlots,
            DeleteSlots = deleteSlots,
            Modifiers = modifiers.ToWrite(),
        };
    }
}
