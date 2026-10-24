using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.World.Entities;
using Avalon.World.Loot;
using Avalon.World.Public.Characters;

namespace Avalon.World.Inventory;

/// <summary>
/// <see cref="IInventoryService" /> over one character's containers. The one writer of a
/// character's items: every change it makes marks <see cref="CharacterEntity.SaveState" /> and
/// <see cref="CharacterEntity.ClientChanges" />, and it alone writes <see cref="CharacterEntity.ItemModifiers" /> after
/// select (#1179), beside the container, so an item's entry lives exactly as long as the item is held. A stack offered
/// in a trade (<see cref="CharacterEntity.Locks" />, #1175) is left alone: adds never fill it, removals by template
/// never see it, and removing it by id is Locked. While a trade completes, every add but its own
/// (<see cref="TryAddInstanceAt" />) treats the Bag slots it reserved as taken and counts the copies it is about to add
/// toward Unique. Every new copy of a template that links modifiers rolls them as it is made (#1179, B-R12), through
/// <paramref name="roller" /> over the catalog <paramref name="currentCatalog" /> answers at that moment.
/// </summary>
public sealed class CharacterInventoryService(
    CharacterEntity owner,
    Func<ItemTemplateId, ItemTemplate?> findTemplate,
    IItemIdAllocator itemIds,
    IItemRoller roller,
    Func<ItemModifierCatalog> currentCatalog) : IInventoryService
{
    /// <summary>Where an instance id is looked for, in order.</summary>
    private static readonly InventoryType[] s_allContainers = [InventoryType.Bag, InventoryType.Equipment, InventoryType.Bank];

    private CharacterInventoryContainer Bag => owner.Container(InventoryType.Bag);

    public InventoryAddResult CanAdd(ItemTemplateId templateId, uint count) => Check(templateId, count, currentCatalog(), out _);

    public InventoryAddResult TryAdd(ItemTemplateId templateId, uint count)
    {
        ItemModifierCatalog catalog = currentCatalog();
        InventoryAddResult result = Check(templateId, count, catalog, out ItemTemplate? template);
        if (result != InventoryAddResult.Ok || count == 0)
            return result;

        bool rolls = Rolls(templateId, catalog);
        uint maxStack = MaxStack(template!, rolls);
        uint remaining = count;
        CharacterInventoryContainer bag = Bag;

        // Existing stacks first, in slot order, so a new stack is never started while one of the
        // same item still has room. Both walks go by slot number over the Bag as it is: a step writes
        // only the slot it stands on, so the slots still to come are as they were when the walk began,
        // which is what the lists copied before each walk used to hold (#883).
        for (byte slot = 0; slot < bag.Capacity; slot++)
        {
            if (!bag.TryGet(slot, out InventoryItem stack) || !IsFillable(stack, templateId, maxStack))
                continue;

            uint take = Math.Min(maxStack - stack.Count, remaining);
            Replace(InventoryType.Bag, stack with { Count = stack.Count + take });
            remaining -= take;
            if (remaining == 0)
                return InventoryAddResult.Ok;
        }

        for (byte slot = 0; slot < bag.Capacity; slot++)
        {
            if (bag.TryGet(slot, out _) || owner.Locks.IsReserved(slot))
                continue;

            uint take = Math.Min(maxStack, remaining);
            if (rolls)
            {
                CreateRolled(slot, template!, catalog);
            }
            else
            {
                Create(InventoryType.Bag, new InventoryItem(slot, itemIds.Next(), templateId, take,
                    ItemInstanceDefaults.InitialDurability(template!), ItemInstanceFlags.None, template!.Rarity,
                    ItemInstanceDefaults.InitialItemLevel(template)));
            }

            remaining -= take;
            if (remaining == 0)
                return InventoryAddResult.Ok;
        }

        // Check measured this same state on this same thread a moment ago.
        throw new InvalidOperationException(
            $"Adding {count} of item template {templateId} ran out of the room Check counted.");
    }

    public InventoryRemoveResult TryRemove(ItemTemplateId templateId, uint count)
    {
        CharacterInventoryContainer bag = Bag;
        bool found = false;
        long held = 0;
        foreach (InventoryItem item in bag.Held)
        {
            if (item.TemplateId != templateId || IsLocked(item))
                continue;

            found = true;
            held += item.Count;
        }

        if (!found)
            return InventoryRemoveResult.NotFound;

        if (held < count)
            return InventoryRemoveResult.NotEnough;

        // From the highest slot down, by slot number over the Bag as it is: a step empties or shrinks
        // only the slot it stands on, so no list of the stacks is taken first (#883).
        uint remaining = count;
        for (int slot = bag.Capacity - 1; slot >= 0 && remaining > 0; slot--)
        {
            if (!bag.TryGet((byte)slot, out InventoryItem stack) || stack.TemplateId != templateId || IsLocked(stack))
                continue;

            uint take = Math.Min(stack.Count, remaining);
            Take(InventoryType.Bag, stack, take);
            remaining -= take;
        }

        return InventoryRemoveResult.Ok;
    }

    public InventoryRemoveResult TryRemove(ItemInstanceId itemInstanceId, uint count)
    {
        foreach (InventoryType container in s_allContainers)
        {
            foreach (InventoryItem item in owner.Container(container).Held)
            {
                if (item.InstanceId != itemInstanceId)
                    continue;

                if (IsLocked(item))
                    return InventoryRemoveResult.Locked;

                if (item.Count < count)
                    return InventoryRemoveResult.NotEnough;

                Take(container, item, count);
                return InventoryRemoveResult.Ok;
            }
        }

        return InventoryRemoveResult.NotFound;
    }

    public ItemRequestResult TryMove(SlotRef from, SlotRef to, uint? count, bool bankAccessible)
    {
        MoveDecision decision = InventoryMove.Decide(owner, findTemplate, bankAccessible, from, to, count);
        if (!decision.Accepted)
            return decision.Result;

        Apply(decision.Plan);
        return ItemRequestResult.Ok;
    }

    public ItemRequestResult TryDestroy(SlotRef slot, uint? count, bool bankAccessible)
    {
        ItemRequestResult result =
            InventoryMove.DecideDestroy(owner, findTemplate, bankAccessible, slot, count, out uint destroying);
        if (result != ItemRequestResult.Ok)
            return result;

        owner.Container(slot.Container).TryGet(slot.Slot, out InventoryItem item);
        Take(slot.Container, item, destroying);
        return ItemRequestResult.Ok;
    }

    public CarriedItem TakeOut(SlotRef slot, uint count)
    {
        if (count == 0
            || !owner.Container(slot.Container).TryGet(slot.Slot, out InventoryItem item)
            || item.Count < count)
        {
            throw new InvalidOperationException(
                $"Cannot take {count} out of {slot}: VendorRules checks the slot and the count first.");
        }

        if (count == item.Count)
            return new CarriedItem(item, Delete(slot.Container, item));

        // #1179: an item carrying modifiers is one of a kind and never splits, as the move rules refuse it; a part of it
        // would be a copy without them.
        if (owner.ItemModifiers.Of(item.InstanceId).Count > 0)
        {
            throw new InvalidOperationException(
                $"Cannot take {count} of {item.Count} out of {slot}: an item carrying modifiers is taken out whole.");
        }

        Replace(slot.Container, item with { Count = item.Count - count });
        return new CarriedItem(item with { InstanceId = itemIds.Next(), Count = count }, []);
    }

    public InventoryAddResult TryAddInstance(CarriedItem carried)
    {
        InventoryItem item = carried.Item;
        ThrowIfHeld(item);

        CharacterInventoryContainer bag = Bag;
        byte? lowestFree = null;
        for (byte free = 0; free < bag.Capacity; free++)
        {
            if (owner.Locks.IsFree(bag, free))
            {
                lowestFree = free;
                break;
            }
        }

        if (lowestFree is not { } slot)
            return InventoryAddResult.InventoryFull;

        // Its own unique guard (#432), not only the caller's: two copies of a unique item must
        // never exist. A template that is gone cannot say it is unique, and the item is still the
        // player's own.
        if (IsUnique(item.TemplateId) && OwnedOrIncoming(item.TemplateId) + item.Count > 1)
            return InventoryAddResult.UniqueAlreadyOwned;

        Create(InventoryType.Bag, item with { Slot = slot }, carried.Modifiers);

        // Its rows may be gone (a sale saved before the buyback deleted them, modifiers and all), so the save that
        // upserts it writes its modifiers again.
        if (carried.Modifiers.Count > 0)
            owner.SaveState.ItemModifiersChanged(item.InstanceId);

        return InventoryAddResult.Ok;
    }

    public InventoryAddResult TryAddInstanceAt(CarriedItem carried, byte bagSlot)
    {
        InventoryItem item = carried.Item;
        ThrowIfHeld(item);

        CharacterInventoryContainer bag = Bag;
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bagSlot, bag.Capacity);

        // The trade's own add: its reservations are what it is filling, so only an occupied slot refuses, and only
        // what the character already holds counts toward Unique.
        if (bag.TryGet(bagSlot, out _))
            return InventoryAddResult.InventoryFull;

        if (IsUnique(item.TemplateId) && OwnedCount(item.TemplateId) + item.Count > 1)
            return InventoryAddResult.UniqueAlreadyOwned;

        // Its modifiers unmarked: the paired save wrote them as this character's.
        Create(InventoryType.Bag, item with { Slot = bagSlot }, carried.Modifiers);
        return InventoryAddResult.Ok;
    }

    /// <summary>Adding an instance the character already holds would duplicate it; callers only add one that left.</summary>
    private void ThrowIfHeld(InventoryItem item)
    {
        foreach (InventoryType container in s_allContainers)
        {
            foreach (InventoryItem held in owner.Container(container).Held)
            {
                if (held.InstanceId == item.InstanceId)
                    throw new InvalidOperationException($"Item {item.InstanceId} is already held; it cannot be added twice.");
            }
        }
    }

    /// <summary>A template that is gone cannot say it is unique.</summary>
    private bool IsUnique(ItemTemplateId templateId) =>
        findTemplate(templateId) is { } template && (template.Flags & ItemTemplateFlags.Unique) != 0;

    /// <summary>
    /// Applies a plan InventoryMove accepted a moment ago, on this thread, against this state, so
    /// nothing here can fail part way: every slot it names was checked to exist and hold what the
    /// plan says.
    /// </summary>
    private void Apply(MovePlan plan)
    {
        owner.Container(plan.From.Container).TryGet(plan.From.Slot, out InventoryItem source);

        switch (plan.Kind)
        {
            case MoveKind.Move:
                Vacate(plan.From);
                Occupy(plan.To, source with { Slot = plan.To.Slot }, occupiedBefore: false);
                break;

            case MoveKind.Swap:
                owner.Container(plan.To.Container).TryGet(plan.To.Slot, out InventoryItem target);
                Occupy(plan.To, source with { Slot = plan.To.Slot }, occupiedBefore: true);
                Occupy(plan.From, target with { Slot = plan.From.Slot }, occupiedBefore: true);
                break;

            case MoveKind.Split:
                // The new stack is a copy of the old one's durability, flags and charges.
                Replace(plan.From.Container, source with { Count = source.Count - plan.Count });
                Create(plan.To.Container,
                    source with { Slot = plan.To.Slot, InstanceId = itemIds.Next(), Count = plan.Count });
                break;

            case MoveKind.Merge:
                owner.Container(plan.To.Container).TryGet(plan.To.Slot, out InventoryItem stack);
                Replace(plan.To.Container, stack with { Count = stack.Count + plan.Count });
                Take(plan.From.Container, source, plan.Count);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(plan), plan.Kind, "Unknown move kind");
        }
    }

    /// <summary>The instance leaves the slot but still exists, so only the slot row changes.</summary>
    private void Vacate(SlotRef slot)
    {
        owner.Container(slot.Container).Remove(slot.Slot);
        owner.SaveState.SlotChanged(slot.Container, slot.Slot, occupiedBefore: true, occupiedAfter: false);
        owner.ClientChanges.RecordSlot(slot.Container, slot.Slot);
    }

    /// <summary>An existing instance arrives in the slot, so only the slot row changes.</summary>
    private void Occupy(SlotRef slot, InventoryItem item, bool occupiedBefore)
    {
        owner.Container(slot.Container).Put(item);
        owner.SaveState.SlotChanged(slot.Container, slot.Slot, occupiedBefore, occupiedAfter: true);
        owner.ClientChanges.RecordSlot(slot.Container, slot.Slot);
    }

    private InventoryAddResult Check(ItemTemplateId templateId, uint count, ItemModifierCatalog catalog, out ItemTemplate? template)
    {
        template = findTemplate(templateId);
        if (template is null)
            return InventoryAddResult.UnknownTemplate;

        if (count == 0)
            return InventoryAddResult.Ok;

        // Unique caps ownership at one copy, anywhere the character keeps items. A mask test: HasFlag boxes both
        // of its operands in a build that does not optimise, 48 B for every add (#883).
        if ((template.Flags & ItemTemplateFlags.Unique) != 0 && OwnedOrIncoming(templateId) + count > 1)
            return InventoryAddResult.UniqueAlreadyOwned;

        // In long: 30 slots of a uint.MaxValue stack still fit, and nothing here can wrap. A slot a completing trade
        // reserved (#1175) is no room.
        uint maxStack = MaxStack(template, Rolls(templateId, catalog));
        CharacterInventoryContainer bag = Bag;
        long room = (long)owner.Locks.FreeSlotCount(bag) * maxStack;
        foreach (InventoryItem item in bag.Held)
        {
            if (IsFillable(item, templateId, maxStack))
                room += maxStack - item.Count;
        }

        return room >= count ? InventoryAddResult.Ok : InventoryAddResult.InventoryFull;
    }

    private long OwnedCount(ItemTemplateId templateId)
    {
        long owned = 0;
        foreach (InventoryType container in s_allContainers)
        {
            foreach (InventoryItem item in owner.Container(container).Held)
            {
                if (item.TemplateId == templateId)
                    owned += item.Count;
            }
        }

        return owned;
    }

    /// <summary>What the character holds plus what a completing trade is about to add (#1175): every add but the trade's own.</summary>
    private long OwnedOrIncoming(ItemTemplateId templateId) =>
        OwnedCount(templateId) + (owner.Locks.Any ? owner.Locks.IncomingCount(templateId) : 0);

    /// <summary>
    /// A stack of the item below the maximum, not offered in a trade, and carrying no modifiers (#1179): an item with
    /// them is one of a kind. One already above the maximum (a lowered template) is left alone.
    /// </summary>
    private bool IsFillable(InventoryItem item, ItemTemplateId templateId, uint maxStack) =>
        item.TemplateId == templateId && item.Count < maxStack && !IsLocked(item)
        && owner.ItemModifiers.Of(item.InstanceId).Count == 0;

    /// <summary>Offered in a trade (#1175). One bool read while nothing is.</summary>
    private bool IsLocked(InventoryItem item) => owner.Locks.IsLocked(item.InstanceId);

    /// <summary>
    /// A MaxStackSize of 0 is read as 1: every item takes at least a slot of its own. A copy that rolls modifiers is one
    /// of a kind (#1179), so each takes a slot of its own whatever the template says.
    /// </summary>
    private static uint MaxStack(ItemTemplate template, bool rolls) => rolls ? 1u : InventoryMove.MaxStack(template);

    /// <summary>Whether a new copy of the template rolls modifiers (#1179): it links some in the catalog.</summary>
    private static bool Rolls(ItemTemplateId templateId, ItemModifierCatalog catalog) => catalog.LinkedTo(templateId).Count > 0;

    /// <summary>
    /// One new copy of a template that links modifiers, rolled now (#1179, B-R12): a plain copy at its template's item
    /// level, in the slot, with its modifiers in the store and marked, so the save that inserts it writes them.
    /// </summary>
    private void CreateRolled(byte slot, ItemTemplate template, ItemModifierCatalog catalog)
    {
        RolledItem rolled = roller.RollPlain(template, ItemInstanceDefaults.InitialItemLevel(template), catalog);
        Create(InventoryType.Bag, new InventoryItem(slot, rolled.InstanceId, rolled.TemplateId, 1,
            ItemInstanceDefaults.InitialDurability(template), ItemInstanceFlags.None, rolled.Rarity, rolled.ItemLevel));
        owner.ItemModifiers.PutMade(rolled.InstanceId, rolled.Modifiers);
        owner.SaveState.ItemModifiersChanged(rolled.InstanceId);
    }

    private void Take(InventoryType container, InventoryItem item, uint count)
    {
        if (count == 0)
            return;

        if (count == item.Count)
            Delete(container, item);
        else
            Replace(container, item with { Count = item.Count - count });
    }

    /// <summary>A new instance, or one arriving, in the slot: <paramref name="modifiers" /> enter the store with it.</summary>
    private void Create(InventoryType container, InventoryItem item, IReadOnlyList<ItemInstanceModifier>? modifiers = null)
    {
        owner.Container(container).Put(item);
        if (modifiers is not null)
            owner.ItemModifiers.Put(item.InstanceId, modifiers);

        owner.SaveState.ItemCreated(item.InstanceId);
        owner.SaveState.SlotChanged(container, item.Slot, occupiedBefore: false, occupiedAfter: true);
        owner.ClientChanges.RecordSlot(container, item.Slot);
    }

    /// <summary>Same instance, same slot, new values. The slot row (which item where) is unchanged.</summary>
    private void Replace(InventoryType container, InventoryItem item)
    {
        owner.Container(container).Put(item);
        owner.SaveState.ItemChanged(item.InstanceId);
        owner.ClientChanges.RecordSlot(container, item.Slot);
    }

    /// <summary>The instance leaves the character: its modifiers leave the store with it, and are returned.</summary>
    private IReadOnlyList<ItemInstanceModifier> Delete(InventoryType container, InventoryItem item)
    {
        owner.Container(container).Remove(item.Slot);
        owner.SaveState.ItemRemoved(item.InstanceId);
        owner.SaveState.SlotChanged(container, item.Slot, occupiedBefore: true, occupiedAfter: false);
        owner.ClientChanges.RecordSlot(container, item.Slot);
        return owner.ItemModifiers.Take(item.InstanceId);
    }
}
