using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Character;
using Avalon.World.Public.Characters;

namespace Avalon.World.Inventory;

/// <summary>
/// Changes one character's items. Tick thread only. All or nothing: if the whole
/// count cannot be applied, nothing changes. Lives in Avalon.World,
/// not the modding API; ICharacterInventory there stays read-only.
/// </summary>
public interface IInventoryService
{
    /// <summary>Whether <see cref="TryAdd" /> would succeed, changing nothing.</summary>
    InventoryAddResult CanAdd(ItemTemplateId templateId, uint count);

    /// <summary>
    /// Fills existing Bag stacks of the template up to its MaxStackSize in slot order, then the lowest
    /// free Bag slots, one new instance per stack. Never adds to Equipment or the Bank. A count of 0
    /// is Ok and changes nothing.
    /// </summary>
    InventoryAddResult TryAdd(ItemTemplateId templateId, uint count);

    /// <summary>
    /// From Bag stacks of the template, highest slot first. NotFound when the Bag holds none of it,
    /// whatever the count, including 0.
    /// </summary>
    InventoryRemoveResult TryRemove(ItemTemplateId templateId, uint count);

    /// <summary>From the one instance, wherever it sits.</summary>
    InventoryRemoveResult TryRemove(ItemInstanceId itemInstanceId, uint count);

    /// <summary>
    /// A client move (spec #463): InventoryMove decides from the two slots and the count whether it
    /// is a move, split, merge or swap, and this applies it. A split's new instance takes its id from
    /// IItemIdAllocator. <paramref name="bankAccessible" /> is the caller's BankAccess check; a request
    /// naming the Bank without it is refused as BankClosed.
    /// </summary>
    ItemRequestResult TryMove(SlotRef from, SlotRef to, uint? count, bool bankAccessible);

    /// <summary>Destroys <paramref name="count" /> (null: all) of the stack in one slot, unless it is NoDestroy.</summary>
    ItemRequestResult TryDestroy(SlotRef slot, uint? count, bool bankAccessible);

    /// <summary>
    /// Takes <paramref name="count" /> out of one slot and returns what left. A whole stack returns
    /// the instance itself, with the item and its slot marked Removed. Part of a stack shrinks it
    /// (the item is marked Changed) and returns a copy of <paramref name="count" /> with a new id
    /// from IItemIdAllocator. The copy is in no container and carries no mark. A vendor sale
    /// (spec #432) keeps what this returns for buyback. Throws when the slot is empty or holds fewer
    /// than <paramref name="count" />, or when the count is 0: callers decide first.
    /// </summary>
    InventoryItem TakeOut(SlotRef slot, uint count);

    /// <summary>
    /// Puts this exact instance (same id, count, durability, flags, charges) in the lowest free Bag
    /// slot. It is never merged into a stack. The item is marked New, whatever mark it had before,
    /// so the next save upserts it. InventoryFull when no Bag slot is free. UniqueAlreadyOwned when
    /// its template is Unique and the character would then hold more than one copy anywhere; an
    /// instance whose template is gone is still the player's own, and is added. Throws when the
    /// instance is already held: a vendor buyback (spec #432) only ever adds one that TakeOut
    /// handed over.
    /// </summary>
    InventoryAddResult TryAddInstance(InventoryItem item);
}
