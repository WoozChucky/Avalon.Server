using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Seeding;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Doodads;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Avalon.World.Maps.Navigation;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using Avalon.World.Scripts;
using Avalon.World.Testing.Fakes;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProtoBuf;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// What a refused Resonance Workbench request costs (#1180 part C): each request through its real handler, the
/// Workbench's shared prefix (found in a real instance, in range) and its own rules, to the one reply it is answered
/// with, over the seeded reference data. The character holds an identified magic plate chest at item level 10 in Bag
/// slot 0, carrying its implicit and "P11 T7", and an "S14 T9" glyph in Bag slot 1, with no gold.
/// <see cref="Refused_identify" /> (the baseline, #1179) names the chest and is answered AlreadyIdentified.
/// <see cref="Refused_extract" /> names the chest's implicit and walks every extract rule to NotExtractable, where the row
/// walk finds the implicit and refuses it.
/// <see cref="Refused_imprint" /> imprints the glyph onto the chest, which has room for it, and walks every imprint rule
/// to the last, NotEnoughGold (the glyph's tier costs 25). 0 B expected on every row: a refusal changes nothing and
/// allocates nothing beyond its pooled reply.
/// </summary>
/// <remarks>
/// Setup fails the run unless each row's first call is answered once, with its intended result, as read back from the
/// reply the connection was sent; and cleanup fails it unless every row left the character's items, gold and pending
/// client changes as they were. The economy is production's, so the inventory service and the wallet it keeps for the
/// character are in the figure, as they are for every request.
/// </remarks>
[MemoryDiagnoser]
public class WorkbenchRequestBenchmarks
{
    private const byte ChestSlot = 0;
    private const byte GlyphSlot = 1;

    // The seeded plate chest, a rolled base whose implicit is linked to it.
    private static readonly ItemTemplateId s_chest = new(37);

    private ServiceProvider _services = null!;
    private MapInstance _instance = null!;
    private CharacterEntity _character = null!;
    private QuietConnection _connection = null!;
    private ItemIdentifyHandler _identifier = null!;
    private ItemExtractHandler _extractor = null!;
    private ItemImprintHandler _imprinter = null!;
    private CItemIdentifyPacket _identify = null!;
    private CItemExtractPacket _extract = null!;
    private CItemImprintPacket _imprint = null!;
    private InventoryItem _chestBefore;
    private InventoryItem _glyphBefore;

    [GlobalSetup]
    public void Setup()
    {
        StaticData data = ScenarioReferenceData.Shared.Data;
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new CombatConfig());
        services.AddSingleton<IScriptManager>(new ScriptManager(NullLoggerFactory.Instance));
        services.AddSingleton<IChunkLibrary>(new AnyChunkLibrary());
        _services = services.BuildServiceProvider();

        var entry = new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero);
        var layout = new ChunkLayout(Seed: 0, Chunks: [entry], EntryChunk: entry, BossChunk: null, Portals: [],
            EntrySpawnWorldPos: Vector3.zero, CellSize: 30f, Config: null);
        _instance = new MapInstance(NullLoggerFactory.Instance, _services, new QuietWorld(data), new MapTemplateId(1),
            ownerCharacterId: null, layout, new MapNavigator(NullLoggerFactory.Instance), seed: 0, mapType: MapType.Normal);
        var workbench = new Doodad(new ObjectGuid(ObjectType.Doodad, 1),
            data.Doodads.Templates.Single(t => t.Service == DoodadService.Workbench), Vector3.zero, 0f);
        _instance.AddDoodad(workbench);

        var row = new Character
        {
            Id = new CharacterId(1),
            AccountId = new AccountId(1),
            Name = "Workbench",
            Class = CharacterClass.Warrior,
            Level = 1,
            CreationDate = DateTime.UtcNow,
            Gold = 0,
        };
        _character = new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration()) { Data = row };
        _character.InstanceId = _instance.InstanceId;
        int implicitId = HoldChestAndGlyph(data);
        _connection = new QuietConnection(_character);

        var world = new QuietWorld(data, _instance);
        var ids = new ItemIdAllocator();
        var economy = new CharacterEconomy(world, ids,
            new ItemRoller(new LootRandom(new Random(0)), ids, Options.Create(new GameConfiguration())));
        _identifier = new ItemIdentifyHandler(NullLogger<ItemIdentifyHandler>.Instance, world, economy);
        _extractor = new ItemExtractHandler(NullLogger<ItemExtractHandler>.Instance, world, economy);
        _imprinter = new ItemImprintHandler(NullLogger<ItemImprintHandler>.Instance, world, economy);

        ulong guid = workbench.Guid.RawValue;
        _identify = new CItemIdentifyPacket { RequestId = 1, WorkbenchGuid = guid, BagSlot = ChestSlot };
        _extract = new CItemExtractPacket { RequestId = 2, WorkbenchGuid = guid, BagSlot = ChestSlot, ModifierId = (uint)implicitId };
        _imprint = new CItemImprintPacket { RequestId = 3, WorkbenchGuid = guid, GlyphBagSlot = GlyphSlot, TargetBagSlot = ChestSlot };

        _chestBefore = Held(ChestSlot);
        _glyphBefore = Held(GlyphSlot);
        if (world.Configuration.Items.ImprintCost.GetValueOrDefault((byte)9) == 0)
            throw new InvalidOperationException("A T9 glyph costs no gold, so the imprint row cannot be refused NotEnoughGold.");

        _connection.KeepLastPayload = true;
        Refused_identify();
        Expect(Reply<SItemIdentifyResultPacket>().Result, ItemIdentifyResult.AlreadyIdentified);
        Refused_extract();
        Expect(Reply<SItemExtractResultPacket>().Result, ItemExtractResult.NotExtractable);
        Refused_imprint();
        Expect(Reply<SItemImprintResultPacket>().Result, ItemImprintResult.NotEnoughGold);
        _connection.KeepLastPayload = false;
        if (_connection.Sent != 3)
            throw new InvalidOperationException($"Three requests were answered with {_connection.Sent} replies.");

        Unchanged();
    }

    [Benchmark(Baseline = true)]
    public int Refused_identify()
    {
        _identifier.Execute(_connection, _identify);
        return _connection.Sent;
    }

    [Benchmark]
    public int Refused_extract()
    {
        _extractor.Execute(_connection, _extract);
        return _connection.Sent;
    }

    [Benchmark]
    public int Refused_imprint()
    {
        _imprinter.Execute(_connection, _imprint);
        return _connection.Sent;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Unchanged();
        _instance.Dispose();
        _services.Dispose();
    }

    /// <summary>
    /// Loads the chest and the glyph into the Bag, as character select loads a character's items.
    /// </summary>
    /// <returns>The chest's implicit modifier id.</returns>
    private int HoldChestAndGlyph(StaticData data)
    {
        ItemModifierCatalog catalog = data.Items.Modifiers;
        ItemTemplate glyphTemplate = data.Items.Glyph ?? throw new InvalidOperationException("The seed has no glyph template");
        int implicitId = catalog.LinkedTo(s_chest).Single().Id;
        var chest = new ItemInstanceId(Guid.CreateVersion7());
        var glyph = new ItemInstanceId(Guid.CreateVersion7());
        ItemInstance[] instances =
        [
            new() { Id = chest, TemplateId = s_chest, Identified = true, Modifiers = [Row(catalog, chest, 0, implicitId), Row(catalog, chest, 1, AffixOf("P11", 7))] },
            new() { Id = glyph, TemplateId = glyphTemplate.Id, Identified = true, Modifiers = [Row(catalog, glyph, 0, AffixOf("S14", 9))] },
        ];
        _character.ItemModifiers.Load(instances);
        _character.Container(InventoryType.Bag).Load(
        [
            new InventoryItem(ChestSlot, chest, s_chest, 1, 0, ItemInstanceFlags.None, ItemRarity.Magic, 10, RareName: null, Identified: true, Tainted: false),
            new InventoryItem(GlyphSlot, glyph, glyphTemplate.Id, 1, 0, ItemInstanceFlags.Attuned, ItemRarity.Magic, 1, RareName: null, Identified: true, Tainted: false),
        ]);
        return implicitId;
    }

    /// <summary>The seeded affix of <paramref name="family" /> at <paramref name="tier" />, with no element.</summary>
    private static int AffixOf(string family, byte tier)
    {
        ItemModifierGroup group = ItemModifierSeed.Groups().Single(g =>
            g.Kind is ItemModifierKind.Prefix or ItemModifierKind.Suffix && g.Code == family && g.Element is null);
        return ItemModifierSeed.Modifiers().Single(m => m.GroupId == group.Id && m.Tier == tier).Id;
    }

    /// <summary>A row of <paramref name="modifierId" /> on <paramref name="item" />, each line at its low end.</summary>
    private static ItemInstanceModifier Row(ItemModifierCatalog catalog, ItemInstanceId item, byte ordinal, int modifierId)
    {
        ItemModifier modifier = catalog.Find(modifierId) ?? throw new InvalidOperationException($"The catalog has no modifier {modifierId}");
        var id = Guid.CreateVersion7();
        return new ItemInstanceModifier
        {
            Id = id,
            ItemInstanceId = item,
            ModifierId = modifierId,
            Ordinal = ordinal,
            Values =
            [
                .. modifier.Stats.Select(line => new ItemInstanceModifierValue
                {
                    ItemInstanceModifierId = id,
                    Index = line.Index,
                    Stat = line.Stat,
                    Value = line.Min,
                    Value2 = line.Min2,
                }),
            ],
        };
    }

    private InventoryItem Held(byte slot) =>
        _character.Container(InventoryType.Bag).TryGet(slot, out InventoryItem item)
            ? item
            : throw new InvalidOperationException($"Bag slot {slot} is empty");

    private static void Expect<T>(T answered, T intended) where T : struct, Enum
    {
        if (!answered.Equals(intended))
            throw new InvalidOperationException($"A request was answered {answered}, not {intended}.");
    }

    /// <summary>The last reply the connection kept, decoded.</summary>
    private T Reply<T>() => Serializer.Deserialize<T>(_connection.LastPayload);

    /// <summary>Fails unless the items, the gold and the pending client changes are as setup left them.</summary>
    private void Unchanged()
    {
        if (Held(ChestSlot) != _chestBefore || Held(GlyphSlot) != _glyphBefore || _character.Data!.Gold != 0
            || _character.ClientChanges.HasChanges || _character.ItemModifiers.Of(_chestBefore.InstanceId).Count != 2)
        {
            throw new InvalidOperationException("A refused Workbench request changed the character's items, gold or client changes.");
        }
    }
}
