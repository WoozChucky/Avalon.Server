using Avalon.Common;
using Avalon.Network.Packets.Character;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public.Characters;
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

    // Out of order, with one slot empty and one named twice.
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
        Assert.Equal(8 * Rounds, _connection.Sent);
    }

    [Fact]
    public void Answer_an_item_request_allocating_only_the_slots_a_refusal_names()
    {
        long accepted = Allocated(() => ItemRequestReply.Send(_connection, _character, 1, ItemRequestResult.Ok, s_slots));
        long refused = Allocated(() =>
            ItemRequestReply.Send(_connection, _character, 2, ItemRequestResult.TargetFull, s_slots));

        Assert.Equal(0, accepted);
        Assert.Equal(Allocated(() => Entries(3)), refused);
        Assert.Equal(8 * Rounds, _connection.Sent);
    }

    /// <summary>
    /// What <see cref="Rounds" /> calls allocate on this thread: the fewest of three windows, after one to warm up.
    /// </summary>
    private static long Allocated(Action round)
    {
        long fewest = long.MaxValue;
        for (int window = 0; window < 4; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < Rounds; i++)
                round();

            if (window > 0)
                fewest = Math.Min(fewest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return fewest;
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
