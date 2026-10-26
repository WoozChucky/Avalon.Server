using System.Globalization;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Server.World.UnitTests.Doodads;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Seeding;
using Avalon.World;
using Avalon.World.Configuration;
using Avalon.World.Doodads;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Avalon.World.Persistence;
using Avalon.World.Public.Characters;
using Avalon.World.Testing.Scenarios;
using Avalon.World.Trading;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;
using static Avalon.Server.World.UnitTests.Inventory.TestModifiers;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>
/// Extraction and imprinting at the Resonance Workbench (#1180 part C), through the real handlers, a real instance with
/// the seeded Workbench placed in it, the seeded templates and catalog, and the economy's own inventory service: every
/// request gets one result, a refusal names the first rule it breaks and changes nothing, an extract destroys the item and
/// puts a bound glyph holding the affix in its slot, and an imprint spends the glyph and its gold to add the affix to an
/// item, binding it, or bricking it Tainted at the instability limit. Character id 117_902 is this class's.
/// </summary>
public class ItemExtractionShould
{
    private static readonly StaticData s_data = ScenarioReferenceData.Shared.Data;
    private static readonly ItemTemplate s_chest = s_data.Items.Templates.Single(t => t.Name == "Ironbound Chestguard");
    private static readonly ItemTemplate s_helm = s_data.Items.Templates.Single(t => t.Name == "Ironbound Helm");
    private static readonly ItemTemplate s_unique = s_data.Items.Templates.First(t => t.Rarity == ItemRarity.Unique);
    private static readonly ItemTemplate s_sword = s_data.Items.Templates.First(t => t.BaseKind == ItemBaseKind.Sword);

    private static readonly DoodadTemplateView s_workbench =
        s_data.Doodads.Templates.Single(t => t.Name == "Resonance Workbench");

    private static readonly DoodadTemplateView s_forestChest = s_data.Doodads.Templates.Single(t => t.Name == "Forest Chest");

    private const uint Me = 117_902, RequestId = 43;
    private const byte Slot = 3, GlyphSlot = 5;
    private const ulong Gold = 25;

    // The Workbench stands at x 10, and the character at x 6, 4 m from it, within its 5 m.
    private static readonly Vector3 s_atWorkbench = new(10f, 0f, 0f), s_beside = new(6f, 0f, 0f);

    // The rules in the order ExtractRules asks them. A row breaks its own rule and every one after it the fixture can
    // (it asks for the implicit, which is never extractable), so it shows each earlier rule is asked first.
    private static readonly ItemExtractResult[] s_order =
    [
        ItemExtractResult.Dead, ItemExtractResult.Stunned, ItemExtractResult.NotFound, ItemExtractResult.TooFar,
        ItemExtractResult.NoItem, ItemExtractResult.Unidentified, ItemExtractResult.Locked, ItemExtractResult.Tainted,
        ItemExtractResult.NotExtractable,
    ];

    [Theory]
    [InlineData("dead", ItemExtractResult.Dead)]
    [InlineData("stunned", ItemExtractResult.Stunned)]
    [InlineData("unknown guid", ItemExtractResult.NotFound)]
    [InlineData("a chest's guid", ItemExtractResult.NotFound)]
    [InlineData("despawned workbench", ItemExtractResult.NotFound)]
    [InlineData("5.01 m away", ItemExtractResult.TooFar)]
    [InlineData("position NaN", ItemExtractResult.TooFar)]
    [InlineData("slot 30", ItemExtractResult.NoItem)]
    [InlineData("empty slot", ItemExtractResult.NoItem)]
    [InlineData("unidentified", ItemExtractResult.Unidentified)]
    [InlineData("offered in a trade", ItemExtractResult.Locked)]
    [InlineData("tainted", ItemExtractResult.Tainted)]
    [InlineData("a common copy", ItemExtractResult.NotExtractable)]
    [InlineData("a unique", ItemExtractResult.NotExtractable)]
    [InlineData("a glyph", ItemExtractResult.NotExtractable)]
    [InlineData("a NoDestroy base", ItemExtractResult.NotExtractable)]
    [InlineData("no such affix", ItemExtractResult.NotExtractable)]
    [InlineData("the implicit", ItemExtractResult.NotExtractable)]
    [InlineData("the sealed affix", ItemExtractResult.NotExtractable)]
    [InlineData("no glyph template in the patch", ItemExtractResult.NotExtractable)]
    public async Task Refuse_an_extract_naming_why_in_order(string @case, ItemExtractResult expected)
    {
        StaticData data = s_data;
        ItemTemplate template = s_chest;
        ItemRarity rarity = ItemRarity.Magic;
        switch (@case)
        {
            case "a common copy":
                rarity = ItemRarity.Common;
                break;
            case "a unique":
                (template, rarity) = (s_unique, ItemRarity.Unique);
                break;
            case "a glyph":
                (template, rarity) = (s_data.Items.Glyph!, ItemRarity.Magic);
                break;
            case "a NoDestroy base":
                // A copy of the chest base under the next free id, which can never be destroyed, in the fixture's own data.
                data = await ScenarioReferenceData.Shared.NewDataAsync(auras => auras);
                template = NoDestroyCopyOf(s_chest, data.Items.Templates.Max(t => t.Id.Value) + 1);
                data.Apply(data.Items with { Templates = [.. data.Items.Templates, template] });
                break;
            case "no glyph template in the patch":
                data = await ScenarioReferenceData.Shared.NewDataAsync(auras => auras);
                data.Apply(data.Items with { Templates = [.. data.Items.Templates.Where(t => t.SubClass != ItemSubClass.Glyph)] });
                break;
        }

        using var f = new Fixture(data, Chestguard(template, rarity, identified: !Breaks(ItemExtractResult.Unidentified),
            tainted: Breaks(ItemExtractResult.Tainted), sealedPrefix: @case == "the sealed affix"));
        CharacterEntity character = f.Client.Character;
        InventoryItem item = TestCharacters.At(character, InventoryType.Bag, Slot);
        IReadOnlyList<ItemInstanceModifier> rows = character.ItemModifiers.Of(item.InstanceId);

        if (Breaks(ItemExtractResult.Locked))
            character.Locks.Lock(item.InstanceId);

        byte bagSlot = Breaks(ItemExtractResult.NoItem) ? @case == "empty slot" ? (byte)2 : (byte)30 : Slot;

        if (Breaks(ItemExtractResult.TooFar))
            character.Position = @case == "position NaN" ? new Vector3(float.NaN, 0f, 0f) : new Vector3(4.99f, 0f, 0f);

        ObjectGuid workbench = f.Workbench.Guid;
        if (Breaks(ItemExtractResult.NotFound))
        {
            switch (@case)
            {
                case "a chest's guid":
                    workbench = f.Chest.Guid;
                    break;
                case "despawned workbench":
                    // Nothing despawns an Unlimited, script-less Workbench; the rule is asked all the same.
                    DoodadStates.Set(f.Workbench, DoodadState.Despawned);
                    break;
                default:
                    workbench = new ObjectGuid(ObjectType.Doodad, 999);
                    break;
            }
        }

        if (Breaks(ItemExtractResult.Stunned))
            character.Auras.Add(Auras.AuraTestData.HeldControl(CrowdControl.Stun), DateTimeOffset.UnixEpoch);
        if (Breaks(ItemExtractResult.Dead))
            character.IsDead = true;

        // Every row before NotExtractable's own asks for the implicit, so it breaks that rule too; NotExtractable's rows ask
        // for the prefix their item carries, unless the row is about the affix asked for.
        string affix = @case switch
        {
            "no such affix" => "P2 T8",
            "the sealed affix" => "P11 T7",
            _ when expected == ItemExtractResult.NotExtractable && @case != "the implicit" => "P11 T7",
            _ => "implicit IncreasedMaximumLife T9",
        };

        Assert.Equal(expected, f.Extract(workbench, bagSlot, CatalogKeys.ModifierId(affix)));

        Assert.Equal(item, TestCharacters.At(character, InventoryType.Bag, Slot));
        Assert.Same(rows, character.ItemModifiers.Of(item.InstanceId));
        Assert.Equal(Gold, character.Data!.Gold);
        // No item or slot marked for a save (a stun's aura is, and is no item's).
        SaveMarks marks = character.SaveState.TakeMarks();
        Assert.Empty(marks.Items);
        Assert.Empty(marks.Slots);
        Assert.False(character.SaveState.GoldDirty);
        Assert.False(character.ClientChanges.HasChanges);

        bool Breaks(ItemExtractResult rule) => Array.IndexOf(s_order, rule) >= Array.IndexOf(s_order, expected);
    }

    [Theory]
    [InlineData("a magic chest's prefix", "P11 T7")]
    [InlineData("a healthy attuned rare's imprinted suffix, extracted again", "S5 Fire T8")]
    public void Extract_an_affix_as_a_bound_glyph_in_the_items_slot(string @case, string affix)
    {
        CarriedItem carried = @case == "a magic chest's prefix"
            ? Chestguard(s_chest, ItemRarity.Magic, identified: true, tainted: false, sealedPrefix: false)
            : ImprintedHelm();
        using var f = new Fixture(s_data, carried);
        CharacterEntity character = f.Client.Character;
        ItemInstanceId old = carried.Item.InstanceId;
        int modifierId = CatalogKeys.ModifierId(affix);
        ItemInstanceModifier extracted = carried.Modifiers.Single(m => m.ModifierId == modifierId);
        ItemTemplate glyphTemplate = s_data.Items.Glyph!;

        Assert.Equal(ItemExtractResult.Ok, f.Extract(f.Workbench.Guid, Slot, modifierId));

        InventoryItem glyph = TestCharacters.At(character, InventoryType.Bag, Slot);
        Assert.NotEqual(old, glyph.InstanceId);
        Assert.Equal(
            (glyphTemplate.Id, 1u, ItemRarity.Magic, true, ItemInstanceFlags.Attuned, false,
                s_data.Items.Modifiers.Find(modifierId)!.RequiredItemLevel),
            (glyph.TemplateId, glyph.Count, glyph.Rarity, glyph.Identified, glyph.Flags, glyph.Tainted, glyph.ItemLevel));

        Assert.False(character.ItemModifiers.IsOneOfAKind(old));
        // A glyph is a new copy: the helm's 40 never comes with its affix.
        Assert.Equal(0, character.ItemModifiers.InstabilityOf(glyph.InstanceId));
        ItemInstanceModifier row = Assert.Single(character.ItemModifiers.Of(glyph.InstanceId));
        Assert.Equal((modifierId, (byte)0, false, false), (row.ModifierId, row.Ordinal, row.Sealed, row.Imprinted));
        Assert.NotEqual(extracted.Id, row.Id);
        Assert.Equal(glyph.InstanceId, row.ItemInstanceId);
        Assert.Equal(Values(extracted), Values(row));
        Assert.All(row.Values, v => Assert.Equal(row.Id, v.ItemInstanceModifierId));

        SaveMarks marks = character.SaveState.TakeMarks();
        Assert.Equal(SaveState.Removed, marks.Items[old].State);
        Assert.Equal((SaveState.New, true), (marks.Items[glyph.InstanceId].State, marks.Items[glyph.InstanceId].Modifiers));
        Assert.Equal(SaveState.Changed, marks.Slots[(InventoryType.Bag, Slot)].State);

        f.Flush();
        ItemSlotDto sent = Assert.Single(f.Updates()[^1].Slots, s => s.Container == InventoryType.Bag && s.Slot == Slot).Item!;
        Assert.Equal(glyph.InstanceId.Value, sent.ItemInstanceId);
        Assert.Equal(ItemNames.Display(glyphTemplate, ItemRarity.Magic, null, [row], s_data.Items.Modifiers), sent.Name);
        Assert.NotNull(sent.Name);

        Assert.Equal(Gold, character.Data!.Gold);
        Assert.False(TradeRules.IsTradeable(glyphTemplate, glyph.Flags));
    }

    // The rules in the order ImprintRules asks them. A row breaks its own rule and every one after it the fixture can, so
    // it shows each earlier rule is asked first: every row before NoRoom's is aimed at a rare whose three suffixes (one
    // sealed) include the glyph's group, every row before InternalError's prices no T9, and every row but the gold rows
    // holds no gold.
    private static readonly ItemImprintResult[] s_imprintOrder =
    [
        ItemImprintResult.Dead, ItemImprintResult.Stunned, ItemImprintResult.NotFound, ItemImprintResult.TooFar,
        ItemImprintResult.NoGlyph, ItemImprintResult.NoItem, ItemImprintResult.Locked, ItemImprintResult.Unidentified,
        ItemImprintResult.WrongRarity, ItemImprintResult.Tainted, ItemImprintResult.NoRoom, ItemImprintResult.GroupTaken,
        ItemImprintResult.WrongSlot, ItemImprintResult.ItemLevelTooLow, ItemImprintResult.InternalError,
        ItemImprintResult.NotEnoughGold,
    ];

    /// <summary>
    /// The imprint's refusals, in order. The default glyph is "S14 T9" in Bag slot 5, and the default target an identified
    /// Rare Ironbound Chestguard at item level 9 in slot 3 carrying its implicit, "P11 T7", "P12 T9" and "S1 T9". Every
    /// target holds instability 99, one short of tainting, so a rule that read it would show. Each row is answered once,
    /// and changes nothing: neither item, nor the store's lists, nor the instability, nor the gold; it marks no save and
    /// records nothing for the client.
    /// </summary>
    [Theory]
    [InlineData("dead", ItemImprintResult.Dead)]
    [InlineData("stunned", ItemImprintResult.Stunned)]
    [InlineData("unknown guid", ItemImprintResult.NotFound)]
    [InlineData("a chest's guid", ItemImprintResult.NotFound)]
    [InlineData("despawned workbench", ItemImprintResult.NotFound)]
    [InlineData("5.01 m away", ItemImprintResult.TooFar)]
    [InlineData("position NaN", ItemImprintResult.TooFar)]
    [InlineData("empty glyph slot", ItemImprintResult.NoGlyph)]
    [InlineData("a sword in the glyph slot", ItemImprintResult.NoGlyph)]
    [InlineData("a glyph with its modifier gone from the catalog", ItemImprintResult.NoGlyph)]
    [InlineData("empty target slot", ItemImprintResult.NoItem)]
    [InlineData("target slot 30", ItemImprintResult.NoItem)]
    [InlineData("target offered in a trade", ItemImprintResult.Locked)]
    [InlineData("unidentified rare", ItemImprintResult.Unidentified)]
    [InlineData("a common chest", ItemImprintResult.WrongRarity)]
    [InlineData("a unique", ItemImprintResult.WrongRarity)]
    [InlineData("a second glyph as the target", ItemImprintResult.WrongRarity)]
    [InlineData("the glyph's own slot", ItemImprintResult.WrongRarity)]
    [InlineData("tainted", ItemImprintResult.Tainted)]
    [InlineData("a magic chest whose one suffix is taken", ItemImprintResult.NoRoom)]
    [InlineData("a rare with three suffixes, one of them sealed", ItemImprintResult.NoRoom)]
    [InlineData("a rare already carrying S14", ItemImprintResult.GroupTaken)]
    [InlineData("S5 Fire onto a chest", ItemImprintResult.WrongSlot)]
    [InlineData("a T7 glyph onto item level 6", ItemImprintResult.ItemLevelTooLow)]
    [InlineData("costs without T9", ItemImprintResult.InternalError)]
    [InlineData("cost 25, 24 gold", ItemImprintResult.NotEnoughGold)]
    [InlineData("cost 25, 30 gold with 6 held for a trade", ItemImprintResult.NotEnoughGold)]
    public void Refuse_an_imprint_naming_why_in_order(string @case, ItemImprintResult expected)
    {
        (ItemTemplate template, ItemRarity rarity) = @case switch
        {
            "a common chest" => (s_chest, ItemRarity.Common),
            "a unique" => (s_unique, ItemRarity.Unique),
            "a second glyph as the target" => (s_data.Items.Glyph!, ItemRarity.Magic),
            "a magic chest whose one suffix is taken" => (s_chest, ItemRarity.Magic),
            _ when Breaks(ItemImprintResult.WrongRarity) => (s_chest, ItemRarity.Common),
            _ => (s_chest, ItemRarity.Rare),
        };
        string[] affixes = @case switch
        {
            "a second glyph as the target" => ["S1 T9"],
            "a magic chest whose one suffix is taken" => ["implicit IncreasedMaximumLife T9", "P11 T7", "S14 T9"],
            "a rare already carrying S14" => ["implicit IncreasedMaximumLife T9", "P11 T7", "P12 T9", "S14 T9"],
            _ when Breaks(ItemImprintResult.NoRoom) =>
                ["implicit IncreasedMaximumLife T9", "P11 T7", "P12 T9", "S1 T9", "S5 Blight T9", "S14 T9 sealed"],
            _ => ["implicit IncreasedMaximumLife T9", "P11 T7", "P12 T9", "S1 T9"],
        };
        CarriedItem target = Target(template, rarity, @case == "a T7 glyph onto item level 6" ? (ushort)6 : (ushort)9,
            identified: !Breaks(ItemImprintResult.Unidentified), tainted: Breaks(ItemImprintResult.Tainted),
            ItemInstanceFlags.None, instability: 99, affixes);
        CarriedItem second = @case switch
        {
            "a sword in the glyph slot" => TestCharacters.Carried(TestCharacters.Item(GlyphSlot, s_sword)),
            "a glyph with its modifier gone from the catalog" => Glyph("S14 T9", modifierId: 999_999),
            "a rare already carrying S14" => Glyph("S14 T8"),
            "S5 Fire onto a chest" => Glyph("S5 Fire T9"),
            "a T7 glyph onto item level 6" => Glyph("S14 T7"),
            _ => Glyph("S14 T9"),
        };
        var configuration = new GameConfiguration();
        if (Breaks(ItemImprintResult.InternalError))
            configuration.Items.ImprintCost = new Dictionary<byte, ulong> { [8] = 75, [7] = 200 };
        ulong gold = @case switch
        {
            "cost 25, 24 gold" => 24,
            "cost 25, 30 gold with 6 held for a trade" => 30,
            _ => 0,
        };

        using var f = new Fixture(s_data, target, second, gold, configuration);
        CharacterEntity character = f.Client.Character;
        InventoryItem item = TestCharacters.At(character, InventoryType.Bag, Slot);
        InventoryItem held = TestCharacters.At(character, InventoryType.Bag, GlyphSlot);
        IReadOnlyList<ItemInstanceModifier> rows = character.ItemModifiers.Of(item.InstanceId);
        IReadOnlyList<ItemInstanceModifier> heldRows = character.ItemModifiers.Of(held.InstanceId);

        if (@case == "cost 25, 30 gold with 6 held for a trade")
            character.Locks.HeldGold = 6;
        if (Breaks(ItemImprintResult.Locked))
            character.Locks.Lock(item.InstanceId);

        byte targetSlot = @case switch
        {
            "empty target slot" => 4,
            "the glyph's own slot" => GlyphSlot,
            _ when Breaks(ItemImprintResult.NoItem) => 30,
            _ => Slot,
        };
        byte glyphSlot = Breaks(ItemImprintResult.NoGlyph)
                         && @case is not ("a sword in the glyph slot" or "a glyph with its modifier gone from the catalog")
            ? (byte)2
            : GlyphSlot;
        ObjectGuid workbench = AtWorkbench(f, @case, Breaks(ItemImprintResult.NotFound), Breaks(ItemImprintResult.TooFar),
            Breaks(ItemImprintResult.Stunned), Breaks(ItemImprintResult.Dead));

        Assert.Equal(expected, f.Imprint(workbench, glyphSlot, targetSlot));

        Assert.Equal(item, TestCharacters.At(character, InventoryType.Bag, Slot));
        Assert.Equal(held, TestCharacters.At(character, InventoryType.Bag, GlyphSlot));
        Assert.Same(rows, character.ItemModifiers.Of(item.InstanceId));
        Assert.Same(heldRows, character.ItemModifiers.Of(held.InstanceId));
        Assert.Equal(99, character.ItemModifiers.InstabilityOf(item.InstanceId));
        Assert.Equal(gold, character.Data!.Gold);
        // No item or slot marked for a save (a stun's aura is, and is no item's).
        SaveMarks marks = character.SaveState.TakeMarks();
        Assert.Empty(marks.Items);
        Assert.Empty(marks.Slots);
        Assert.False(character.SaveState.GoldDirty);
        Assert.False(character.ClientChanges.HasChanges);

        bool Breaks(ItemImprintResult rule) =>
            Array.IndexOf(s_imprintOrder, rule) >= Array.IndexOf(s_imprintOrder, expected);
    }

    /// <summary>
    /// An imprint through the handler, over the seeded catalog, each adding 30 instability (Game:Items:Imprint's bounds
    /// both 30), for the "S14 T9" glyph's 25 gold. Below the limit the glyph's affix joins the item, imprinted, and binds
    /// it; at the limit the item bricks: its affixes are rerolled among their families' weakest tiers (drawn from the
    /// seeded random), it is Tainted and its binding cleared, and every later modification is refused.
    /// </summary>
    [Theory]
    [InlineData("an S14 T9 glyph onto a magic chest with only P11, from 0", ItemImprintResult.Ok)]
    [InlineData("the same onto a chest already attuned", ItemImprintResult.Ok)]
    [InlineData("an S14 T9 glyph onto an attuned rare at 70 carrying a sealed affix at 17", ItemImprintResult.Bricked)]
    public void Imprint_a_glyph_binding_the_item_or_bricking_it(string @case, ItemImprintResult expected)
    {
        bool bricks = expected == ItemImprintResult.Bricked;
        // The rare's Attuned stands for one a wake set: the brick clears it all the same.
        CarriedItem target = bricks
            ? Target(s_chest, ItemRarity.Rare, 9, identified: true, tainted: false, ItemInstanceFlags.Attuned, instability: 70,
                "implicit IncreasedMaximumLife T9", "P11 T7", "P12 T9 sealed 17", "S1 T9")
            : Target(s_chest, ItemRarity.Magic, 9, identified: true, tainted: false,
                @case.Contains("attuned", StringComparison.Ordinal) ? ItemInstanceFlags.Attuned : ItemInstanceFlags.None,
                instability: 0, "implicit IncreasedMaximumLife T9", "P11 T7");
        CarriedItem glyph = Glyph("S14 T9");
        var configuration = new GameConfiguration();
        configuration.Items.Imprint.InstabilityMinPct = 30;
        configuration.Items.Imprint.InstabilityMaxPct = 30;

        using var f = new Fixture(s_data, target, glyph, Gold, configuration);
        CharacterEntity character = f.Client.Character;
        ItemInstanceId id = target.Item.InstanceId;
        IReadOnlyList<ItemInstanceModifier> before = character.ItemModifiers.Of(id);
        ItemInstanceModifier glyphRow = Assert.Single(glyph.Modifiers);
        ItemModifierCatalog catalog = s_data.Items.Modifiers;

        Assert.Equal(expected, f.Imprint(f.Workbench.Guid, GlyphSlot, Slot));

        InventoryItem item = TestCharacters.At(character, InventoryType.Bag, Slot);
        IReadOnlyList<ItemInstanceModifier> after = character.ItemModifiers.Of(id);
        Assert.NotSame(before, after);
        Assert.Equal(before.Count + 1, after.Count);
        ItemInstanceModifier imprinted = after[^1];
        Assert.Equal((id, (byte)before.Count, true, (int?)null),
            (imprinted.ItemInstanceId, imprinted.Ordinal, imprinted.Imprinted, imprinted.SealProgress));
        Assert.NotEqual(glyphRow.Id, imprinted.Id);
        Assert.All(imprinted.Values, v => Assert.Equal(imprinted.Id, v.ItemInstanceModifierId));
        Assert.False(character.Container(InventoryType.Bag).TryGet(GlyphSlot, out _));
        Assert.Equal(0UL, character.Data!.Gold);

        SaveMarks marks = character.SaveState.TakeMarks();
        Assert.Equal((SaveState.Changed, true), (marks.Items[id].State, marks.Items[id].Modifiers));
        Assert.Equal(SaveState.Removed, marks.Items[glyph.Item.InstanceId].State);

        f.Flush();
        ItemSlotDto sent = Assert.Single(f.Updates()[^1].Slots, s => s.Container == InventoryType.Bag && s.Slot == Slot).Item!;

        if (!bricks)
        {
            Assert.Equal(glyphRow.ModifierId, imprinted.ModifierId);
            Assert.Equal(Values(glyphRow), Values(imprinted));
            for (int i = 0; i < before.Count; i++)
                Assert.Same(before[i], after[i]);

            Assert.Equal(30, character.ItemModifiers.InstabilityOf(id));
            Assert.Equal((ItemInstanceFlags.Attuned, false), (item.Flags, item.Tainted));
            Assert.Equal(
                after.Select(m => ((uint)m.ModifierId, m.Imprinted)),
                sent.Modifiers!.Select(m => (m.ModifierId, m.Imprinted)));
            Assert.Equal(ItemNames.Display(s_chest, ItemRarity.Magic, null, after, catalog), sent.Name);
            Assert.EndsWith("of Deep Roots", sent.Name, StringComparison.Ordinal);
            return;
        }

        Assert.Equal((ItemInstanceFlags.None, true), (item.Flags, item.Tainted));
        Assert.Equal(100, character.ItemModifiers.InstabilityOf(id));
        Assert.True(sent.Tainted);

        // Every prefix and suffix, the imprinted one included, sits on a row the brick offered its own group at item
        // level 9; the implicit is the same object; each row keeps its id, ordinal and seal, and the new one its mark.
        Assert.Same(before[0], after[0]);
        int[] offered = new int[ItemModifierCatalog.BrickTierCount];
        for (int i = 1; i < after.Count; i++)
        {
            ItemModifier was = catalog.Find(i < before.Count ? before[i].ModifierId : glyphRow.ModifierId)!;
            int n = catalog.BrickTiers(was, s_chest.BaseKind!.Value, 9, offered);
            Assert.Contains(after[i].ModifierId, offered[..n]);
            Assert.Equal(was.GroupId, catalog.Find(after[i].ModifierId)!.GroupId);
            if (i < before.Count)
            {
                Assert.Equal((before[i].Id, before[i].Ordinal, before[i].SealProgress),
                    (after[i].Id, after[i].Ordinal, after[i].SealProgress));
            }
        }

        Assert.Equal(17, after[2].SealProgress);
        Assert.True(after[^1].Imprinted);

        // Tainted for good: a new glyph's imprint and an extract are refused, and with its binding cleared it trades.
        Assert.Equal(InventoryAddResult.Ok, f.Economy.InventoryOf(character).TryAddInstanceAt(Glyph("S1 T8"), GlyphSlot));
        Assert.Equal(ItemImprintResult.Tainted, f.Imprint(f.Workbench.Guid, GlyphSlot, Slot));
        Assert.Equal(ItemExtractResult.Tainted, f.Extract(f.Workbench.Guid, Slot, after[1].ModifierId));
        Assert.True(TradeRules.IsTradeable(s_chest, item.Flags));
    }

    /// <summary>
    /// #1180 part C: an imprint is all or nothing. When the inventory service refuses an imprint ImprintRules allowed, or
    /// its roll throws, it has written nothing, and the handler gives the gold it spent back: the answer is InternalError,
    /// and neither item, nor the store's lists, nor the instability, nor the gold has changed. The refusal is a magic chest
    /// whose copy carries no rolled rows; the throw a rare at 99 carrying "S5 Fire T9", which never rolls on a chest, so
    /// the brick the imprint brings finds no tier to offer it. No game path makes either.
    /// </summary>
    [Theory]
    [InlineData("the service refuses: a copy carrying no rolled rows")]
    [InlineData("the roll throws: a brick finds no tier for S5 Fire on a chest")]
    public void Give_back_the_gold_of_an_imprint_that_fails_after_the_rules_allowed_it(string @case)
    {
        CarriedItem target = @case.StartsWith("the service refuses", StringComparison.Ordinal)
            ? TestCharacters.Carried(new InventoryItem(Slot, new ItemInstanceId(Guid.CreateVersion7()), s_chest.Id, 1, 0,
                ItemInstanceFlags.None, ItemRarity.Magic, 9, RareName: null, Identified: true, Tainted: false))
            : Target(s_chest, ItemRarity.Rare, 9, identified: true, tainted: false, ItemInstanceFlags.None, instability: 99,
                "implicit IncreasedMaximumLife T9", "P11 T7", "S5 Fire T9");
        using var f = new Fixture(s_data, target, Glyph("S14 T9"), Gold);
        CharacterEntity character = f.Client.Character;
        ItemInstanceId id = target.Item.InstanceId;
        IReadOnlyList<ItemInstanceModifier> rows = character.ItemModifiers.Of(id);
        ushort instability = character.ItemModifiers.InstabilityOf(id);
        InventoryItem held = TestCharacters.At(character, InventoryType.Bag, GlyphSlot);
        IReadOnlyList<ItemInstanceModifier> heldRows = character.ItemModifiers.Of(held.InstanceId);

        Assert.Equal(ItemImprintResult.InternalError, f.Imprint(f.Workbench.Guid, GlyphSlot, Slot));

        Assert.Equal(target.Item, TestCharacters.At(character, InventoryType.Bag, Slot));
        Assert.Equal(held, TestCharacters.At(character, InventoryType.Bag, GlyphSlot));
        Assert.Same(rows, character.ItemModifiers.Of(id));
        Assert.Same(heldRows, character.ItemModifiers.Of(held.InstanceId));
        Assert.Equal(instability, character.ItemModifiers.InstabilityOf(id));
        Assert.Equal(Gold, character.Data!.Gold);
        SaveMarks marks = character.SaveState.TakeMarks();
        Assert.Empty(marks.Items);
        Assert.Empty(marks.Slots);
    }

    /// <summary>
    /// Points the request at the Workbench, or breaks the Workbench's shared prefix as each flag says: not found (by the
    /// case: a chest's guid, a despawned Workbench, otherwise an unknown guid), too far (by the case: a NaN position,
    /// otherwise 5.01 m away), stunned and dead. Answers the guid the request names.
    /// </summary>
    private static ObjectGuid AtWorkbench(Fixture f, string @case, bool notFound, bool tooFar, bool stunned, bool dead)
    {
        CharacterEntity character = f.Client.Character;
        if (tooFar)
            character.Position = @case == "position NaN" ? new Vector3(float.NaN, 0f, 0f) : new Vector3(4.99f, 0f, 0f);

        ObjectGuid workbench = f.Workbench.Guid;
        if (notFound)
        {
            switch (@case)
            {
                case "a chest's guid":
                    workbench = f.Chest.Guid;
                    break;
                case "despawned workbench":
                    DoodadStates.Set(f.Workbench, DoodadState.Despawned);
                    break;
                default:
                    workbench = new ObjectGuid(ObjectType.Doodad, 999);
                    break;
            }
        }

        if (stunned)
            character.Auras.Add(Auras.AuraTestData.HeldControl(CrowdControl.Stun), DateTimeOffset.UnixEpoch);
        if (dead)
            character.IsDead = true;

        return workbench;
    }

    /// <summary>
    /// A copy of <paramref name="template" /> in Bag slot 3 holding <paramref name="instability" />, its rows the seeded
    /// catalog keys given, in order from ordinal 0, each line at its low end; a key ending "sealed" is sealed at 5, and one
    /// ending "sealed N" at N.
    /// </summary>
    private static CarriedItem Target(ItemTemplate template, ItemRarity rarity, ushort itemLevel, bool identified,
        bool tainted, ItemInstanceFlags flags, ushort instability, params string[] affixes)
    {
        var id = new ItemInstanceId(Guid.CreateVersion7());
        var item = new InventoryItem(Slot, id, template.Id, 1, 0, flags, rarity, itemLevel,
            rarity == ItemRarity.Rare ? "Grim Shell" : null, identified, Tainted: tainted);
        List<ItemInstanceModifier> rows = [];
        foreach (string affix in affixes)
        {
            int at = affix.IndexOf(" sealed", StringComparison.Ordinal);
            ItemInstanceModifier row = LowEnd(id, CatalogKeys.ModifierId(at < 0 ? affix : affix[..at]), (byte)rows.Count);
            if (at >= 0)
                row.SealProgress = affix.Length > at + 7 ? int.Parse(affix[(at + 8)..], CultureInfo.InvariantCulture) : 5;
            rows.Add(row);
        }

        return new CarriedItem(item, new ItemBaseStats { Armour = 8 }, rows, instability);
    }

    /// <summary>
    /// A glyph in Bag slot 5 as extraction makes one: Magic, identified, Attuned, at its affix's required item level,
    /// holding one row of the seeded <paramref name="affix" />, each line at its low end, at ordinal 0; naming
    /// <paramref name="modifierId" /> instead when given, one the catalog does not hold.
    /// </summary>
    private static CarriedItem Glyph(string affix, int? modifierId = null)
    {
        var id = new ItemInstanceId(Guid.CreateVersion7());
        int key = CatalogKeys.ModifierId(affix);
        ItemInstanceModifier row = LowEnd(id, key, 0);
        row.ModifierId = modifierId ?? key;
        var item = new InventoryItem(GlyphSlot, id, s_data.Items.Glyph!.Id, 1, 0, ItemInstanceFlags.Attuned,
            ItemRarity.Magic, s_data.Items.Modifiers.Find(key)!.RequiredItemLevel, null, true, Tainted: false);
        return new CarriedItem(item, default, [row], 0);
    }

    /// <summary>A row of the seeded modifier <paramref name="modifierId" /> on <paramref name="item" />, each line at its low end.</summary>
    private static ItemInstanceModifier LowEnd(ItemInstanceId item, int modifierId, byte ordinal) =>
        Modifier(item, modifierId, ordinal,
        [
            .. s_data.Items.Modifiers.Find(modifierId)!.Stats.OrderBy(l => l.Index)
                .Select(l => (l.Index, l.Stat, l.Min, l.Min2)),
        ]);

    private static (byte, StatType, int, int?)[] Values(ItemInstanceModifier row) =>
        [.. row.Values.Select(v => (v.Index, v.Stat, v.Value, v.Value2))];

    /// <summary>
    /// A copy of <paramref name="template" /> at item level 9: its implicit, "P11 T7" (sealed when asked) and "S14 T9", and
    /// its rolled armour; a Tainted copy holds the hidden instability that tainted it.
    /// </summary>
    private static CarriedItem Chestguard(ItemTemplate template, ItemRarity rarity, bool identified, bool tainted,
        bool sealedPrefix)
    {
        var id = new ItemInstanceId(Guid.CreateVersion7());
        var item = new InventoryItem(Slot, id, template.Id, 1, 0, ItemInstanceFlags.None, rarity, 9, null, identified,
            Tainted: tainted);
        ItemInstanceModifier prefix = Modifier(id, CatalogKeys.ModifierId("P11 T7"), 1, (0, StatType.Health, 5, null));
        if (sealedPrefix)
            prefix.SealProgress = 0;

        return new CarriedItem(item, new ItemBaseStats { Armour = 8 },
        [
            Modifier(id, CatalogKeys.ModifierId("implicit IncreasedMaximumLife T9"), 0,
                (0, StatType.IncreasedMaximumLife, 2, null)),
            prefix,
            Modifier(id, CatalogKeys.ModifierId("S14 T9"), 2, (0, StatType.IncreasedMaximumLife, 1, null)),
        ], tainted ? (ushort)130 : (ushort)0);
    }

    /// <summary>
    /// An identified, attuned Rare Ironbound Helm at item level 9, its instability 40, well short of tainting: its implicit,
    /// "P11 T7", "S14 T9", and "S5 Fire T8" imprinted from a glyph.
    /// </summary>
    private static CarriedItem ImprintedHelm()
    {
        var id = new ItemInstanceId(Guid.CreateVersion7());
        var item = new InventoryItem(Slot, id, s_helm.Id, 1, 0, ItemInstanceFlags.Attuned, ItemRarity.Rare, 9,
            "Grim Visor", true, Tainted: false);
        ItemModifier @implicit = s_data.Items.Modifiers.LinkedTo(s_helm.Id).Single();
        ItemModifierStat line = @implicit.Stats[0];
        ItemInstanceModifier imprinted =
            Modifier(id, CatalogKeys.ModifierId("S5 Fire T8"), 3, (0, StatType.FireResistance, 9, null));
        imprinted.Imprinted = true;

        return new CarriedItem(item, new ItemBaseStats { Armour = 5 },
        [
            Modifier(id, @implicit.Id, 0, (0, line.Stat, line.Min, line.Min2)),
            Modifier(id, CatalogKeys.ModifierId("P11 T7"), 1, (0, StatType.Health, 5, null)),
            Modifier(id, CatalogKeys.ModifierId("S14 T9"), 2, (0, StatType.IncreasedMaximumLife, 1, null)),
            imprinted,
        ], 40);
    }

    private static ItemTemplate NoDestroyCopyOf(ItemTemplate template, ulong id) => new()
    {
        Id = new ItemTemplateId(id),
        Name = template.Name,
        Class = template.Class,
        SubClass = template.SubClass,
        Flags = template.Flags | ItemTemplateFlags.NoDestroy,
        MaxStackSize = template.MaxStackSize,
        DisplayId = template.DisplayId,
        Rarity = template.Rarity,
        BaseKind = template.BaseKind,
        Slot = template.Slot,
        BaseStats = template.BaseStats,
    };

    /// <summary>
    /// The instance with the seeded Workbench at x 10 and a forest chest (doodad 1) beside it, the character 4 m from the
    /// Workbench holding the item it is given in Bag slot 3, the glyph (or any second item) it is given in Bag slot 5, and
    /// the gold it is given (25 by default), already sent once and saved, so a request starts from a character with
    /// nothing to save or send. The world's configuration (imprint costs and instability) is the test's to choose; the
    /// roller draws from a seeded random.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        private readonly ItemModifierDtoPool _pool = new();
        private readonly StaticData _data;

        public Fixture(StaticData data, CarriedItem carried, CarriedItem? glyph = null, ulong gold = Gold,
            GameConfiguration? configuration = null)
        {
            _data = data;
            configuration ??= new GameConfiguration();
            IWorld world = NewWorld(data);
            world.Configuration.Returns(configuration);
            Instance = TestMapInstances.Build(world, time: new FakeTimeProvider());
            world.InstanceRegistry.GetInstanceById(Instance.InstanceId).Returns(Instance);
            Workbench = Place(2, s_workbench, s_atWorkbench);
            Chest = Place(1, s_forestChest, new Vector3(8f, 0f, 2f));

            CharacterEntity character = TestCharacters.New(Me, gold);
            character.Position = s_beside;
            Client = Join(Instance, character);

            var ids = new ItemIdAllocator();
            var roller = new ItemRoller(new LootRandom(new Random(1180)), ids, Options.Create(configuration));
            Economy = new CharacterEconomy(world, ids, roller);
            Handler = new ItemExtractHandler(NullLogger<ItemExtractHandler>.Instance, world, Economy);
            Imprinter = new ItemImprintHandler(NullLogger<ItemImprintHandler>.Instance, world, Economy);
            Assert.Equal(InventoryAddResult.Ok, Economy.InventoryOf(character).TryAddInstanceAt(carried, Slot));
            if (glyph is { } held)
                Assert.Equal(InventoryAddResult.Ok, Economy.InventoryOf(character).TryAddInstanceAt(held, GlyphSlot));

            // The tick that sends it, and the save that writes it.
            Flush();
            character.SaveState.Acknowledge(character.SaveState.TakeMarks());
        }

        public MapInstance Instance { get; }
        public MapInstanceClient Client { get; }
        public Doodad Workbench { get; }
        public Doodad Chest { get; }
        public ItemExtractHandler Handler { get; }
        public ItemImprintHandler Imprinter { get; }
        public CharacterEconomy Economy { get; }

        /// <summary>One request, and the one answer it got, its request id echoed.</summary>
        public ItemExtractResult Extract(ObjectGuid workbench, byte bagSlot, int modifierId)
        {
            int before = Results().Count;
            var packet = new CItemExtractPacket
            {
                RequestId = RequestId,
                WorkbenchGuid = workbench.RawValue,
                BagSlot = bagSlot,
                ModifierId = (uint)modifierId,
            };
            Handler.Execute(Client.Connection, packet);

            List<SItemExtractResultPacket> results = Results();
            Assert.Equal(before + 1, results.Count);
            Assert.Equal(RequestId, results[^1].RequestId);
            return results[^1].Result;
        }

        /// <summary>One imprint request, and the one answer it got, its request id echoed.</summary>
        public ItemImprintResult Imprint(ObjectGuid workbench, byte glyphSlot, byte targetSlot)
        {
            int before = ImprintResults().Count;
            Imprinter.Execute(Client.Connection, new CItemImprintPacket
            {
                RequestId = RequestId,
                WorkbenchGuid = workbench.RawValue,
                GlyphBagSlot = glyphSlot,
                TargetBagSlot = targetSlot,
            });

            List<SItemImprintResultPacket> results = ImprintResults();
            Assert.Equal(before + 1, results.Count);
            Assert.Equal(RequestId, results[^1].RequestId);
            return results[^1].Result;
        }

        /// <summary>The tick's inventory flush, over the current items generation.</summary>
        public void Flush() => InventoryUpdateFlusher.Flush(Client.Connection, _data.Items, _pool);

        public List<SInventoryUpdatePacket> Updates() =>
            Client.Read<SInventoryUpdatePacket>(NetworkPacketType.SMSG_INVENTORY_UPDATE);

        public void Dispose() => Instance.Dispose();

        private List<SItemExtractResultPacket> Results() =>
            Client.Read<SItemExtractResultPacket>(NetworkPacketType.SMSG_ITEM_EXTRACT_RESULT);

        private List<SItemImprintResultPacket> ImprintResults() =>
            Client.Read<SItemImprintResultPacket>(NetworkPacketType.SMSG_ITEM_IMPRINT_RESULT);

        private Doodad Place(uint id, DoodadTemplateView template, Vector3 position)
        {
            var doodad = new Doodad(new ObjectGuid(ObjectType.Doodad, id), template, position, 0f);
            Instance.AddDoodad(doodad);
            return doodad;
        }
    }
}
