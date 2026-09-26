using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.World.Entities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;

namespace Avalon.World.Inventory;

public enum MoveKind
{
    /// <summary>The whole instance goes to an empty slot, as it is.</summary>
    Move,

    /// <summary>A new instance of Count goes to an empty slot; the rest stays.</summary>
    Split,

    /// <summary>Count leaves the source for a stack of the same item.</summary>
    Merge,

    /// <summary>Two different items trade slots.</summary>
    Swap,
}

/// <param name="Count">
/// Move and Swap: the whole source stack. Split: how many go to the new stack at To. Merge: how many
/// leave From for the stack at To.
/// </param>
public readonly record struct MovePlan(MoveKind Kind, SlotRef From, SlotRef To, uint Count);

public readonly record struct MoveDecision(ItemRequestResult Result, MovePlan Plan)
{
    public bool Accepted => Result == ItemRequestResult.Ok;

    internal static MoveDecision Refused(ItemRequestResult result) => new(result, default);

    internal static MoveDecision Accept(MoveKind kind, SlotRef from, SlotRef to, uint count) =>
        new(ItemRequestResult.Ok, new MovePlan(kind, from, to, count));
}

/// <summary>
/// The spec #463 move rules, as a pure function: it reads the character's class, level and
/// containers and the item templates, and changes nothing. The server derives the action from
/// what the two slots hold and the count; it never trusts a label from the client.
/// </summary>
/// <remarks>
/// The checks run in this order, and the first failure answers: the slots exist and differ and
/// are not reserved; the Bank is accessible if either names it; the source holds an item whose
/// template exists; the count is 1 to the stack's size; then the rule for what the target holds.
/// The character being dead is the handler's check, before any of these.
/// </remarks>
public static class InventoryMove
{
    public static MoveDecision Decide(
        CharacterEntity character,
        Func<ItemTemplateId, ItemTemplate?> findTemplate,
        bool bankAccessible,
        SlotRef from,
        SlotRef to,
        uint? count)
    {
        if (!IsUsable(character, from) || !IsUsable(character, to) || from == to)
            return MoveDecision.Refused(ItemRequestResult.InvalidSlot);

        if (!bankAccessible && (from.Container == InventoryType.Bank || to.Container == InventoryType.Bank))
            return MoveDecision.Refused(ItemRequestResult.BankClosed);

        if (!character.Container(from.Container).TryGet(from.Slot, out InventoryItem source)
            || findTemplate(source.TemplateId) is not { } sourceTemplate)
            return MoveDecision.Refused(ItemRequestResult.NotFound);

        uint moving = count ?? source.Count;
        if (moving == 0 || moving > source.Count)
            return MoveDecision.Refused(ItemRequestResult.InvalidCount);

        bool whole = moving == source.Count;
        uint maxStack = MaxStack(sourceTemplate);

        if (!character.Container(to.Container).TryGet(to.Slot, out InventoryItem target))
        {
            if (!whole)
            {
                return to.Container == InventoryType.Equipment || maxStack == 1
                    ? MoveDecision.Refused(ItemRequestResult.NotStackable)
                    : MoveDecision.Accept(MoveKind.Split, from, to, moving);
            }

            ItemRequestResult fits = CanWear(character, sourceTemplate, to);
            if (fits != ItemRequestResult.Ok)
                return MoveDecision.Refused(fits);

            return TwoHandConflict(character, findTemplate, from, to, source, target: null)
                ? MoveDecision.Refused(ItemRequestResult.Blocked)
                : MoveDecision.Accept(MoveKind.Move, from, to, moving);
        }

        if (target.TemplateId == source.TemplateId)
        {
            if (to.Container == InventoryType.Equipment || maxStack == 1)
                return MoveDecision.Refused(ItemRequestResult.NotStackable);

            if (target.Count >= maxStack)
                return MoveDecision.Refused(ItemRequestResult.TargetFull);

            return MoveDecision.Accept(MoveKind.Merge, from, to, Math.Min(moving, maxStack - target.Count));
        }

        if (!whole)
            return MoveDecision.Refused(ItemRequestResult.InvalidCount);

        // A swap: each item is checked against the slot it is going to.
        ItemRequestResult sourceFits = CanWear(character, sourceTemplate, to);
        if (sourceFits != ItemRequestResult.Ok)
            return MoveDecision.Refused(sourceFits);

        ItemRequestResult targetFits = from.Container != InventoryType.Equipment
            ? ItemRequestResult.Ok
            : findTemplate(target.TemplateId) is { } targetTemplate
                ? CanWear(character, targetTemplate, from)
                : ItemRequestResult.WrongEquipSlot;
        if (targetFits != ItemRequestResult.Ok)
            return MoveDecision.Refused(targetFits);

        return TwoHandConflict(character, findTemplate, from, to, source, target)
            ? MoveDecision.Refused(ItemRequestResult.Blocked)
            : MoveDecision.Accept(MoveKind.Swap, from, to, moving);
    }

    /// <summary>
    /// Destroy: a count below the stack reduces it. <paramref name="destroying" /> is how many go,
    /// and 0 whenever the result is not Ok.
    /// </summary>
    public static ItemRequestResult DecideDestroy(
        CharacterEntity character,
        Func<ItemTemplateId, ItemTemplate?> findTemplate,
        bool bankAccessible,
        SlotRef slot,
        uint? count,
        out uint destroying)
    {
        destroying = 0;

        if (!IsUsable(character, slot))
            return ItemRequestResult.InvalidSlot;

        if (!bankAccessible && slot.Container == InventoryType.Bank)
            return ItemRequestResult.BankClosed;

        if (!character.Container(slot.Container).TryGet(slot.Slot, out InventoryItem item)
            || findTemplate(item.TemplateId) is not { } template)
            return ItemRequestResult.NotFound;

        uint amount = count ?? item.Count;
        if (amount == 0 || amount > item.Count)
            return ItemRequestResult.InvalidCount;

        if ((template.Flags & ItemTemplateFlags.NoDestroy) != 0)
            return ItemRequestResult.CannotDestroy;

        destroying = amount;
        return ItemRequestResult.Ok;
    }

    /// <summary>The slot exists in its container and is not a reserved equipment slot.</summary>
    public static bool IsUsable(CharacterEntity character, SlotRef slot) =>
        slot.Slot < character.Container(slot.Container).Capacity
        && !(slot.Container == InventoryType.Equipment && EquipmentSlots.IsReserved(slot.Slot));

    /// <summary>A MaxStackSize of 0 is read as 1, as CharacterInventoryService reads it.</summary>
    public static uint MaxStack(ItemTemplate template) => Math.Max(1u, template.MaxStackSize);

    /// <summary>Whether an item may go to <paramref name="destination" />. Anything may go to the Bag or the Bank.</summary>
    private static ItemRequestResult CanWear(CharacterEntity character, ItemTemplate template, SlotRef destination)
    {
        if (destination.Container != InventoryType.Equipment)
            return ItemRequestResult.Ok;

        if (!EquipmentSlots.Accepts(destination.Slot, template.Slot))
            return ItemRequestResult.WrongEquipSlot;

        if (template.RequiredLevel is { } required && character.Level < required)
            return ItemRequestResult.LevelTooLow;

        if (!template.AllowedClasses.Contains(character.Class))
            return ItemRequestResult.WrongClass;

        return ItemRequestResult.Ok;
    }

    /// <summary>
    /// True when, after the move, a two-handed weapon would be in the main hand while the off hand
    /// holds anything. Asked only of a move that changes one of the two hands, so a conflict an old
    /// row loaded does not freeze the rest of the equipment, and taking the off hand off is allowed.
    /// </summary>
    private static bool TwoHandConflict(
        CharacterEntity character,
        Func<ItemTemplateId, ItemTemplate?> findTemplate,
        SlotRef from,
        SlotRef to,
        InventoryItem source,
        InventoryItem? target)
    {
        var mainHand = new SlotRef(InventoryType.Equipment, EquipmentSlots.MainHand);
        var offHand = new SlotRef(InventoryType.Equipment, EquipmentSlots.OffHand);

        if (from != mainHand && from != offHand && to != mainHand && to != offHand)
            return false;

        InventoryItem? mainAfter = HeldAfter(character, mainHand, from, to, source, target);
        InventoryItem? offAfter = HeldAfter(character, offHand, from, to, source, target);

        return mainAfter is { } main
               && offAfter is not null
               && findTemplate(main.TemplateId) is { SubClass: ItemSubClass.TwoHanded };
    }

    /// <summary>What <paramref name="slot" /> holds once a Move or Swap from <paramref name="from" /> to <paramref name="to" /> is applied.</summary>
    private static InventoryItem? HeldAfter(
        CharacterEntity character, SlotRef slot, SlotRef from, SlotRef to, InventoryItem source, InventoryItem? target)
    {
        if (slot == to)
            return source;

        if (slot == from)
            return target;

        return character.Container(slot.Container).TryGet(slot.Slot, out InventoryItem held) ? held : null;
    }
}
