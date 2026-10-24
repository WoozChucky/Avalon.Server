using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Inventory;
using Avalon.World.Loot;
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

    // Out of order, with one slot empty and one named twice: a potion, a sword, a seeded plate chest carrying its implicit
    // and a magic one carrying its implicit and its affixes (#1179).
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
        Assert.NotNull(_character.ItemModifiers.NameOf(At(_character, InventoryType.Bag, 2), s_items));
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
        long slots = Allocated(() =>
        {
            foreach (SlotRef slot in s_slots)
                _character.ClientChanges.RecordSlot(slot.Container, slot.Slot);
            InventoryUpdateFlusher.Flush(_connection, s_items, _pool);
        });

        Assert.Equal(0, goldOnly);
        Assert.Equal(Allocated(() => Entries(Named)), slots);
        Assert.Equal(8 * Rounds, _connection.Sent);
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
    /// item, and nothing for the items' modifiers and names (#1179), which the pool and the store already hold.
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
