using Avalon.Common;
using Avalon.Network.Packets.Character;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public.Characters;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Performance;

/// <summary>
/// The tick's inventory update allocates the entries its packet carries and nothing else (#883): no query and no
/// closure on the way. What the entries cost is measured here, by building the same ones, rather than written down,
/// so the pin holds on any runtime.
/// </summary>
public class InventoryAllocationShould
{
    private const int Rounds = 100;

    // Out of order, with one slot empty and one touched twice.
    private static readonly SlotRef[] s_slots =
    [
        new(InventoryType.Bag, 3), new(InventoryType.Bag, 9), new(InventoryType.Bag, 0), new(InventoryType.Bag, 3),
    ];

    private readonly CharacterEntity _character = New();
    private readonly QuietConnection _connection;

    public InventoryAllocationShould()
    {
        _character.Container(InventoryType.Bag).Load([Item(0, Potion, count: 5), Item(3, Sword)]);
        _connection = new QuietConnection(_character);
    }

    [Fact]
    public void Flush_a_ticks_changes_allocating_only_the_entries_sent()
    {
        long moneyOnly = Allocated(() =>
        {
            _character.ClientChanges.RecordMoney();
            InventoryUpdateFlusher.Flush(_connection);
        });
        long slots = Allocated(() =>
        {
            foreach (SlotRef slot in s_slots)
                _character.ClientChanges.RecordSlot(slot.Container, slot.Slot);
            InventoryUpdateFlusher.Flush(_connection);
        });

        Assert.Equal(0, moneyOnly);
        Assert.Equal(Allocated(() => Entries(3)), slots);
        Assert.Equal(4 * Rounds, _connection.Sent);
    }

    /// <summary>What <see cref="Rounds" /> calls allocate on this thread, after as many to warm up.</summary>
    private static long Allocated(Action round)
    {
        for (int i = 0; i < Rounds; i++)
            round();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Rounds; i++)
            round();

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>The entries a packet naming the first <paramref name="count" /> slots carries: the array, and each slot with its item.</summary>
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
                Item = _character.Container(slot.Container).TryGet(slot.Slot, out InventoryItem item)
                    ? ItemSlotDtoMapper.ToDto(slot.Container, item)
                    : null,
            };
        }

        return entries;
    }
}
