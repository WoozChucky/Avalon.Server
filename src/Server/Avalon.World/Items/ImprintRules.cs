using System.Diagnostics;
using Avalon.Common;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.World.Doodads;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Avalon.World.Public.Characters;
using Avalon.World.Reload;

namespace Avalon.World.Items;

/// <summary>
/// Whether a character may imprint the glyph in one Bag slot onto the item in another at a Resonance Workbench now (#1180
/// part C). It reads and never changes, and allocates nothing: ItemImprintHandler applies an Ok. The checks, in order:
/// <list type="number">
/// <item>the Workbench's shared prefix (<see cref="WorkbenchRules.Access" />: dead, stunned, the Workbench not found,
/// beyond its range);</item>
/// <item>NoGlyph: the glyph slot is empty or past the Bag, its template is gone or is not a glyph, it holds other than
/// exactly one row, or that row's catalog modifier is gone, untiered, or not a prefix or suffix;</item>
/// <item>NoItem: the target slot is empty or past the Bag;</item>
/// <item>Locked: the target is offered in a trade;</item>
/// <item>Unidentified;</item>
/// <item>WrongRarity: the copy is not Magic or Rare, or its template is gone or is not a rolled base (so a glyph, and the
/// glyph's own slot, is refused here);</item>
/// <item>Tainted;</item>
/// <item>NoRoom: the target's rows of the glyph's kind (prefix or suffix), a sealed one included, already fill what its
/// rarity allows, as a drop's roll fills it;</item>
/// <item>GroupTaken: the target carries an affix of the glyph modifier's group;</item>
/// <item>WrongSlot: the glyph's modifier never rolls on the target's base kind;</item>
/// <item>ItemLevelTooLow: the target's item level is below the glyph modifier's required item level;</item>
/// <item>InternalError: Game:Items:ImprintCost holds no cost for the glyph modifier's tier (a reload added a tier
/// without configuration), which the handler logs;</item>
/// <item>NotEnoughGold: the cost is above 0 and the wallet cannot spend it (the wallet already leaves out gold a trade
/// holds).</item>
/// </list>
/// Rows the catalog no longer holds count toward neither room nor group. No rule reads the target's instability, so a
/// refusal says nothing of it. Tick thread only.
/// </summary>
public static class ImprintRules
{
    /// <param name="workbench">The Workbench's guid as the request carries it.</param>
    /// <param name="glyphSlot">The glyph's Bag slot as the request carries it.</param>
    /// <param name="targetSlot">The target's Bag slot as the request carries it.</param>
    /// <param name="items">The item templates and modifier catalog as they are now.</param>
    /// <param name="costs">Game:Items:ImprintCost: the gold an imprint costs, by the glyph modifier's tier.</param>
    /// <param name="wallet">The character's wallet.</param>
    /// <param name="cost">The imprint's cost when the answer is Ok; 0 otherwise.</param>
    public static ItemImprintResult Decide(CharacterEntity character, IDoodadHost? host, ulong workbench,
        byte glyphSlot, byte targetSlot, ItemsPatch items, IReadOnlyDictionary<byte, ulong> costs, IWallet wallet,
        out ulong cost)
    {
        cost = 0;
        WorkbenchAccess access = WorkbenchRules.Access(character, host, workbench);
        if (access != WorkbenchAccess.Ok)
        {
            return access switch
            {
                WorkbenchAccess.Dead => ItemImprintResult.Dead,
                WorkbenchAccess.Stunned => ItemImprintResult.Stunned,
                WorkbenchAccess.NotFound => ItemImprintResult.NotFound,
                WorkbenchAccess.TooFar => ItemImprintResult.TooFar,
                _ => throw new UnreachableException($"WorkbenchAccess {access} has no imprint result"),
            };
        }

        CharacterInventoryContainer bag = character.Container(InventoryType.Bag);
        if (!bag.TryGet(glyphSlot, out InventoryItem glyph)
            || GlyphAffix(character, glyph, items, out _) is not { } affix)
        {
            return ItemImprintResult.NoGlyph;
        }

        if (!bag.TryGet(targetSlot, out InventoryItem target))
            return ItemImprintResult.NoItem;

        if (character.Locks.IsLocked(target.InstanceId))
            return ItemImprintResult.Locked;

        if (!target.Identified)
            return ItemImprintResult.Unidentified;

        if (target.Rarity is not (ItemRarity.Magic or ItemRarity.Rare)
            || items.Find(target.TemplateId) is not { } template || !ItemRoller.IsRolledBase(template))
        {
            return ItemImprintResult.WrongRarity;
        }

        if (target.Tainted)
            return ItemImprintResult.Tainted;

        IReadOnlyList<ItemInstanceModifier> rows = character.ItemModifiers.Of(target.InstanceId);
        ItemModifierCatalog catalog = items.Modifiers;
        int room = target.Rarity == ItemRarity.Magic ? ItemRoller.MagicRoom : ItemRoller.RareRoom;
        if (CountOfKind(rows, affix.Group.Kind, catalog) >= room)
            return ItemImprintResult.NoRoom;

        if (CarriesGroup(rows, affix.GroupId, catalog))
            return ItemImprintResult.GroupTaken;

        if (!catalog.RollsOn(affix, template.BaseKind!.Value))
            return ItemImprintResult.WrongSlot;

        if (target.ItemLevel < affix.RequiredItemLevel)
            return ItemImprintResult.ItemLevelTooLow;

        // The glyph's modifier is tiered: GlyphAffix refuses one that is not.
        if (!costs.TryGetValue(affix.Tier!.Value, out ulong price))
            return ItemImprintResult.InternalError;

        if (price > 0 && wallet.CanSpend(price) != WalletResult.Ok)
            return ItemImprintResult.NotEnoughGold;

        cost = price;
        return ItemImprintResult.Ok;
    }

    /// <summary>
    /// The catalog modifier of <paramref name="glyph" />'s one row, with that row in <paramref name="row" />, when it is a
    /// glyph (its template's subclass is Glyph) holding exactly one row whose modifier the catalog holds as a tiered prefix
    /// or suffix; otherwise null. Shared by the rules and the inventory service, so both judge the glyph the same way.
    /// </summary>
    internal static ItemModifier? GlyphAffix(CharacterEntity character, in InventoryItem glyph, ItemsPatch items,
        out ItemInstanceModifier row)
    {
        row = null!;
        if (items.Find(glyph.TemplateId) is not { SubClass: ItemSubClass.Glyph })
            return null;

        IReadOnlyList<ItemInstanceModifier> rows = character.ItemModifiers.Of(glyph.InstanceId);
        if (rows.Count != 1)
            return null;

        if (items.Modifiers.Find(rows[0].ModifierId) is not
            { Tier: not null, Group.Kind: ItemModifierKind.Prefix or ItemModifierKind.Suffix } affix)
        {
            return null;
        }

        row = rows[0];
        return affix;
    }

    /// <summary>How many of <paramref name="rows" /> the catalog holds as modifiers of <paramref name="kind" />, sealed ones included.</summary>
    private static int CountOfKind(IReadOnlyList<ItemInstanceModifier> rows, ItemModifierKind kind, ItemModifierCatalog catalog)
    {
        int count = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            if (catalog.Find(rows[i].ModifierId)?.Group.Kind == kind)
                count++;
        }

        return count;
    }

    /// <summary>Whether one of <paramref name="rows" /> is a modifier the catalog holds in group <paramref name="groupId" />.</summary>
    private static bool CarriesGroup(IReadOnlyList<ItemInstanceModifier> rows, int groupId, ItemModifierCatalog catalog)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (catalog.Find(rows[i].ModifierId)?.GroupId == groupId)
                return true;
        }

        return false;
    }
}
