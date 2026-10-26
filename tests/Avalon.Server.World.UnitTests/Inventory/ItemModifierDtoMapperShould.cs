using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Items;
using Avalon.Network.Packets.Loot;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Trade;
using Avalon.Network.Packets.Vendor;
using Avalon.Server.World.UnitTests.Seeding;
using Avalon.Server.World.UnitTests.Trading;
using Avalon.World;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Quests;
using Avalon.World.Testing.Scenarios;
using Avalon.World.Vendors;
using NSubstitute;
using ProtoBuf;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;
using static Avalon.Server.World.UnitTests.Inventory.TestModifiers;

namespace Avalon.Server.World.UnitTests.Inventory;

/// <summary>
/// What a client is told of an unidentified copy (#1179), on every message that describes one: its template, rarity,
/// item level, base stats and implicit line, and none of its prefixes, suffixes (a sealed one's progress and threshold
/// included, #1180), a unique's own line or its name. Each path builds its real message and reads it back off the wire,
/// over the seeded templates and catalog. An identified copy shows an imprinted affix as imprinted, and its Tainted mark on
/// the slot and trade paths (#1180).
/// </summary>
public class ItemModifierDtoMapperShould
{
    private static readonly StaticData s_data = ScenarioReferenceData.Shared.Data;
    private static readonly ItemTemplate s_chest = s_data.Items.Templates.Single(t => t.Name == "Ironbound Chestguard");
    private static readonly ItemTemplate s_signet = s_data.Items.Templates.Single(t => t.Name == "Thornheart Signet");

    /// <summary>A copy as one message showed it: modifiers and base stats as lines, null for none.</summary>
    private sealed record Seen(
        ulong TemplateId, ItemRarity Rarity, uint ItemLevel, bool Identified, string? Name, string? Modifiers,
        string? BaseStats, bool Tainted);

    [Theory]
    [InlineData("snapshot")]
    [InlineData("update")]
    [InlineData("bank")]
    [InlineData("buyback")]
    [InlineData("item result")]
    [InlineData("trade")]
    [InlineData("ground")]
    [InlineData("kept drop")]
    [InlineData("identified")]
    [InlineData("identified, trade")]
    [InlineData("identified, buyback")]
    public void Hide_an_unidentified_items_affixes_and_name_on_every_path(string path)
    {
        // The chest's prefix is sealed and its suffix imprinted (#1180): identified, it shows the seal's progress and
        // threshold, the imprint and its Tainted mark; unidentified, it sends neither affix nor their numbers.
        bool identified = path.StartsWith("identified", StringComparison.Ordinal);
        CarriedItem chest = Chest(identified);

        Seen seen = Show(path switch { "identified" => "update", _ when identified => path["identified, ".Length..], _ => path },
            chest);

        Assert.Equal(identified
            ? new Seen(s_chest.Id.Value, ItemRarity.Rare, 9, true, "Moss Shell", Lines(chest.Modifiers, 9), "Armour 8", true)
            : new Seen(s_chest.Id.Value, ItemRarity.Rare, 9, false, null, Lines([chest.Modifiers[0]], 9), "Armour 8", false),
            seen);
    }

    /// <summary>
    /// A pooled entry is written whole (#1180): one a sealed row filled, refilled once that row has woken (an awake copy
    /// in its place, as a wake replaces the item's list), carries no seal, so a client never shows a woken affix sealed;
    /// and one an imprinted row filled, refilled with a row that is not, reads not imprinted.
    /// </summary>
    [Fact]
    public void Clear_a_reused_entrys_seal_when_it_shows_an_awake_row()
    {
        var pool = new ItemModifierDtoPool();
        CarriedItem chest = Chest(identified: true);
        List<ItemModifierDto>? entry = pool.Refill(null, chest.Modifiers, s_data.Items.Modifiers, identified: true,
            chest.Item.ItemLevel);
        Assert.Equal(Lines(chest.Modifiers, 9), Lines(RoundTrip(new ItemSlotDto { Modifiers = entry }).Modifiers));

        ItemInstanceModifier awake = chest.Modifiers[1].CopyRow();
        awake.SealProgress = null;
        awake.Values = chest.Modifiers[1].Values;
        ItemInstanceModifier plain = chest.Modifiers[2].CopyRow();
        plain.Imprinted = false;
        plain.Values = chest.Modifiers[2].Values;
        List<ItemInstanceModifier> woken = [chest.Modifiers[0], awake, plain];

        List<ItemModifierDto>? refilled = pool.Refill(entry, woken, s_data.Items.Modifiers, identified: true,
            chest.Item.ItemLevel);

        Assert.Same(entry, refilled);
        List<ItemModifierDto> shown = RoundTrip(new ItemSlotDto { Modifiers = refilled }).Modifiers!;
        Assert.Equal(Lines(woken, 9), Lines(shown));
        Assert.False(shown[2].Imprinted);
    }

    /// <summary>
    /// An unidentified unique shows the implicit of the base it is made on and withholds its own line, which the hiding
    /// finds by the line's kind, not its place; identified, it shows both.
    /// </summary>
    [Fact]
    public void Show_an_unidentified_unique_with_its_implicit_only()
    {
        CarriedItem unidentified = Signet(identified: false);
        CarriedItem identified = Signet(identified: true);
        var hidden = new Seen(s_signet.Id.Value, ItemRarity.Unique, 5, false, null, Lines([unidentified.Modifiers[0]], 5),
            null, false);

        Assert.Equal(hidden, Show("update", unidentified));
        Assert.Equal(hidden, Show("trade", unidentified));
        Assert.Equal(new Seen(s_signet.Id.Value, ItemRarity.Unique, 5, true, null, Lines(identified.Modifiers, 5), null, false),
            Show("update", identified));
    }

    /// <summary>
    /// A rare Ironbound Chestguard: its implicit, a prefix sealed with 17 points (#1180) and an imprinted suffix, and its
    /// rolled armour. Identified, it is Tainted (#1180).
    /// </summary>
    private static CarriedItem Chest(bool identified)
    {
        var id = new ItemInstanceId(Guid.CreateVersion7());
        var item = new InventoryItem(0, id, s_chest.Id, 1, 0, ItemInstanceFlags.None, ItemRarity.Rare, 9, "Moss Shell",
            identified, Tainted: identified);
        ItemInstanceModifier sealedPrefix = Modifier(id, CatalogKeys.ModifierId("P11 T7"), 1, (0, StatType.Health, 5, null));
        sealedPrefix.SealProgress = 17;
        ItemInstanceModifier imprintedSuffix = Modifier(id, CatalogKeys.ModifierId("S14 T9"), 2,
            (0, StatType.IncreasedMaximumLife, 1, null));
        imprintedSuffix.Imprinted = true;
        return new CarriedItem(item, new ItemBaseStats { Armour = 8 },
        [
            Modifier(id, CatalogKeys.ModifierId("implicit IncreasedMaximumLife T9"), 0, (0, StatType.IncreasedMaximumLife, 2, null)),
            sealedPrefix,
            imprintedSuffix,
        ], 0);
    }

    /// <summary>A Thornheart Signet: its base's implicit, then its own line, and no base stats.</summary>
    private static CarriedItem Signet(bool identified)
    {
        var id = new ItemInstanceId(Guid.CreateVersion7());
        var item = new InventoryItem(0, id, s_signet.Id, 1, 0, ItemInstanceFlags.None, ItemRarity.Unique, 5, null,
            identified);
        return new CarriedItem(item, default,
        [
            Modifier(id, CatalogKeys.ModifierId("implicit AddedLightningDamageToAttacks T8"), 0,
                (0, StatType.AddedLightningDamageToAttacks, 1, 2)),
            Modifier(id, CatalogKeys.ModifierId("unique 65"), 1,
                (0, StatType.Health, 5, null), (1, StatType.AddedPhysicalDamageToAttacks, 1, 3),
                (2, StatType.CritMultiplier, 4, null)),
        ], 0);
    }

    /// <summary>The copy as <paramref name="path" />'s message carries it to a client.</summary>
    private static Seen Show(string path, CarriedItem copy)
    {
        var pool = new ItemModifierDtoPool();
        CharacterEntity character = New();
        var sent = new List<OutboundPacket>();
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        connection.When(c => c.Send(Arg.Any<OutboundPacket>())).Do(call => sent.Add(call.Arg<OutboundPacket>()));
        CharacterInventoryService inventory = InventoryFor(character, s_data.Items.Find, modifiers: s_data.Items.Modifiers);

        switch (path)
        {
            case "snapshot":
            {
                Assert.Equal(InventoryAddResult.Ok, inventory.TryAddInstance(copy));
                ItemSlotDto slot = ItemSlotDtoMapper.ToDto(InventoryType.Bag, At(character, InventoryType.Bag, 0),
                    character.ItemModifiers, s_data.Items, pool);
                return Of(Read<SInventorySnapshotPacket>(SInventorySnapshotPacket.Create([slot], 0, PacketEncoder.Shared))
                    .Items.Single());
            }
            case "update":
                Assert.Equal(InventoryAddResult.Ok, inventory.TryAddInstance(copy));
                InventoryUpdateFlusher.Flush(connection, s_data.Items, pool);
                return Of(Read<SInventoryUpdatePacket>(Assert.Single(sent)).Slots.Single().Item!);
            case "bank":
            {
                Assert.Equal(InventoryAddResult.Ok, inventory.TryAddInstance(copy));
                Assert.Equal(ItemRequestResult.Ok, inventory.TryMove(new SlotRef(InventoryType.Bag, 0),
                    new SlotRef(InventoryType.Bank, 0), count: null, bankAccessible: true));
                InventorySlotUpdateDto[] bank = BankAccess.Snapshot(character, s_data.Items, pool);
                return Of(Read<SInventoryUpdatePacket>(SInventoryUpdatePacket.Create(bank, null, PacketEncoder.Shared))
                    .Slots.Single(s => s.Item is not null).Item!);
            }
            case "buyback":
            {
                character.Buyback.Push(new BuybackEntry(copy, 10));
                var noStock = new VendorStockState(new CreatureTemplateId(1), []);
                SVendorListPacket list = VendorListBuilder.Build(new ObjectGuid(ObjectType.Creature, 1), noStock, character,
                    s_data, NoQuestProgress.Instance);
                return Of(Read<SVendorListPacket>(SVendorListPacket.Create(list.VendorGuid, list.Entries, list.Buyback,
                    PacketEncoder.Shared)).Buyback.Single().Item!);
            }
            case "item result":
                Assert.Equal(InventoryAddResult.Ok, inventory.TryAddInstance(copy));
                ItemRequestReply.Send(connection, character, 1, ItemRequestResult.TargetFull,
                    [new SlotRef(InventoryType.Bag, 0)], s_data.Items, pool);
                return Of(Read<SItemResultPacket>(Assert.Single(sent)).Slots.Single().Item!);
            case "trade":
            {
                var w = new TradeTestWorld(items: s_data.Items);
                Trader owner = w.Online(1);
                Trader partner = w.Online(2);
                Assert.Equal(InventoryAddResult.Ok, w.Economy.InventoryOf(owner.Character).TryAddInstance(copy));
                w.Open(owner, partner);
                Assert.Equal(TradeResult.Ok, w.Trades.OfferItem(owner.Id, bagSlot: 0, tradeSlot: 0));

                // One entry serves both sides: its owner is shown no more than its partner.
                Seen shown = Of(Assert.Single(partner.States().Last().Partner.Items));
                Assert.Equal(shown, Of(Assert.Single(owner.States().Last().You.Items)));
                return shown;
            }
            case "ground":
            {
                GroundLoot drop = Ground(copy.Item, copy);

                // A snapshot's drop and a kill's kept message, refilled from the pool, say the same.
                Seen fresh = Of(RoundTrip(LootDropMapper.ToDto(drop, s_data)));
                Assert.Equal(fresh, Of(RoundTrip(LootDropMapper.Fill(new LootDropDto(), drop, s_data, pool))));
                return fresh;
            }
            case "kept drop":
            {
                // A kill's kept message (#890), refilled from the pool: first a common drop showing all three rows, then
                // this one, which keeps one of the three in the same list; then one that keeps none, whose list goes back
                // to the pool for the next message.
                var kept = new LootDropDto();
                LootDropMapper.Fill(kept, Ground(copy.Item with { Rarity = ItemRarity.Common }, copy), s_data, pool);
                List<ItemModifierDto> list = Assert.IsType<List<ItemModifierDto>>(kept.Modifiers);
                Assert.Equal(3, list.Count);

                LootDropMapper.Fill(kept, Ground(copy.Item, copy), s_data, pool);
                Assert.Same(list, kept.Modifiers);
                Seen shown = Of(RoundTrip(kept));

                LootDropMapper.Fill(kept, Ground(copy.Item, copy with { Modifiers = [.. copy.Modifiers.Skip(1)] }), s_data,
                    pool);
                Assert.Null(kept.Modifiers);
                Assert.Same(list, pool.Rent(copy.Modifiers, s_data.Items.Modifiers, identified: true, copy.Item.ItemLevel));
                return shown;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(path), path, null);
        }
    }

    /// <summary><paramref name="item" /> lying on the ground as a drop rolled it, with <paramref name="rows" />' rows.</summary>
    private static GroundLoot Ground(InventoryItem item, CarriedItem rows) => new()
    {
        Guid = new ObjectGuid(ObjectType.Loot, 1),
        Position = Vector3.zero,
        ItemTemplateId = item.TemplateId,
        Count = 1,
        Rolled = new RolledItem(item.InstanceId, item.TemplateId, item.Rarity, item.ItemLevel, item.RareName,
            rows.BaseStats, [.. rows.Modifiers]),
    };

    private static Seen Of(ItemSlotDto slot) => new(slot.ItemTemplateId, slot.Rarity, slot.ItemLevel, slot.Identified,
        slot.Name, Lines(slot.Modifiers), Lines(slot.BaseStats), slot.Tainted);

    private static Seen Of(TradeItemDto item) => new(item.ItemTemplateId, item.Rarity, item.ItemLevel, item.Identified,
        item.Name, Lines(item.Modifiers), Lines(item.BaseStats), item.Tainted);

    /// <summary>A drop carries no Identified (it is exactly Common), no name and no Tainted mark (it is a fresh roll).</summary>
    private static Seen Of(LootDropDto drop) => new(drop.ItemTemplateId!.Value, drop.Rarity, drop.ItemLevel,
        drop.Rarity == ItemRarity.Common, null, Lines(drop.Modifiers), Lines(drop.BaseStats), false);

    private static string? Lines(List<ItemModifierDto>? modifiers) => modifiers is null
        ? null
        : string.Join(" | ", modifiers.Select(m =>
            $"{m.ModifierId}: " + string.Join(", ", m.Values.Select(v => $"{v.Stat} {v.Value}..{v.Value2}")) +
            (m.SealProgress is null && m.SealThreshold is null ? "" : $" sealed {m.SealProgress}/{m.SealThreshold}") +
            (m.Imprinted ? " imprinted" : "")));

    /// <summary>
    /// The rows of a copy of <paramref name="itemLevel" /> as <see cref="Lines(List{ItemModifierDto})" /> words a
    /// message's: a sealed row with its progress and its threshold (" sealed 17/130" at item level 9, 40 + 90), and an
    /// imprinted row marked " imprinted" (#1180).
    /// </summary>
    private static string Lines(IReadOnlyList<ItemInstanceModifier> modifiers, ushort itemLevel) =>
        string.Join(" | ", modifiers.Select(m =>
            $"{m.ModifierId}: " + string.Join(", ", m.Values.Select(v => $"{(ItemStat)v.Stat} {v.Value}..{v.Value2}")) +
            (m.SealProgress is { } progress ? $" sealed {progress}/{ItemAwakening.Threshold(itemLevel)}" : "") +
            (m.Imprinted ? " imprinted" : "")));

    private static string? Lines(List<ItemBaseStatDto>? baseStats) =>
        baseStats is null ? null : string.Join(", ", baseStats.Select(b => $"{b.Stat} {b.Value}"));

    private static T Read<T>(OutboundPacket packet)
    {
        using var stream = new MemoryStream(packet.PayloadMemory.ToArray());
        return Serializer.Deserialize<T>(stream);
    }

    private static T RoundTrip<T>(T message)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, message);
        stream.Position = 0;
        return Serializer.Deserialize<T>(stream);
    }
}
