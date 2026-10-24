using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Character;

namespace Avalon.World.Inventory;

/// <summary>
/// Changes one character's items. Tick thread only. All or nothing: if the whole
/// count cannot be applied, nothing changes. Lives in Avalon.World,
/// not the modding API; ICharacterInventory there stays read-only. The one writer of the character's
/// <see cref="ItemModifierStore" /> (#1179): an item's modifiers enter and leave it with the item.
/// </summary>
public interface IInventoryService
{
    /// <summary>Whether <see cref="TryAdd" /> would succeed, changing nothing.</summary>
    InventoryAddResult CanAdd(ItemTemplateId templateId, uint count);

    /// <summary>
    /// Fills existing Bag stacks of the template up to its MaxStackSize in slot order, then the lowest
    /// free Bag slots, one new instance per stack. Never adds to Equipment or the Bank, nor to a stack
    /// offered in a trade (#1175), nor to one carrying modifiers (#1179), nor to a Bag slot a completing trade
    /// reserved; a Unique template counts the copies that trade is about to add. A count of 0 is Ok and
    /// changes nothing. A template that links modifiers in the current catalog (a base's implicits, a
    /// unique's own, #1179) makes one copy per slot, each rolling them now through IItemRoller.RollPlain,
    /// its modifiers in the store and marked for the save that inserts it.
    /// </summary>
    InventoryAddResult TryAdd(ItemTemplateId templateId, uint count);

    /// <summary>
    /// From Bag stacks of the template, highest slot first, leaving out any stack offered in a trade
    /// (#1175). NotFound when the Bag holds none of it outside a trade, whatever the count, including 0.
    /// </summary>
    InventoryRemoveResult TryRemove(ItemTemplateId templateId, uint count);

    /// <summary>From the one instance, wherever it sits. Locked while it is offered in a trade (#1175).</summary>
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
    /// the instance itself, with the item and its slot marked Removed, and its modifiers, which leave
    /// the store with it (#1179). Part of a stack shrinks it (the item is marked Changed) and returns a
    /// copy of <paramref name="count" /> with a new id from IItemIdAllocator and no modifiers. The copy
    /// is in no container and carries no mark. A vendor sale (spec #432) keeps what this returns for
    /// buyback. Throws when the slot is empty or holds fewer than <paramref name="count" />, when the
    /// count is 0, or when it is part of an item carrying modifiers, which is one of a kind and never
    /// splits: callers decide first.
    /// </summary>
    CarriedItem TakeOut(SlotRef slot, uint count);

    /// <summary>
    /// Puts this exact instance (same id, count, durability, flags, charges, rarity, item level and
    /// modifiers) in the lowest free Bag slot. It is never merged into a stack. The item is marked New,
    /// whatever mark it had before, so the next save upserts it, and, when it has modifiers, they are
    /// marked too, so that save writes them (#1179): its rows may have been deleted since it left.
    /// InventoryFull when no Bag slot is free (a slot a completing trade reserved, #1175, is not).
    /// UniqueAlreadyOwned when its template is Unique and the character would then hold more than one
    /// copy anywhere, counting the copies a completing trade is about to add; an instance whose
    /// template is gone is still the player's own, and is added. Throws when the instance is already
    /// held: a vendor buyback (spec #432) only ever adds one that TakeOut handed over.
    /// </summary>
    InventoryAddResult TryAddInstance(CarriedItem carried);

    /// <summary>
    /// A completing trade's own add (#1175): this exact instance in exactly <paramref name="bagSlot" />,
    /// never merged, marked New, with its modifiers, which are not marked: the paired save wrote them
    /// (#1179). InventoryFull when the slot is occupied; a slot the trade reserved is
    /// not refused, since it is what the reservation was for. UniqueAlreadyOwned, and the throw for an
    /// instance already held, as <see cref="TryAddInstance" /> answers them, counting only what the
    /// character holds. Throws when the slot is past the Bag: the trade plans the slot first.
    /// </summary>
    InventoryAddResult TryAddInstanceAt(CarriedItem carried, byte bagSlot);
}
