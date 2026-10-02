using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Items;

/// <summary>
/// A use of gear (item use, 2026-10-02): the item goes to the slot its type is worn in, through the same
/// InventoryMove rules and IInventoryService apply as a drag, so the save and client marks are the drag's. An occupied
/// slot swaps its item into the clicked Bag slot; a ring takes the empty finger, else the first; a two-hander first
/// moves a worn off-hand item to the lowest Bag slot free before the use (TargetFull when there is none).
/// </summary>
public static class ItemEquip
{
    public static bool IsWearable(ItemTemplate template) => EquipmentSlots.SlotsFor(template.Slot).Count > 0;

    /// <param name="bagSlot">The clicked Bag slot; the caller has checked it holds an item of <paramref name="template" />.</param>
    public static ItemUseResult Equip(
        CharacterEntity character, IInventoryService inventory, ushort bagSlot, ItemTemplate template, ILogger logger)
    {
        IReadOnlyList<ushort> slots = EquipmentSlots.SlotsFor(template.Slot);
        CharacterInventoryContainer equipment = character.Container(InventoryType.Equipment);
        CharacterInventoryContainer bag = character.Container(InventoryType.Bag);

        ushort target = slots[0];
        foreach (ushort slot in slots)
        {
            if (!equipment.TryGet(slot, out _))
            {
                target = slot;
                break;
            }
        }

        var from = new SlotRef(InventoryType.Bag, bagSlot);
        var to = new SlotRef(InventoryType.Equipment, target);

        if (template.SubClass == ItemSubClass.TwoHanded && target == EquipmentSlots.MainHand
            && equipment.TryGet(EquipmentSlots.OffHand, out _))
        {
            if (!bag.TryGet(bagSlot, out InventoryItem item))
                return ItemUseResult.NotFound;

            // Asked first, so a refusal leaves the off-hand item where it is.
            ItemRequestResult wearable = InventoryMove.CanWear(character, template, item, to);
            if (wearable != ItemRequestResult.Ok)
                return Map(wearable);

            ushort? free = null;
            foreach (ushort slot in bag.FreeSlots())
            {
                free = slot;
                break;
            }

            if (free is null)
                return ItemUseResult.TargetFull;

            ItemRequestResult offHand = inventory.TryMove(
                new SlotRef(InventoryType.Equipment, EquipmentSlots.OffHand), new SlotRef(InventoryType.Bag, free.Value),
                count: null, bankAccessible: false);
            if (offHand != ItemRequestResult.Ok)
                return Map(offHand);

            ItemRequestResult worn = inventory.TryMove(from, to, count: null, bankAccessible: false);
            if (worn != ItemRequestResult.Ok)
            {
                // Not rolled back (the VendorTrade stance): the off-hand item already sits in the Bag, which is safe.
                logger.LogError("Equipping two-hander {Item} from Bag slot {Slot} failed with {Result} after its off-hand item moved",
                    template.Id.Value, bagSlot, worn);
            }

            return Map(worn);
        }

        return Map(inventory.TryMove(from, to, count: null, bankAccessible: false));
    }

    /// <summary>A move rule's answer as a use's (owner decision 6).</summary>
    public static ItemUseResult Map(ItemRequestResult result) => result switch
    {
        ItemRequestResult.Ok => ItemUseResult.Ok,
        ItemRequestResult.NotFound or ItemRequestResult.InvalidSlot => ItemUseResult.NotFound,
        ItemRequestResult.WrongEquipSlot => ItemUseResult.WrongEquipSlot,
        ItemRequestResult.LevelTooLow => ItemUseResult.LevelTooLow,
        ItemRequestResult.WrongClass => ItemUseResult.WrongClass,
        ItemRequestResult.NotStackable => ItemUseResult.NotUsable,
        ItemRequestResult.TargetFull => ItemUseResult.TargetFull,
        ItemRequestResult.Blocked => ItemUseResult.Blocked,
        ItemRequestResult.Dead => ItemUseResult.Dead,
        _ => ItemUseResult.InternalError,
    };
}
