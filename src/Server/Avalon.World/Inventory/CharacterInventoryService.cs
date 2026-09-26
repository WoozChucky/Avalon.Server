using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.World.Entities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;

namespace Avalon.World.Inventory;

/// <summary>
/// <see cref="IInventoryService" /> over one character's containers. The one writer of a
/// character's items: every change it makes marks <see cref="CharacterEntity.SaveState" /> and
/// <see cref="CharacterEntity.ClientChanges" />.
/// </summary>
public sealed class CharacterInventoryService(
    CharacterEntity owner,
    Func<ItemTemplateId, ItemTemplate?> findTemplate,
    IItemIdAllocator itemIds) : IInventoryService
{
    /// <summary>Where an instance id is looked for, in order.</summary>
    private static readonly InventoryType[] AllContainers = [InventoryType.Bag, InventoryType.Equipment, InventoryType.Bank];

    private CharacterInventoryContainer Bag => owner.Container(InventoryType.Bag);

    public InventoryAddResult CanAdd(ItemTemplateId templateId, uint count) => Check(templateId, count, out _);

    public InventoryAddResult TryAdd(ItemTemplateId templateId, uint count)
    {
        InventoryAddResult result = Check(templateId, count, out ItemTemplate? template);
        if (result != InventoryAddResult.Ok || count == 0)
            return result;

        uint maxStack = MaxStack(template!);
        uint remaining = count;

        // Existing stacks first, in slot order, so a new stack is never started while one of the
        // same item still has room.
        foreach (InventoryItem stack in PartialStacks(templateId, maxStack).OrderBy(i => i.Slot).ToList())
        {
            uint take = Math.Min(maxStack - stack.Count, remaining);
            Replace(InventoryType.Bag, stack with { Count = stack.Count + take });
            remaining -= take;
            if (remaining == 0)
                return InventoryAddResult.Ok;
        }

        foreach (ushort slot in Bag.FreeSlots().ToList())
        {
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
        List<InventoryItem> stacks = Bag.Items
            .Where(i => i.TemplateId == templateId)
            .OrderByDescending(i => i.Slot)
            .ToList();

        if (stacks.Count == 0)
            return InventoryRemoveResult.NotFound;

        if (stacks.Sum(i => (long)i.Count) < count)
            return InventoryRemoveResult.NotEnough;

        uint remaining = count;
        foreach (InventoryItem stack in stacks)
        {
            if (remaining == 0)
                break;

            uint take = Math.Min(stack.Count, remaining);
            Take(InventoryType.Bag, stack, take);
            remaining -= take;
        }

        return InventoryRemoveResult.Ok;
    }

    public InventoryRemoveResult TryRemove(ItemInstanceId itemInstanceId, uint count)
    {
        foreach (InventoryType container in AllContainers)
        {
            foreach (InventoryItem item in owner.Container(container).Items)
            {
                if (item.InstanceId != itemInstanceId)
                    continue;

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

        // Unique caps ownership at one copy, anywhere the character keeps items.
        if (template.Flags.HasFlag(ItemTemplateFlags.Unique) && OwnedCount(templateId) + count > 1)
            return InventoryAddResult.UniqueAlreadyOwned;

        // In long: 30 slots of a uint.MaxValue stack still fit, and nothing here can wrap.
        uint maxStack = MaxStack(template);
        long room = PartialStacks(templateId, maxStack).Sum(i => (long)(maxStack - i.Count))
                    + (long)Bag.FreeSlots().Count() * maxStack;

        return room >= count ? InventoryAddResult.Ok : InventoryAddResult.InventoryFull;
    }

    private long OwnedCount(ItemTemplateId templateId) => AllContainers.Sum(container =>
        owner.Container(container).Items.Where(i => i.TemplateId == templateId).Sum(i => (long)i.Count));

    /// <summary>Stacks below the maximum. One already above it (a lowered template) is left alone.</summary>
    private IEnumerable<InventoryItem> PartialStacks(ItemTemplateId templateId, uint maxStack) =>
        Bag.Items.Where(i => i.TemplateId == templateId && i.Count < maxStack);

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
