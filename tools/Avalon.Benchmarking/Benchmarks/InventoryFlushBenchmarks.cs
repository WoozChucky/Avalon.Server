using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Serialization;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public.Characters;
using Avalon.World.Testing.Fakes;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// The tick's inventory update for one connection whose character's inventory changed (#883): <see cref="Slots" /> Bag
/// slots, each holding an item, marked changed and sent as one <c>SInventoryUpdatePacket</c>; with no slot, a tick
/// where only the gold changed. <see cref="Queried" /> is the flush as it was, the changed slots filtered, ordered
/// and read by a query; <see cref="Flusher" /> is <c>InventoryUpdateFlusher.Flush</c>. The connection counts what it is
/// sent and gives the payload back to the pool, so what is left is the flush, the packet's entries and the encode.
/// </summary>
[MemoryDiagnoser]
public class InventoryFlushBenchmarks
{
    private CharacterEntity _character = null!;
    private QuietConnection _connection = null!;

    [Params(0, 1, 8, 30)]
    public int Slots { get; set; }

    [GlobalSetup]
    public void Setup()
    {
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

        var held = new InventoryItem[Slots];
        for (byte slot = 0; slot < Slots; slot++)
        {
            held[slot] = new InventoryItem(slot, new ItemInstanceId(Guid.CreateVersion7()), new ItemTemplateId(1), 5, 0,
                ItemInstanceFlags.None, ItemRarity.Common, 1);
        }

        _character.Container(InventoryType.Bag).Load(held);
        _connection = new QuietConnection(_character);

        if (Queried() != 1 || Flusher() != 2)
            throw new InvalidOperationException("A flush with changes sent nothing.");
    }

    /// <summary>The flush as it was before #883: the changed slots filtered, ordered and read by a query.</summary>
    [Benchmark(Baseline = true)]
    public int Queried()
    {
        MarkChanged();

        InventoryClientChanges changes = _character.ClientChanges;
        InventorySlotUpdateDto[] slots = Query(changes, _character, BankAccess.IsOpen(_connection, _character));
        ulong? gold = changes.GoldChanged ? _character.Data!.Gold : null;
        changes.Clear();

        _connection.Send(SInventoryUpdatePacket.Create(slots, gold, PacketEncoder.Shared));
        return _connection.Sent;
    }

    [Benchmark]
    public int Flusher()
    {
        MarkChanged();
        InventoryUpdateFlusher.Flush(_connection);
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

    private static InventorySlotUpdateDto[] Query(InventoryClientChanges changes, CharacterEntity character,
        bool bankOpen) =>
        changes.Slots
            .Where(s => bankOpen || s.Container != InventoryType.Bank)
            .OrderBy(s => s.Container)
            .ThenBy(s => s.Slot)
            .Select(s => new InventorySlotUpdateDto
            {
                Container = s.Container,
                Slot = s.Slot,
                Item = character.Container(s.Container).TryGet(s.Slot, out InventoryItem item)
                    ? ItemSlotDtoMapper.ToDto(s.Container, item, character.ItemModifiers.Of(item.InstanceId))
                    : null,
            })
            .ToArray();
}
