using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
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
using Avalon.World.Items;
using Avalon.World.Loot;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Reload;
using Avalon.World.Testing;
using Avalon.World.Testing.Scenarios;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Performance;

/// <summary>
/// The tick's inventory update and the answer to an item request allocate the entries their packet carries and
/// nothing else (#883): no query, no closure, no list on the way. What the entries cost is measured here, by building
/// the same ones, rather than written down, so the pin holds on any runtime.
/// </summary>
/// <remarks>
/// Both paths send, and a send takes its payload segment from the process's one pool and this connection gives it
/// straight back. A test sending on another thread can take that free segment in between, and then the next send
/// here allocates a new one (200 B, which failed this pin in a whole-suite run). So these run in the collection that
/// runs alone in this assembly, and each figure is the fewest of three windows, which nothing but the path itself
/// can lower.
/// </remarks>
[Collection(nameof(ScenarioAllocations))]
public class InventoryAllocationShould
{
    private const int Rounds = 100;

    // Out of order, with one slot empty and one named twice: a potion, a sword, a seeded plate chest carrying its base
    // stats and its implicit and a magic one carrying its base stats, its implicit and its affixes (#1179).
    private static readonly SlotRef[] s_slots =
    [
        new(InventoryType.Bag, 3), new(InventoryType.Bag, 9), new(InventoryType.Bag, 1), new(InventoryType.Bag, 2),
        new(InventoryType.Bag, 0), new(InventoryType.Bag, 3),
    ];

    private const int Named = 5;

    // The seeded plate chest and the real catalog, so the copies carry real implicit and affix rows.
    private static readonly ItemTemplate s_chest = ScenarioReferenceData.Shared.Data.Items.Find(new ItemTemplateId(37))!;
    private static readonly ItemsPatch s_items = new([Potion, Sword, s_chest], ScenarioReferenceData.Shared.Data.Items.Modifiers);

    // The flush's and the replies' modifier entries, as WorldServer and the item handlers own theirs.
    private readonly ItemModifierDtoPool _pool = new();

    private readonly CharacterEntity _character = New();
    private readonly QuietConnection _connection;

    public InventoryAllocationShould()
    {
        _character.Container(InventoryType.Bag).Load([Item(0, Potion, count: 5), Item(3, Sword)]);
        var roller = new ItemRoller(new LootRandom(new Random(1179)), new ItemIdAllocator(), Options.Create(new GameConfiguration()));
        CharacterInventoryService inventory = InventoryFor(_character, s_items.Find);
        Assert.Equal(InventoryAddResult.Ok, inventory.TryAddRolled(roller.RollPlain(s_chest, 1, s_items)));
        RolledItem magic = roller.Roll(s_chest, new DropContext(10, CreatureRarity.Normal, ForceMagic: true), s_items)!;
        Assert.Equal(InventoryAddResult.Ok, inventory.TryAddRolled(magic));
        // It arrives unidentified; held identified here, so it is described by its affixes and its name.
        HoldMagicChest(identified: true);
        Assert.NotNull(_character.ItemModifiers.Describe(At(_character, InventoryType.Bag, 2), s_items).Name);
        // Both chests send their base stats, from the pool as their modifiers are.
        Assert.All([1, 2], slot => Assert.False(
            _character.ItemModifiers.BaseStatsOf(At(_character, InventoryType.Bag, (byte)slot).InstanceId).IsEmpty));
        _character.ClientChanges.Clear();
        _connection = new QuietConnection(_character);
    }

    [Fact]
    public void Flush_a_ticks_changes_allocating_only_the_entries_sent()
    {
        long goldOnly = Allocated(() =>
        {
            _character.ClientChanges.RecordGold();
            InventoryUpdateFlusher.Flush(_connection, s_items, _pool);
        });
        long slots = Allocated(FlushTheSlots);
        // #1179: the same copies with the magic chest held unidentified, its modifiers' kinds looked up and its affixes
        // and name left out: from the same pool, so it costs no more.
        HoldMagicChest(identified: false);
        long unidentified = Allocated(FlushTheSlots);

        Assert.Equal(0, goldOnly);
        Assert.Equal(Allocated(() => Entries(Named)), slots);
        Assert.True(unidentified <= slots, $"held unidentified, the flush allocated {unidentified} B against {slots} B");
        Assert.Equal(12 * Rounds, _connection.Sent);
    }

    private void FlushTheSlots()
    {
        foreach (SlotRef slot in s_slots)
            _character.ClientChanges.RecordSlot(slot.Container, slot.Slot);
        InventoryUpdateFlusher.Flush(_connection, s_items, _pool);
    }

    /// <summary>The magic chest in Bag slot 2, held identified or not (#1179).</summary>
    private void HoldMagicChest(bool identified)
    {
        InventoryItem chest = At(_character, InventoryType.Bag, 2);
        Assert.Equal(ItemRarity.Magic, chest.Rarity);
        _character.Container(InventoryType.Bag).Put(chest with { Identified = identified });
    }

    [Fact]
    public void Answer_an_item_request_allocating_only_the_slots_a_refusal_names()
    {
        long accepted = Allocated(() => ItemRequestReply.Send(_connection, _character, 1, ItemRequestResult.Ok, s_slots, s_items, _pool));
        long refused = Allocated(() =>
            ItemRequestReply.Send(_connection, _character, 2, ItemRequestResult.TargetFull, s_slots, s_items, _pool));

        Assert.Equal(0, accepted);
        Assert.Equal(Allocated(() => Entries(Named)), refused);
        Assert.Equal(8 * Rounds, _connection.Sent);
    }

    /// <summary>
    /// The paths that go through the character's economy (#883): an item request, and what a pickup, a vendor trade
    /// and a quest do to the Bag (an add, a removal by template, the count a Unique item is checked by). The economy
    /// is production's, so the service it hands out and its template lookup are in the figure.
    /// </summary>
    [Fact]
    public async Task Change_items_through_the_economy_allocating_nothing()
    {
        var world = new QuietWorld(await TestStaticData.LoadAsync(items: [Potion, Sword, Relic]));
        CharacterEconomy economy = TestEconomy.Over(world);
        var move = new ItemMoveHandler(NullLogger<ItemMoveHandler>.Instance, world, economy);
        _character.Container(InventoryType.Bag).Load([Item(0, Potion, count: 5), Item(3, Sword), Item(4, Relic)]);
        CItemMovePacket there = Move(0, 1);
        CItemMovePacket back = Move(1, 0);

        long moved = Allocated(() =>
        {
            move.Execute(_connection, there);
            move.Execute(_connection, back);
        });
        long traded = Allocated(() =>
        {
            economy.InventoryOf(_character).TryAdd(Potion.Id, 3);
            economy.InventoryOf(_character).TryRemove(Potion.Id, 3);
        });
        long fitted = Allocated(() => economy.InventoryOf(_character).CanAdd(Potion.Id, 3));
        long owned = Allocated(() => economy.InventoryOf(_character).CanAdd(Relic.Id, 1));

        Assert.Multiple(
            () => Assert.Equal(0, moved),
            () => Assert.Equal(0, traded),
            () => Assert.Equal(0, fitted),
            () => Assert.Equal(0, owned));
        Assert.Equal(InventoryAddResult.UniqueAlreadyOwned, economy.InventoryOf(_character).CanAdd(Relic.Id, 1));
        Assert.Equal(5u, At(_character, InventoryType.Bag, 0).Count);
        Assert.Equal(2 * 4 * Rounds, _connection.Sent);
    }

    /// <summary>
    /// An identify request at the Resonance Workbench (#1179) allocates nothing beyond its one reply, whether it is
    /// refused (here AlreadyIdentified, the last rule before gold) or done, at no cost as v1 ships it and at a cost: no
    /// wallet is taken when there is no gold to ask for, the character's own is when there is, and the Workbench is
    /// looked up by the guid the packet carries. At the same Workbench (#1180 part C), an extract request naming the chest's
    /// implicit walks every extract rule and is refused at the last (NotExtractable), and an imprint request of an
    /// "S14 T9" glyph onto a magic chest with room for it walks every imprint rule and is refused at the last
    /// (NotEnoughGold), each allocating nothing either.
    /// </summary>
    [Theory]
    [InlineData(0UL)]
    [InlineData(10UL)]
    public void Answer_workbench_requests_allocating_nothing(ulong cost)
    {
        StaticData data = ScenarioReferenceData.Shared.Data;
        using MapInstance instance = TestMapInstances.Build(new QuietWorld(data));
        var workbench = new Doodad(new ObjectGuid(ObjectType.Doodad, 1),
            data.Doodads.Templates.Single(t => t.Service == DoodadService.Workbench), _character.Position, 0f);
        instance.AddDoodad(workbench);
        _character.InstanceId = instance.InstanceId;
        var world = new QuietWorld(data, instance);
        world.Configuration.Items.IdentifyCost = cost;
        // Enough for every request the windows send.
        _character.Data!.Gold = 1_000_000;
        CharacterEconomy economy = TestEconomy.Over(world);
        var handler = new ItemIdentifyHandler(NullLogger<ItemIdentifyHandler>.Instance, world, economy);
        var identify = new CItemIdentifyPacket { RequestId = 9, WorkbenchGuid = workbench.Guid.RawValue, BagSlot = 2 };

        // Put back unidentified before each request, as the item it was.
        HoldMagicChest(identified: false);
        InventoryItem unidentified = At(_character, InventoryType.Bag, 2);
        CharacterInventoryContainer bag = _character.Container(InventoryType.Bag);

        long identified = Allocated(() =>
        {
            bag.Put(unidentified);
            handler.Execute(_connection, identify);
        });
        Assert.True(At(_character, InventoryType.Bag, 2).Identified);
        Assert.Equal(1_000_000 - cost * 4 * Rounds, _character.Data.Gold);
        long refused = Allocated(() => handler.Execute(_connection, identify));

        int implicitId = _character.ItemModifiers.Of(unidentified.InstanceId)
            .First(m => data.Items.Modifiers.Find(m.ModifierId)?.Group.Kind == ItemModifierKind.Implicit).ModifierId;
        var extractor = new ItemExtractHandler(NullLogger<ItemExtractHandler>.Instance, world, economy);
        var extract = new CItemExtractPacket
        {
            RequestId = 10,
            WorkbenchGuid = workbench.Guid.RawValue,
            BagSlot = 2,
            ModifierId = (uint)implicitId,
        };
        Assert.Equal(ItemExtractResult.NotExtractable,
            ExtractRules.Decide(_character, instance, extract.WorkbenchGuid, extract.BagSlot, extract.ModifierId, data.Items));
        long extractRefused = Allocated(() => extractor.Execute(_connection, extract));

        // A magic chest carrying its implicit and "P11 T7" in slot 4, and an "S14 T9" glyph in slot 5, with no gold for
        // the glyph's 25.
        CarriedItem target = Carried(4, s_chest.Id, ItemRarity.Magic, 10, ItemInstanceFlags.None,
            data.Items.Modifiers.LinkedTo(s_chest.Id).Single().Id, CatalogKeys.ModifierId("P11 T7"));
        CarriedItem glyph = Carried(5, data.Items.Glyph!.Id, ItemRarity.Magic, 1, ItemInstanceFlags.Attuned,
            CatalogKeys.ModifierId("S14 T9"));
        Assert.Equal(InventoryAddResult.Ok, economy.InventoryOf(_character).TryAddInstanceAt(target, 4));
        Assert.Equal(InventoryAddResult.Ok, economy.InventoryOf(_character).TryAddInstanceAt(glyph, 5));
        _character.Data.Gold = 0;
        var imprinter = new ItemImprintHandler(NullLogger<ItemImprintHandler>.Instance, world, economy);
        var imprint = new CItemImprintPacket
        {
            RequestId = 11,
            WorkbenchGuid = workbench.Guid.RawValue,
            GlyphBagSlot = 5,
            TargetBagSlot = 4,
        };
        Assert.Equal(ItemImprintResult.NotEnoughGold, ImprintRules.Decide(_character, instance, imprint.WorkbenchGuid,
            imprint.GlyphBagSlot, imprint.TargetBagSlot, data.Items, world.Configuration.Items.ImprintCost,
            economy.WalletOf(_character), out _));
        long imprintRefused = Allocated(() => imprinter.Execute(_connection, imprint));

        Assert.Multiple(
            () => Assert.Equal(0, identified),
            () => Assert.Equal(0, refused),
            () => Assert.Equal(0, extractRefused),
            () => Assert.Equal(0, imprintRefused));
        Assert.Equal(4 * 4 * Rounds, _connection.Sent);
    }

    /// <summary>
    /// An identified copy of <paramref name="template" /> in Bag slot <paramref name="slot" />, carrying one row of each
    /// seeded modifier given, in order from ordinal 0, each line at its low end.
    /// </summary>
    private static CarriedItem Carried(byte slot, ItemTemplateId template, ItemRarity rarity, ushort itemLevel,
        ItemInstanceFlags flags, params int[] modifierIds)
    {
        var id = new ItemInstanceId(Guid.CreateVersion7());
        ItemModifierCatalog catalog = ScenarioReferenceData.Shared.Data.Items.Modifiers;
        List<ItemInstanceModifier> rows = [];
        foreach (int modifierId in modifierIds)
        {
            rows.Add(TestModifiers.Modifier(id, modifierId, (byte)rows.Count,
                [.. catalog.Find(modifierId)!.Stats.Select(l => (l.Index, l.Stat, l.Min, l.Min2))]));
        }

        return new CarriedItem(new InventoryItem(slot, id, template, 1, 0, flags, rarity, itemLevel, null, true),
            default, rows, 0);
    }

    private static CItemMovePacket Move(byte from, byte to) => new()
    {
        RequestId = 7,
        FromContainer = InventoryType.Bag,
        FromSlot = from,
        ToContainer = InventoryType.Bag,
        ToSlot = to,
    };

    /// <summary>
    /// What <see cref="Rounds" /> calls allocate on this thread: the fewest of three windows, after one to warm up.
    /// </summary>
    private static long Allocated(Action round) => ThreadAllocations.FewestOfThreeWindows(Rounds, round);

    /// <summary>
    /// The entries a packet naming the first <paramref name="count" /> slots carries: the array, and each slot with its
    /// item, and nothing for the items' modifiers, base stats and names (#1179), which the pool and the store already hold.
    /// </summary>
    private InventorySlotUpdateDto[] Entries(int count)
    {
        var entries = new InventorySlotUpdateDto[count];
        for (int i = 0; i < count; i++)
        {
            SlotRef slot = s_slots[i];
            entries[i] = new InventorySlotUpdateDto
            {
                Container = slot.Container,
                Slot = slot.Slot,
                Item = _character.Container(slot.Container).TryGet(slot.Slot, out _) ? new ItemSlotDto() : null,
            };
        }

        return entries;
    }
}
