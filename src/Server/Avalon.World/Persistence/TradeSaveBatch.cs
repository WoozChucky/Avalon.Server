using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
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
    /// Each outgoing item is deleted (and no longer upserted), and its Bag slot deleted unless an incoming item lands
    /// there. Each incoming item is upserted as this character's, stamped <paramref name="now" />, with a slot row at its
    /// destination. The row is a new copy with the gold given and received. Each item id and each slot key appears at
    /// most once, as the repository requires.
    /// </summary>
    /// <exception cref="OverflowException">The side gives more gold than the row holds: the plan was not checked.</exception>
    public static CharacterSaveBatch Apply(CharacterSaveBatch batch, TradeSidePlan side, DateTime now)
    {
        Character row = batch.Row.Copy();
        row.Gold = checked(row.Gold - side.GoldOut + side.GoldIn);

        HashSet<ItemInstanceId> touchedItems = [];
        HashSet<(InventoryType Container, byte Slot)> touchedSlots = [];
        foreach (InventoryItem item in side.Outgoing)
        {
            touchedItems.Add(item.InstanceId);
            touchedSlots.Add((InventoryType.Bag, item.Slot));
        }

        foreach (InventoryItem item in side.Incoming)
        {
            touchedItems.Add(item.InstanceId);
            touchedSlots.Add((InventoryType.Bag, item.Slot));
        }

        // Whatever the snapshot said about a key the trade touches is replaced by what the trade makes of it.
        List<ItemInstance> upsertItems = [.. batch.UpsertItems.Where(i => !touchedItems.Contains(i.Id))];
        List<ItemInstanceId> deleteItems = [.. batch.DeleteItems.Where(id => !touchedItems.Contains(id))];
        List<CharacterInventory> upsertSlots = [.. batch.UpsertSlots.Where(s => !touchedSlots.Contains((s.Container, s.Slot)))];
        List<(InventoryType Container, byte Slot)> deleteSlots = [.. batch.DeleteSlots.Where(k => !touchedSlots.Contains(k))];

        HashSet<byte> landing = [];
        foreach (InventoryItem item in side.Incoming)
        {
            landing.Add(item.Slot);
            upsertItems.Add(new ItemInstance
            {
                Id = item.InstanceId,
                TemplateId = item.TemplateId,
                CharacterId = row.Id,
                Count = item.Count,
                Durability = item.Durability,
                Charges = item.Charges,
                Flags = item.Flags,
                Rarity = item.Rarity,
                ItemLevel = item.ItemLevel,
                UpdatedAt = now,
            });
            upsertSlots.Add(new CharacterInventory
            {
                CharacterId = row.Id,
                Container = InventoryType.Bag,
                Slot = item.Slot,
                ItemId = item.InstanceId,
            });
        }

        foreach (InventoryItem item in side.Outgoing)
        {
            deleteItems.Add(item.InstanceId);
            if (!landing.Contains(item.Slot))
                deleteSlots.Add((InventoryType.Bag, item.Slot));
        }

        return batch with
        {
            Row = row,
            UpsertItems = upsertItems,
            DeleteItems = deleteItems,
            UpsertSlots = upsertSlots,
            DeleteSlots = deleteSlots,
        };
    }
}
