using System.Diagnostics;
using Avalon.Common;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.World.Doodads;
using Avalon.World.Entities;
using Avalon.World.Loot;
using Avalon.World.Public.Characters;
using Avalon.World.Reload;

namespace Avalon.World.Items;

/// <summary>
/// Whether a character may extract one affix of the item in one Bag slot at a Resonance Workbench now (#1180 part C). It
/// reads and never changes, costs no gold and allocates nothing: ItemExtractHandler applies an Ok. The checks, in order:
/// the Workbench's shared prefix (<see cref="WorkbenchRules.Access" />: dead, stunned, the Workbench not found, beyond its
/// range); the Bag holds nothing in that slot (a slot past the Bag included); the item is not identified; it is offered
/// in a trade; it is Tainted; and NotExtractable, for any of these: its template is gone or is not a rolled base (a
/// unique, a glyph), the copy is not Magic or Rare, the template can never be destroyed, the item carries no row of that
/// modifier (an id above int.MaxValue names none), the row's catalog modifier is gone or is not a prefix or suffix (an
/// implicit, a unique's own), the row is sealed, or the patch holds no glyph template. Tick thread only.
/// </summary>
public static class ExtractRules
{
    /// <param name="workbench">The Workbench's guid as the request carries it.</param>
    /// <param name="modifierId">The catalog modifier of the affix to keep, as the request carries it.</param>
    /// <param name="items">The item templates and modifier catalog as they are now.</param>
    public static ItemExtractResult Decide(CharacterEntity character, IDoodadHost? host, ulong workbench,
        byte bagSlot, uint modifierId, ItemsPatch items)
    {
        WorkbenchAccess access = WorkbenchRules.Access(character, host, workbench);
        if (access != WorkbenchAccess.Ok)
        {
            return access switch
            {
                WorkbenchAccess.Dead => ItemExtractResult.Dead,
                WorkbenchAccess.Stunned => ItemExtractResult.Stunned,
                WorkbenchAccess.NotFound => ItemExtractResult.NotFound,
                WorkbenchAccess.TooFar => ItemExtractResult.TooFar,
                _ => throw new UnreachableException($"WorkbenchAccess {access} has no extract result"),
            };
        }

        if (!character.Container(InventoryType.Bag).TryGet(bagSlot, out InventoryItem item))
            return ItemExtractResult.NoItem;

        if (!item.Identified)
            return ItemExtractResult.Unidentified;

        if (character.Locks.IsLocked(item.InstanceId))
            return ItemExtractResult.Locked;

        if (item.Tainted)
            return ItemExtractResult.Tainted;

        return Extractable(character, item, modifierId, items)
            ? ItemExtractResult.Ok
            : ItemExtractResult.NotExtractable;
    }

    /// <summary>
    /// Whether <paramref name="item" />, a magic or rare copy of a base that can be destroyed, carries an awake prefix or
    /// suffix row of <paramref name="modifierId" />, and the patch can make its glyph.
    /// </summary>
    private static bool Extractable(CharacterEntity character, in InventoryItem item, uint modifierId, ItemsPatch items)
    {
        if (items.Find(item.TemplateId) is not { } template || !ItemRoller.IsRolledBase(template))
            return false;

        if (item.Rarity is not (ItemRarity.Magic or ItemRarity.Rare))
            return false;

        if ((template.Flags & ItemTemplateFlags.NoDestroy) != 0)
            return false;

        if (modifierId > int.MaxValue || ExtractableRow(character.ItemModifiers.Of(item.InstanceId), (int)modifierId,
                items.Modifiers) is null)
        {
            return false;
        }

        return items.Glyph is not null;
    }

    /// <summary>
    /// The row of <paramref name="rows" /> carrying <paramref name="modifierId" />, when it is an awake prefix or suffix
    /// <paramref name="catalog" /> still holds; otherwise null. Shared by the rules and the inventory service, so both judge
    /// the same row the same way. An item never carries two rows of one modifier (one per group).
    /// </summary>
    internal static ItemInstanceModifier? ExtractableRow(IReadOnlyList<ItemInstanceModifier> rows, int modifierId,
        ItemModifierCatalog catalog)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            ItemInstanceModifier row = rows[i];
            if (row.ModifierId != modifierId)
                continue;

            return !row.Sealed
                   && catalog.Find(modifierId)?.Group is { Kind: ItemModifierKind.Prefix or ItemModifierKind.Suffix }
                ? row
                : null;
        }

        return null;
    }
}
