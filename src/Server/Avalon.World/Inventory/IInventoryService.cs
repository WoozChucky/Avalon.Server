using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Items;
using Avalon.World.Loot;

namespace Avalon.World.Inventory;

/// <summary>
/// Changes one character's items. Tick thread only. All or nothing: if the whole
/// count cannot be applied, nothing changes. Lives in Avalon.World,
/// not the modding API; ICharacterInventory there stays read-only. The one writer of the character's
/// <see cref="ItemModifierStore" /> (#1179): an item's rolled rows (modifiers and base stats) enter and leave it
/// with the item.
/// </summary>
public interface IInventoryService
{
    /// <summary>Whether <see cref="TryAdd" /> would succeed, changing nothing.</summary>
    InventoryAddResult CanAdd(ItemTemplateId templateId, uint count);

    /// <summary>
    /// Fills existing Bag stacks of the template up to its MaxStackSize in slot order, then the lowest
    /// free Bag slots, one new instance per stack. Never adds to Equipment or the Bank, nor to a stack
    /// offered in a trade (#1175), nor to one carrying rolled rows (#1179), nor to a Bag slot a completing trade
    /// reserved; a Unique template counts the copies that trade is about to add. A count of 0 is Ok and
    /// changes nothing. A template that has base-stat ranges or links modifiers in the current catalog (a base's
    /// implicit, a unique's own, #1179) makes one copy per slot, each rolling them now through IItemRoller.RollPlain,
    /// its rolled rows in the store and marked for the save that inserts it.
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
    /// the instance itself, with the item and its slot marked Removed, and its rolled rows, which leave
    /// the store with it (#1179). Part of a stack shrinks it (the item is marked Changed) and returns a
    /// copy of <paramref name="count" /> with a new id from IItemIdAllocator and no rolled rows. The copy
    /// is in no container and carries no mark. A vendor sale (spec #432) keeps what this returns for
    /// buyback. Throws when the slot is empty or holds fewer than <paramref name="count" />, when the
    /// count is 0, or when it is part of an item carrying rolled rows, which is one of a kind and never
    /// splits: callers decide first.
    /// </summary>
    CarriedItem TakeOut(SlotRef slot, uint count);

    /// <summary>
    /// Puts this exact instance (same id, count, durability, flags, charges, rarity, item level and
    /// rolled rows) in the lowest free Bag slot. It is never merged into a stack. The item is marked New,
    /// whatever mark it had before, so the next save upserts it, and, when it has rolled rows, they are
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
    /// never merged, marked New, with its rolled rows, which are not marked: the paired save wrote them
    /// (#1179). InventoryFull when the slot is occupied; a slot the trade reserved is
    /// not refused, since it is what the reservation was for. UniqueAlreadyOwned, and the throw for an
    /// instance already held, as <see cref="TryAddInstance" /> answers them, counting only what the
    /// character holds. Throws when the slot is past the Bag: the trade plans the slot first.
    /// </summary>
    InventoryAddResult TryAddInstanceAt(CarriedItem carried, byte bagSlot);

    /// <summary>
    /// Puts the copy a roll made (#1179: a drop picked up, a rolled reward) in the lowest free Bag slot, exactly as
    /// it was made: its id, rarity, item level and rare name, count 1, its template's initial durability, never merged.
    /// Its base stats and its modifier list are handed to the store as the item's own entry (the caller keeps no use of
    /// the list) and marked, with the item marked New, so the next save inserts both. InventoryFull when no Bag slot is
    /// free (a slot a completing trade reserved, #1175, is not), UniqueAlreadyOwned as <see cref="TryAddInstance" />
    /// answers it, and UnknownTemplate when its template is gone; any answer but Ok changes nothing.
    /// </summary>
    InventoryAddResult TryAddRolled(RolledItem item);

    /// <summary>
    /// Identifies the item in <paramref name="bagSlot" /> (#1179): the same instance, now identified, marked Changed and
    /// its slot recorded for the client, which is sent its name and every modifier in the tick's update. Its rolled rows
    /// do not change, so they are not marked. False, changing nothing, for an empty slot (a slot past the Bag included)
    /// or an item already identified. It checks nothing else: IdentifyRules decides the request (the Workbench, a
    /// trade's lock, the cost) first.
    /// </summary>
    bool TryIdentify(byte bagSlot);

    /// <summary>
    /// Wakes the sealed affix of the item worn in <paramref name="equipmentSlot" /> (#1180) when its bar is full
    /// (Items.Awakening.ReadyToWake): IItemRoller.Awaken makes the woken row, which takes the sealed row's place in a new
    /// list the store puts as the item's (ItemModifierStore.ReplaceModifiers, never a field written in the old list), and
    /// the rows are marked for the next save. The item becomes Attuned, marked Changed and its slot recorded for the
    /// client; an item already Attuned keeps its flags, and only its slot is recorded. Null, changing nothing, for an
    /// empty slot (a slot past the Equipment included) or an item with no row ready. It does not refresh stats: the caller
    /// does, as after every gear change.
    /// </summary>
    AwakenedItem? TryAwaken(byte equipmentSlot);

    /// <summary>
    /// Extracts one affix of the item in <paramref name="bagSlot" /> as a glyph (#1180 part C): the item is destroyed,
    /// marked Removed with its rolled rows leaving the store, and in its slot is a new copy of the patch's Glyph template:
    /// count 1, Magic, identified, Attuned (so bound), its item level the affix's required item level, holding one row, a
    /// copy of the affix under a new row id at ordinal 0, unsealed and not imprinted. The glyph and its row are marked New
    /// for the next save, and the slot is recorded for the client. False, changing nothing, unless the slot holds an item
    /// Items.ItemModification.Allowed accepts carrying an awake prefix or suffix row of <paramref name="modifierId" />
    /// in the current catalog, and the patch holds a Glyph template. The glyph is built in full before the item is
    /// destroyed. It checks nothing else: ExtractRules decides the request (the Workbench, a trade's lock, the base and
    /// its rarity) first.
    /// </summary>
    bool TryExtract(byte bagSlot, int modifierId);

    /// <summary>
    /// Imprints the glyph in <paramref name="glyphSlot" /> onto the item in <paramref name="targetSlot" /> (#1180 part C):
    /// IItemRoller.Imprint makes the item's new modifier list (its rows, then the glyph's row copied under a new id at the
    /// next ordinal, imprinted) and its new instability, which becomes the item's in the store, marked for the next save;
    /// the glyph is destroyed, marked Removed. When the new total bricks the item, its affixes were rerolled among their
    /// weakest tiers, and the item is replaced Tainted with Attuned cleared, never modified or bound again; otherwise it
    /// is replaced Attuned, or, already Attuned, only its slot is recorded for the client. Answers Bricked or Ok; null,
    /// changing nothing, unless both slots are held and differ, the glyph is a glyph holding one row of a tiered prefix or
    /// suffix the catalog holds, and the item is one Items.ItemModification.Allowed accepts of a rolled base. It checks
    /// nothing else: ImprintRules decides the request (the Workbench, a trade's lock, the rarity, the room, the group, the
    /// base kind, the item level and the gold) first, and the caller spends the gold. It never logs or throws with the
    /// item's instability in a message.
    /// </summary>
    ItemImprintResult? TryImprint(byte glyphSlot, byte targetSlot);
}

/// <summary>A wake <see cref="IInventoryService.TryAwaken" /> made (#1180): the slot, the row's catalog modifier now, and how it woke.</summary>
public readonly record struct AwakenedItem(byte EquipmentSlot, int ModifierId, AwakeningOutcome Outcome);
