using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Serialization;
using Avalon.World;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Reload;
using Avalon.World.Testing.Fakes;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// The tick's inventory update for one connection whose character's inventory changed (#883): <see cref="Slots" /> Bag
/// slots, each holding an item, marked changed and sent as one <c>SInventoryUpdatePacket</c>; with no slot, a tick
/// where only the gold changed. With <see cref="Rolled" /> (#1179) the items are seeded plate chests the roller made over
/// the real catalog, every other one a plain copy carrying its base stats and its implicit and the rest Magic copies
/// carrying their base stats, their implicit, their affixes and a name; without it, common potions with no modifiers.
/// <see cref="Queried" /> is the flush as it was before #883, the changed slots filtered, ordered and read by a query;
/// <see cref="Unpooled" /> is the flush as it was before #1179's last review, each modifier and base-stat entry made
/// afresh and each magic name built again at every describe; <see cref="Flusher" /> is
/// <c>InventoryUpdateFlusher.Flush</c>, its modifier and base-stat entries from a pool and its names held on the store.
/// The connection counts what it is sent and gives the payload back to the pool, so what is left is the flush, the
/// packet's entries and the encode.
/// </summary>
[MemoryDiagnoser]
public class InventoryFlushBenchmarks
{
    private static readonly ItemTemplate s_potion = new()
    {
        Id = new ItemTemplateId(1),
        Name = "Potion",
        Class = ItemClass.Consumable,
        MaxStackSize = 20,
    };

    // A seeded plate chest, which links one implicit.
    private static readonly ItemTemplateId s_plateChest = new(37);

    private readonly ItemModifierDtoPool _pool = new();
    private ItemsPatch _items = null!;
    private CharacterEntity _character = null!;
    private QuietConnection _connection = null!;

    [Params(0, 1, 8, 30)]
    public int Slots { get; set; }

    [Params(false, true)]
    public bool Rolled { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // The items generation the flush names copies by: the potion, and the seeded chest over the real catalog.
        StaticData data = ScenarioReferenceData.Shared.Data;
        ItemTemplate chest = data.Items.Find(s_plateChest)!;
        _items = new ItemsPatch([s_potion, chest], data.Items.Modifiers);

        var row = new Character
        {
            Id = new CharacterId(1),
            AccountId = new AccountId(1),
            Name = "Bench",
            Class = CharacterClass.Warrior,
            Gold = 40,
            CreationDate = DateTime.UtcNow,
        };
        _character = new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration()) { Data = row };

        var roller = new ItemRoller(new LootRandom(new Random(1179)), new ItemIdAllocator(),
            Options.Create(new GameConfiguration()));
        var magic = new DropContext(10, CreatureRarity.Normal, ForceMagic: true);
        var held = new InventoryItem[Slots];
        var instances = new List<ItemInstance>();
        for (byte slot = 0; slot < Slots; slot++)
        {
            if (!Rolled)
            {
                held[slot] = new InventoryItem(slot, new ItemInstanceId(Guid.CreateVersion7()), s_potion.Id, 5, 0,
                    ItemInstanceFlags.None, ItemRarity.Common, 1, RareName: null, Identified: true);
                continue;
            }

            RolledItem copy = slot % 2 == 0 ? roller.RollPlain(chest, 1, _items) : roller.Roll(chest, magic, _items)!;
            held[slot] = new InventoryItem(slot, copy.InstanceId, copy.TemplateId, 1, 0, ItemInstanceFlags.None,
                copy.Rarity, copy.ItemLevel, copy.RareName, Identified: true);
            var baseStats = new List<ItemInstanceBaseStat>();
            foreach ((ItemBaseStat stat, int value) in copy.BaseStats)
                baseStats.Add(new ItemInstanceBaseStat { ItemInstanceId = copy.InstanceId, Stat = stat, Value = value });

            instances.Add(new ItemInstance
            {
                Identified = true,
                Id = copy.InstanceId,
                TemplateId = copy.TemplateId,
                Modifiers = copy.Modifiers,
                BaseStats = baseStats,
            });
        }

        _character.Container(InventoryType.Bag).Load(held);
        _character.ItemModifiers.Load(instances);
        _connection = new QuietConnection(_character);

        if (Rolled && Slots > 0 && _character.ItemModifiers.BaseStatsOf(held[0].InstanceId).IsEmpty)
            throw new InvalidOperationException("A rolled chest carries no base stats.");
        if (Queried() != 1 || Unpooled() != 2 || Flusher() != 3)
            throw new InvalidOperationException("A flush with changes sent nothing.");
    }

    /// <summary>The flush as it was before #883: the changed slots filtered, ordered and read by a query.</summary>
    [Benchmark(Baseline = true)]
    public int Queried()
    {
        MarkChanged();

        InventoryClientChanges changes = _character.ClientChanges;
        InventorySlotUpdateDto[] slots = Query(changes, _character, BankAccess.IsOpen(_connection, _character), _items);
        ulong? gold = changes.GoldChanged ? _character.Data!.Gold : null;
        changes.Clear();

        _connection.Send(SInventoryUpdatePacket.Create(slots, gold, PacketEncoder.Shared));
        _pool.Return(slots);
        return _connection.Sent;
    }

    /// <summary>
    /// The flush before #1179's last review: the flusher's own walk and sort, but every modifier entry made afresh and every
    /// magic copy's name built again.
    /// </summary>
    [Benchmark]
    public int Unpooled()
    {
        MarkChanged();

        InventoryClientChanges changes = _character.ClientChanges;
        var slots = new InventorySlotUpdateDto[changes.Slots.Count];
        int next = 0;
        foreach ((InventoryType container, byte slot) in changes)
            slots[next++] = new InventorySlotUpdateDto { Container = container, Slot = slot, Item = Fresh(container, slot) };
        Array.Sort(slots, static (a, b) => a.Slot - b.Slot);
        ulong? gold = changes.GoldChanged ? _character.Data!.Gold : null;
        changes.Clear();

        _connection.Send(SInventoryUpdatePacket.Create(slots, gold, PacketEncoder.Shared));
        return _connection.Sent;
    }

    [Benchmark]
    public int Flusher()
    {
        MarkChanged();
        InventoryUpdateFlusher.Flush(_connection, _items, _pool);
        return _connection.Sent;
    }

    private void MarkChanged()
    {
        InventoryClientChanges changes = _character.ClientChanges;
        if (Slots == 0)
            changes.RecordGold();

        // Highest slot first, so the ordering has something to do.
        for (int slot = Slots - 1; slot >= 0; slot--)
            changes.RecordSlot(InventoryType.Bag, (byte)slot);
    }

    /// <summary>A held item as the flush described it before the pool: lists of its own and its name built now.</summary>
    private ItemSlotDto? Fresh(InventoryType container, byte slot)
    {
        if (!_character.Container(container).TryGet(slot, out InventoryItem item))
            return null;

        IReadOnlyList<ItemInstanceModifier> modifiers = _character.ItemModifiers.Of(item.InstanceId);
        return new ItemSlotDto
        {
            Container = container,
            Slot = item.Slot,
            ItemTemplateId = item.TemplateId.Value,
            ItemInstanceId = item.InstanceId.Value,
            Count = item.Count,
            Durability = item.Durability,
            Flags = (uint)item.Flags,
            Rarity = item.Rarity,
            ItemLevel = item.ItemLevel,
            Modifiers = ItemModifierDtoMapper.ToDtos(modifiers),
            Name = ItemSlotDtoMapper.NameOf(item.TemplateId, item.Rarity, item.RareName, modifiers, _items),
            BaseStats = ItemModifierDtoMapper.ToBaseStatDtos(_character.ItemModifiers.BaseStatsOf(item.InstanceId)),
        };
    }

    private InventorySlotUpdateDto[] Query(InventoryClientChanges changes, CharacterEntity character,
        bool bankOpen, ItemsPatch items) =>
        changes.Slots
            .Where(s => bankOpen || s.Container != InventoryType.Bank)
            .OrderBy(s => s.Container)
            .ThenBy(s => s.Slot)
            .Select(s => new InventorySlotUpdateDto
            {
                Container = s.Container,
                Slot = s.Slot,
                Item = character.Container(s.Container).TryGet(s.Slot, out InventoryItem item)
                    ? ItemSlotDtoMapper.ToDto(s.Container, item, character.ItemModifiers, items, _pool)
                    : null,
            })
            .ToArray();
}
