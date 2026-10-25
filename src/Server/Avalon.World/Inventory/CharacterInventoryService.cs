using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.World.Entities;
using Avalon.World.Loot;
using Avalon.World.Public.Characters;
using Avalon.World.Reload;

namespace Avalon.World.Inventory;

/// <summary>
/// <see cref="IInventoryService" /> over one character's containers. The one writer of a
/// character's items: every change it makes marks <see cref="CharacterEntity.SaveState" /> and
/// <see cref="CharacterEntity.ClientChanges" />, and it alone writes <see cref="CharacterEntity.ItemModifiers" /> after
/// select (#1179), beside the container, so an item's entry lives exactly as long as the item is held. A stack offered
/// in a trade (<see cref="CharacterEntity.Locks" />, #1175) is left alone: adds never fill it, removals by template
/// never see it, and removing it by id is Locked. While a trade completes, every add but its own
/// (<see cref="TryAddInstanceAt" />) treats the Bag slots it reserved as taken and counts the copies it is about to add
/// toward Unique. Every new copy of a template that has base-stat ranges or links modifiers rolls them as it is made
/// (#1179), through <paramref name="roller" /> over the items <paramref name="currentItems" /> answers at that moment.
/// </summary>
public sealed class CharacterInventoryService(
    CharacterEntity owner,
    Func<ItemTemplateId, ItemTemplate?> findTemplate,
    IItemIdAllocator itemIds,
    IItemRoller roller,
    Func<ItemsPatch> currentItems) : IInventoryService
{
    /// <summary>Where an instance id is looked for, in order.</summary>
    private static readonly InventoryType[] s_allContainers = [InventoryType.Bag, InventoryType.Equipment, InventoryType.Bank];

    private CharacterInventoryContainer Bag => owner.Container(InventoryType.Bag);

    public InventoryAddResult CanAdd(ItemTemplateId templateId, uint count) => Check(templateId, count, currentItems(), out _);

    public InventoryAddResult TryAdd(ItemTemplateId templateId, uint count)
    {
        ItemsPatch items = currentItems();
        InventoryAddResult result = Check(templateId, count, items, out ItemTemplate? template);
        if (result != InventoryAddResult.Ok || count == 0)
            return result;

        bool rolls = Rolls(template!, items);
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

            // A copy that rolls is one item in its slot (CreateRolled makes count 1), whatever is left to add.
            uint take = rolls ? 1 : Math.Min(maxStack, remaining);
            if (rolls)
            {
                CreateRolled(slot, template!, items);
            }
            else
            {
                Create(InventoryType.Bag, new InventoryItem(slot, itemIds.Next(), templateId, take,
                    ItemInstanceDefaults.InitialDurability(template!), ItemInstanceFlags.None, template!.Rarity,
                    ItemInstanceDefaults.InitialItemLevel(template), RareName: null, Identified: true));
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
        {
            (ItemBaseStats baseStats, IReadOnlyList<ItemInstanceModifier> modifiers) = Delete(slot.Container, item);
            return new CarriedItem(item, baseStats, modifiers);
        }

        // #1179: an item carrying rolled rows (modifiers or base stats) is one of a kind and never splits, as the move
        // rules refuse it; a part of it would be a copy without them.
        if (owner.ItemModifiers.IsOneOfAKind(item.InstanceId))
        {
            throw new InvalidOperationException(
                $"Cannot take {count} of {item.Count} out of {slot}: an item carrying rolled rows is taken out whole.");
        }

        Replace(slot.Container, item with { Count = item.Count - count });
        return new CarriedItem(item with { InstanceId = itemIds.Next(), Count = count }, default, []);
    }

    public InventoryAddResult TryAddInstance(CarriedItem carried)
    {
        InventoryItem item = carried.Item;
        ThrowIfHeld(item);

        if (LowestFreeBagSlot() is not { } slot)
            return InventoryAddResult.InventoryFull;

        // Its own unique guard (#432), not only the caller's: two copies of a unique item must
        // never exist. A template that is gone cannot say it is unique, and the item is still the
        // player's own.
        if (IsUnique(item.TemplateId) && OwnedOrIncoming(item.TemplateId) + item.Count > 1)
            return InventoryAddResult.UniqueAlreadyOwned;

        Create(InventoryType.Bag, item with { Slot = slot }, carried.BaseStats, carried.Modifiers);

        // Its rows may be gone (a sale saved before the buyback deleted them, modifiers, base stats and all), so the save
        // that upserts it writes them again.
        if (carried.HasRolledRows)
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

        // Its rolled rows unmarked: the paired save wrote them as this character's.
        Create(InventoryType.Bag, item with { Slot = bagSlot }, carried.BaseStats, carried.Modifiers);
        return InventoryAddResult.Ok;
    }

    public InventoryAddResult TryAddRolled(RolledItem item)
    {
        ItemTemplate? template = findTemplate(item.TemplateId);
        if (template is null)
            return InventoryAddResult.UnknownTemplate;

        if (LowestFreeBagSlot() is not { } slot)
            return InventoryAddResult.InventoryFull;

        if ((template.Flags & ItemTemplateFlags.Unique) != 0 && OwnedOrIncoming(item.TemplateId) + 1 > 1)
            return InventoryAddResult.UniqueAlreadyOwned;

        // A copy a drop or a quest rolled is identified only when it is common (#1179).
        PutRolled(slot, item, template, identified: item.Rarity == ItemRarity.Common);
        return InventoryAddResult.Ok;
    }

    /// <summary>The lowest empty Bag slot a completing trade has not reserved (#1175), or null when there is none.</summary>
    private byte? LowestFreeBagSlot()
    {
        CharacterInventoryContainer bag = Bag;
        for (byte slot = 0; slot < bag.Capacity; slot++)
        {
            if (owner.Locks.IsFree(bag, slot))
                return slot;
        }

        return null;
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

    private InventoryAddResult Check(ItemTemplateId templateId, uint count, ItemsPatch items, out ItemTemplate? template)
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
        uint maxStack = MaxStack(template, Rolls(template, items));
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
    /// A stack of the item below the maximum, not offered in a trade, and carrying no rolled rows (#1179): an item with
    /// modifiers or base stats is one of a kind. One already above the maximum (a lowered template) is left alone.
    /// </summary>
    private bool IsFillable(InventoryItem item, ItemTemplateId templateId, uint maxStack) =>
        item.TemplateId == templateId && item.Count < maxStack && !IsLocked(item)
        && !owner.ItemModifiers.IsOneOfAKind(item.InstanceId);

    /// <summary>Offered in a trade (#1175). One bool read while nothing is.</summary>
    private bool IsLocked(InventoryItem item) => owner.Locks.IsLocked(item.InstanceId);

    /// <summary>
    /// A MaxStackSize of 0 is read as 1: every item takes at least a slot of its own. A copy that rolls is one of a kind
    /// (#1179), so each takes a slot of its own whatever the template says.
    /// </summary>
    private static uint MaxStack(ItemTemplate template, bool rolls) => rolls ? 1u : InventoryMove.MaxStack(template);

    /// <summary>
    /// Whether a new copy of the template rolls (#1179): it has base-stat ranges, or links modifiers in the catalog.
    /// </summary>
    private static bool Rolls(ItemTemplate template, ItemsPatch items) =>
        template.BaseStats.Count > 0 || items.Modifiers.LinkedTo(template.Id).Count > 0;

    /// <summary>
    /// One new copy of a template that rolls, rolled now (#1179): a plain copy at its template's item level, in the slot,
    /// with its base stats and modifiers in the store and marked, so the save that inserts it writes them. It arrives
    /// identified, a unique included, as every copy <see cref="TryAdd" /> makes does.
    /// </summary>
    private void CreateRolled(byte slot, ItemTemplate template, ItemsPatch items) =>
        PutRolled(slot, roller.RollPlain(template, ItemInstanceDefaults.InitialItemLevel(template), items), template,
            identified: true);

    /// <summary>
    /// A copy a roll made, in the Bag slot: its id, rarity, item level and name as the roll gave them, count 1 and its
    /// template's initial durability, identified as the caller says, with its base stats and its modifier list handed to the
    /// store as its own entry and marked, so the save that inserts it writes them.
    /// </summary>
    private void PutRolled(byte slot, RolledItem rolled, ItemTemplate template, bool identified)
    {
        Create(InventoryType.Bag, new InventoryItem(slot, rolled.InstanceId, rolled.TemplateId, 1,
            ItemInstanceDefaults.InitialDurability(template), ItemInstanceFlags.None, rolled.Rarity, rolled.ItemLevel,
            rolled.RareName, identified));
        if (!rolled.HasRolledRows)
            return;

        owner.ItemModifiers.PutMade(rolled.InstanceId, rolled.BaseStats, rolled.Modifiers);
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

    /// <summary>
    /// An instance arriving in the slot with its rolled rows: <paramref name="baseStats" /> and
    /// <paramref name="modifiers" /> enter the store with it.
    /// </summary>
    private void Create(InventoryType container, InventoryItem item, in ItemBaseStats baseStats,
        IReadOnlyList<ItemInstanceModifier> modifiers)
    {
        owner.ItemModifiers.Put(item.InstanceId, baseStats, modifiers);
        Create(container, item);
    }

    /// <summary>A new instance, or one arriving, in the slot.</summary>
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

    /// <summary>The instance leaves the character: its rolled rows leave the store with it, and are returned.</summary>
    private (ItemBaseStats BaseStats, IReadOnlyList<ItemInstanceModifier> Modifiers) Delete(InventoryType container,
        InventoryItem item)
    {
        owner.Container(container).Remove(item.Slot);
        owner.SaveState.ItemRemoved(item.InstanceId);
        owner.SaveState.SlotChanged(container, item.Slot, occupiedBefore: true, occupiedAfter: false);
        owner.ClientChanges.RecordSlot(container, item.Slot);
        return owner.ItemModifiers.Take(item.InstanceId);
    }
}
