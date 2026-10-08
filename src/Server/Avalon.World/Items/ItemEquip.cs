using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Items;

/// <summary>
/// A use of gear (item use): the item goes to the slot its type is worn in, through the same InventoryMove rules and
/// IInventoryService apply as a drag, so the save and client marks are the drag's. An occupied slot swaps its item into
/// the clicked Bag slot; a ring takes the empty finger, else the first; a two-hander first moves a worn off-hand item to
/// the lowest Bag slot free before the use (TargetFull when there is none). A use of a worn item takes it off to the
/// lowest free Bag slot (<see cref="Unequip" />). Every check runs before anything moves, and <c>beforeApply</c> runs once
/// they have all passed, just before the first move, so a refused equip or unequip never calls it.
/// </summary>
public static class ItemEquip
{
    public static bool IsWearable(ItemTemplate template) => EquipmentSlots.SlotsFor(template.Slot).Count > 0;

    /// <param name="character">The user.</param>
    /// <param name="inventory">The user's inventory service, which applies the moves.</param>
    /// <param name="findTemplate">The item templates, as the inventory service reads them.</param>
    /// <param name="bagSlot">The clicked Bag slot; the caller has checked it holds an item of <paramref name="template" />.</param>
    /// <param name="template">The clicked item's template.</param>
    /// <param name="logger">Where a failure after the off-hand item moved is logged.</param>
    /// <param name="beforeApply">Run once the equip has passed every check, before anything moves.</param>
    public static ItemUseResult Equip(
        CharacterEntity character,
        IInventoryService inventory,
        Func<ItemTemplateId, ItemTemplate?> findTemplate,
        ushort bagSlot,
        ItemTemplate template,
        ILogger logger,
        Action? beforeApply = null)
    {
        IReadOnlyList<ushort> slots = EquipmentSlots.SlotsFor(template.Slot);
        CharacterInventoryContainer equipment = character.Container(InventoryType.Equipment);

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
            return EquipTwoHander(character, inventory, bagSlot, template, from, to, logger, beforeApply);
        }

        // Decided first, as TryMove decides it, so a refusal runs nothing before it.
        MoveDecision decision = InventoryMove.Decide(character, findTemplate, bankAccessible: false, from, to, count: null);
        if (!decision.Accepted)
            return Map(decision.Result);

        beforeApply?.Invoke();
        return Map(inventory.TryMove(from, to, count: null, bankAccessible: false));
    }

    /// <summary>
    /// A use of a worn item: it goes to the lowest free Bag slot (TargetFull when there is none, and nothing moves), through
    /// the drag's InventoryMove rules and IInventoryService. Taking an item off is always allowed, either hand and an item
    /// whose template is gone included (the move rules read a missing template as an item that stacks to 1).
    /// </summary>
    /// <param name="character">The user.</param>
    /// <param name="inventory">The user's inventory service, which applies the move.</param>
    /// <param name="findTemplate">The item templates, as the inventory service reads them.</param>
    /// <param name="equipmentSlot">The clicked Equipment slot; the caller has checked it is usable and holds an item.</param>
    /// <param name="beforeApply">Run once the move has passed every check, just before it applies.</param>
    public static ItemUseResult Unequip(
        CharacterEntity character,
        IInventoryService inventory,
        Func<ItemTemplateId, ItemTemplate?> findTemplate,
        ushort equipmentSlot,
        Action? beforeApply = null)
    {
        if (LowestFreeSlot(character.Container(InventoryType.Bag)) is not { } free)
            return ItemUseResult.TargetFull;

        var from = new SlotRef(InventoryType.Equipment, equipmentSlot);
        var to = new SlotRef(InventoryType.Bag, free);

        // Decided first, as TryMove decides it, so a refusal runs nothing before it.
        MoveDecision decision = InventoryMove.Decide(character, findTemplate, bankAccessible: false, from, to, count: null);
        if (!decision.Accepted)
            return Map(decision.Result);

        beforeApply?.Invoke();
        return Map(inventory.TryMove(from, to, count: null, bankAccessible: false));
    }

    /// <summary>
    /// A move rule's answer as a use's: a missing item or slot is NotFound, a stack of gear NotUsable, the wearing
    /// refusals by name, and the answers a use never meets (a closed bank, a bad count, a destroy) InternalError.
    /// </summary>
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

    /// <summary>A two-hander with an off-hand item worn: the off-hand item goes to the lowest free Bag slot first.</summary>
    private static ItemUseResult EquipTwoHander(
        CharacterEntity character,
        IInventoryService inventory,
        ushort bagSlot,
        ItemTemplate template,
        SlotRef from,
        SlotRef to,
        ILogger logger,
        Action? beforeApply)
    {
        CharacterInventoryContainer bag = character.Container(InventoryType.Bag);
        if (!bag.TryGet(bagSlot, out InventoryItem item))
            return ItemUseResult.NotFound;

        // Asked first, so a refusal leaves the off-hand item where it is.
        ItemRequestResult wearable = InventoryMove.CanWear(character, template, item, to);
        if (wearable != ItemRequestResult.Ok)
            return Map(wearable);

        if (LowestFreeSlot(bag) is not { } free)
            return ItemUseResult.TargetFull;

        beforeApply?.Invoke();

        ItemRequestResult offHand = inventory.TryMove(
            new SlotRef(InventoryType.Equipment, EquipmentSlots.OffHand), new SlotRef(InventoryType.Bag, free),
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

    /// <summary>The lowest empty slot, or null when the container is full; a plain loop, so a use allocates no iterator.</summary>
    private static ushort? LowestFreeSlot(CharacterInventoryContainer container)
    {
        for (ushort slot = 0; slot < container.Capacity; slot++)
        {
            if (!container.TryGet(slot, out _))
                return slot;
        }

        return null;
    }
}
