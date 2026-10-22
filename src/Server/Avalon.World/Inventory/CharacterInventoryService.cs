using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.World.Entities;
using Avalon.World.Public.Characters;

namespace Avalon.World.Inventory;

/// <summary>
/// <see cref="IInventoryService" /> over one character's containers. The one writer of a
/// character's items: every change it makes marks <see cref="CharacterEntity.SaveState" /> and
/// <see cref="CharacterEntity.ClientChanges" />. A stack offered in a trade (<see cref="CharacterEntity.Locks" />, #1175)
/// is left alone: adds never fill it, removals by template never see it, and removing it by id is Locked.
/// </summary>
public sealed class CharacterInventoryService(
    CharacterEntity owner,
    Func<ItemTemplateId, ItemTemplate?> findTemplate,
    IItemIdAllocator itemIds) : IInventoryService
{
    /// <summary>Where an instance id is looked for, in order.</summary>
    private static readonly InventoryType[] s_allContainers = [InventoryType.Bag, InventoryType.Equipment, InventoryType.Bank];

    private CharacterInventoryContainer Bag => owner.Container(InventoryType.Bag);

    public InventoryAddResult CanAdd(ItemTemplateId templateId, uint count) => Check(templateId, count, out _);

    public InventoryAddResult TryAdd(ItemTemplateId templateId, uint count)
    {
        InventoryAddResult result = Check(templateId, count, out ItemTemplate? template);
        if (result != InventoryAddResult.Ok || count == 0)
            return result;

        uint maxStack = MaxStack(template!);
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
            if (bag.TryGet(slot, out _))
                continue;

            uint take = Math.Min(maxStack, remaining);
            Create(InventoryType.Bag, new InventoryItem(slot, itemIds.Next(), templateId, take,
                ItemInstanceDefaults.InitialDurability(template!), ItemInstanceFlags.None));
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

    public InventoryItem TakeOut(SlotRef slot, uint count)
    {
        if (count == 0
            || !owner.Container(slot.Container).TryGet(slot.Slot, out InventoryItem item)
            || item.Count < count)
        {
            throw new InvalidOperationException(
                $"Cannot take {count} out of {slot}: VendorRules checks the slot and the count first.");
        }

        if (count == item.Count)
        {
            Delete(slot.Container, item);
            return item;
        }

        Replace(slot.Container, item with { Count = item.Count - count });
        return item with { InstanceId = itemIds.Next(), Count = count };
    }

    public InventoryAddResult TryAddInstance(InventoryItem item)
    {
        foreach (InventoryType container in s_allContainers)
        {
            foreach (InventoryItem held in owner.Container(container).Held)
            {
                if (held.InstanceId == item.InstanceId)
                    throw new InvalidOperationException($"Item {item.InstanceId} is already held; it cannot be added twice.");
            }
        }

        CharacterInventoryContainer bag = Bag;
        byte? lowestFree = null;
        for (byte free = 0; free < bag.Capacity; free++)
        {
            if (!bag.TryGet(free, out _))
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
        if (findTemplate(item.TemplateId) is { } template
            && (template.Flags & ItemTemplateFlags.Unique) != 0
            && OwnedCount(item.TemplateId) + item.Count > 1)
        {
            return InventoryAddResult.UniqueAlreadyOwned;
        }

        Create(InventoryType.Bag, item with { Slot = slot });
        return InventoryAddResult.Ok;
    }

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

    private InventoryAddResult Check(ItemTemplateId templateId, uint count, out ItemTemplate? template)
    {
        template = findTemplate(templateId);
        if (template is null)
            return InventoryAddResult.UnknownTemplate;

        if (count == 0)
            return InventoryAddResult.Ok;

        // Unique caps ownership at one copy, anywhere the character keeps items. A mask test: HasFlag boxes both
        // of its operands in a build that does not optimise, 48 B for every add (#883).
        if ((template.Flags & ItemTemplateFlags.Unique) != 0 && OwnedCount(templateId) + count > 1)
            return InventoryAddResult.UniqueAlreadyOwned;

        // In long: 30 slots of a uint.MaxValue stack still fit, and nothing here can wrap.
        uint maxStack = MaxStack(template);
        CharacterInventoryContainer bag = Bag;
        long room = (long)(bag.Capacity - bag.Held.Count) * maxStack;
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

    /// <summary>
    /// A stack of the item below the maximum, and not offered in a trade. One already above it (a lowered template) is
    /// left alone.
    /// </summary>
    private bool IsFillable(InventoryItem item, ItemTemplateId templateId, uint maxStack) =>
        item.TemplateId == templateId && item.Count < maxStack && !IsLocked(item);

    /// <summary>Offered in a trade (#1175). One bool read while nothing is.</summary>
    private bool IsLocked(InventoryItem item) => owner.Locks.Any && owner.Locks.IsLocked(item.InstanceId);

    /// <summary>A MaxStackSize of 0 is read as 1: every item takes at least a slot of its own.</summary>
    private static uint MaxStack(ItemTemplate template) => InventoryMove.MaxStack(template);

    private void Take(InventoryType container, InventoryItem item, uint count)
    {
        if (count == 0)
            return;

        if (count == item.Count)
            Delete(container, item);
        else
            Replace(container, item with { Count = item.Count - count });
    }

    private void Create(InventoryType container, InventoryItem item)
    {
        owner.Container(container).Put(item);
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

    private void Delete(InventoryType container, InventoryItem item)
    {
        owner.Container(container).Remove(item.Slot);
        owner.SaveState.ItemRemoved(item.InstanceId);
        owner.SaveState.SlotChanged(container, item.Slot, occupiedBefore: true, occupiedAfter: false);
        owner.ClientChanges.RecordSlot(container, item.Slot);
    }
}
