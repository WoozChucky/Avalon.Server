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
/// Extraction at the Resonance Workbench (#1180 part C), through the real handler, a real instance with the seeded
/// Workbench placed in it, the seeded templates and catalog, and the economy's own inventory service: every request gets
/// one SMSG_ITEM_EXTRACT_RESULT, a refusal names the first rule it breaks and changes nothing, and an extract destroys the
/// item and puts a bound glyph holding the affix in its slot. Character id 117_902 is this class's.
/// </summary>
public class ItemExtractionShould
{
    private static readonly StaticData s_data = ScenarioReferenceData.Shared.Data;
    private static readonly ItemTemplate s_chest = s_data.Items.Templates.Single(t => t.Name == "Ironbound Chestguard");
    private static readonly ItemTemplate s_helm = s_data.Items.Templates.Single(t => t.Name == "Ironbound Helm");
    private static readonly ItemTemplate s_unique = s_data.Items.Find(new ItemTemplateId(65))!;

    private static readonly DoodadTemplateView s_workbench =
        s_data.Doodads.Templates.Single(t => t.Name == "Resonance Workbench");

    private static readonly DoodadTemplateView s_forestChest = s_data.Doodads.Templates.Single(t => t.Name == "Forest Chest");

    private const uint Me = 117_902, RequestId = 43;
    private const byte Slot = 3;
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
            "Grim Visor", true);
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
    /// Workbench holding the item it is given in Bag slot 3 and 25 gold, already sent once and saved, so a request
    /// starts from a character with nothing to save or send.
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        private readonly ItemModifierDtoPool _pool = new();
        private readonly StaticData _data;

        public Fixture(StaticData data, CarriedItem carried)
        {
            _data = data;
            IWorld world = NewWorld(data);
            Instance = TestMapInstances.Build(world, time: new FakeTimeProvider());
            world.InstanceRegistry.GetInstanceById(Instance.InstanceId).Returns(Instance);
            Workbench = Place(2, s_workbench, s_atWorkbench);
            Chest = Place(1, s_forestChest, new Vector3(8f, 0f, 2f));

            CharacterEntity character = TestCharacters.New(Me, Gold);
            character.Position = s_beside;
            Client = Join(Instance, character);

            var ids = new ItemIdAllocator();
            var roller = new ItemRoller(new LootRandom(new Random(1180)), ids, Options.Create(new GameConfiguration()));
            var economy = new CharacterEconomy(world, ids, roller);
            Handler = new ItemExtractHandler(NullLogger<ItemExtractHandler>.Instance, world, economy);
            Assert.Equal(InventoryAddResult.Ok, economy.InventoryOf(character).TryAddInstanceAt(carried, Slot));

            // The tick that sends it, and the save that writes it.
            Flush();
            character.SaveState.Acknowledge(character.SaveState.TakeMarks());
        }

        public MapInstance Instance { get; }
        public MapInstanceClient Client { get; }
        public Doodad Workbench { get; }
        public Doodad Chest { get; }
        public ItemExtractHandler Handler { get; }

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

        /// <summary>The tick's inventory flush, over the current items generation.</summary>
        public void Flush() => InventoryUpdateFlusher.Flush(Client.Connection, _data.Items, _pool);

        public List<SInventoryUpdatePacket> Updates() =>
            Client.Read<SInventoryUpdatePacket>(NetworkPacketType.SMSG_INVENTORY_UPDATE);

        public void Dispose() => Instance.Dispose();

        private List<SItemExtractResultPacket> Results() =>
            Client.Read<SItemExtractResultPacket>(NetworkPacketType.SMSG_ITEM_EXTRACT_RESULT);

        private Doodad Place(uint id, DoodadTemplateView template, Vector3 position)
        {
            var doodad = new Doodad(new ObjectGuid(ObjectType.Doodad, id), template, position, 0f);
            Instance.AddDoodad(doodad);
            return doodad;
        }
    }
}
