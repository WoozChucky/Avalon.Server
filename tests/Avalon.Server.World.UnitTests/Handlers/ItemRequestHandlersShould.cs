using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Inventory;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>
/// CMSG_ITEM_MOVE and CMSG_ITEM_DESTROY on the tick (spec #463): every request gets exactly one
/// SMSG_ITEM_RESULT, a refusal carries the state of the slots it named, and gear changes refresh
/// the stats. The rules themselves are InventoryMoveShould's.
/// </summary>
public class ItemRequestHandlersShould : IAsyncLifetime
{
    private BankerWorld _w = null!;

    public async Task InitializeAsync() => _w = await BankerWorld.CreateAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private ItemMoveHandler MoveHandler(ICharacterEconomy? economy = null) =>
        new(NullLogger<ItemMoveHandler>.Instance, _w.World, economy ?? new CharacterEconomy(_w.World, new ItemIdAllocator()));

    private ItemDestroyHandler DestroyHandler() =>
        new(NullLogger<ItemDestroyHandler>.Instance, _w.World, new CharacterEconomy(_w.World, new ItemIdAllocator()));

    private void Move(uint request, InventoryType from, uint fromSlot, InventoryType to, uint toSlot, uint? count = null) =>
        MoveHandler().Execute(_w.Connection, new CItemMovePacket
        {
            RequestId = request, FromContainer = (uint)from, FromSlot = fromSlot,
            ToContainer = (uint)to, ToSlot = toSlot, Count = count,
        });

    private void Destroy(uint request, InventoryType container, uint slot, uint? count = null) =>
        DestroyHandler().Execute(_w.Connection, new CItemDestroyPacket
        {
            RequestId = request, Container = (uint)container, Slot = slot, Count = count,
        });

    private List<SItemResultPacket> Results() => _w.Read<SItemResultPacket>(NetworkPacketType.SMSG_ITEM_RESULT);

    [Fact]
    public void Answer_an_accepted_move_with_Ok_and_no_slots()
    {
        _w.Character.Container(InventoryType.Bag).Load([Item(0, Potion, count: 5)]);

        Move(41, InventoryType.Bag, 0, InventoryType.Bag, 3);

        SItemResultPacket result = Assert.Single(Results());
        Assert.Equal((41u, ItemRequestResult.Ok), (result.RequestId, result.Result));
        Assert.Empty(result.Slots);
        Assert.Equal(5u, At(_w.Character, InventoryType.Bag, 3).Count);
        // The change itself reaches the client through the tick's flush, not the result.
        Assert.True(_w.Character.ClientChanges.HasChanges);
    }

    [Fact]
    public void Answer_a_refusal_with_the_current_state_of_both_named_slots()
    {
        _w.Character.Container(InventoryType.Bag).Load([Item(0, Potion, count: 5), Item(1, Potion, count: 20)]);

        Move(42, InventoryType.Bag, 0, InventoryType.Bag, 1);

        SItemResultPacket result = Assert.Single(Results());
        Assert.Equal(ItemRequestResult.TargetFull, result.Result);
        Assert.Equal(2, result.Slots.Length);
        Assert.Equal(5u, result.Slots.Single(s => s.Slot == 0).Item!.Count);
        Assert.Equal(20u, result.Slots.Single(s => s.Slot == 1).Item!.Count);
        Assert.False(_w.Character.ClientChanges.HasChanges);
    }

    [Fact]
    public void Describe_an_empty_named_slot_with_no_item()
    {
        Move(43, InventoryType.Bag, 0, InventoryType.Bag, 1);

        SItemResultPacket result = Assert.Single(Results());
        Assert.Equal(ItemRequestResult.NotFound, result.Result);
        Assert.Equal(2, result.Slots.Length);
        Assert.All(result.Slots, s => Assert.Null(s.Item));
    }

    [Fact]
    public void Answer_Dead_to_a_dead_character_and_change_nothing()
    {
        _w.Character.Container(InventoryType.Bag).Load([Item(0, Potion, count: 5)]);
        _w.Character.IsDead = true;

        Move(44, InventoryType.Bag, 0, InventoryType.Bag, 3);
        Destroy(45, InventoryType.Bag, 0);

        Assert.Equal([ItemRequestResult.Dead, ItemRequestResult.Dead], Results().Select(r => r.Result));
        Assert.Equal(5u, At(_w.Character, InventoryType.Bag, 0).Count);
        Assert.False(_w.Character.ClientChanges.HasChanges);
    }

    /// <summary>Review Focus 4.</summary>
    [Fact]
    public void Answer_InvalidSlot_to_a_container_or_slot_no_character_has()
    {
        _w.Character.Container(InventoryType.Bag).Load([Item(0, Potion, count: 5)]);

        MoveHandler().Execute(_w.Connection, new CItemMovePacket
        {
            RequestId = 46, FromContainer = 7, FromSlot = 0, ToContainer = (uint)InventoryType.Bag, ToSlot = 3,
        });
        MoveHandler().Execute(_w.Connection, new CItemMovePacket
        {
            RequestId = 47, FromContainer = (uint)InventoryType.Bag, FromSlot = 0,
            ToContainer = (uint)InventoryType.Bag, ToSlot = 70000,
        });
        Move(48, InventoryType.Bag, 0, InventoryType.Bag, 3, count: 0);

        List<SItemResultPacket> results = Results();
        Assert.Equal(3, results.Count);
        Assert.Equal(ItemRequestResult.InvalidSlot, results[0].Result);
        Assert.Equal((ushort)3, Assert.Single(results[0].Slots).Slot);      // only the readable slot
        Assert.Equal(ItemRequestResult.InvalidSlot, results[1].Result);
        Assert.Equal((ushort)0, Assert.Single(results[1].Slots).Slot);
        Assert.Equal(ItemRequestResult.InvalidCount, results[2].Result);
        Assert.Equal(5u, At(_w.Character, InventoryType.Bag, 0).Count);
    }

    /// <summary>Review Focus 1: a double drag in one tick moves the item once.</summary>
    [Fact]
    public void Refuse_the_second_of_two_moves_from_one_slot_in_the_same_tick()
    {
        _w.Character.Container(InventoryType.Bag).Load([Item(0, Potion, count: 5)]);

        Move(50, InventoryType.Bag, 0, InventoryType.Bag, 3);
        Move(51, InventoryType.Bag, 0, InventoryType.Bag, 4);

        Assert.Equal([ItemRequestResult.Ok, ItemRequestResult.NotFound], Results().Select(r => r.Result));
        Assert.Single(_w.Character.Container(InventoryType.Bag).Items);
        Assert.Equal(5u, At(_w.Character, InventoryType.Bag, 3).Count);
    }

    /// <summary>Review Focus 2.</summary>
    [Fact]
    public void Leave_bank_slots_out_of_a_refusal_while_the_bank_is_closed()
    {
        _w.Character.Container(InventoryType.Bag).Load([Item(0, Potion, count: 5)]);
        _w.Character.Container(InventoryType.Bank).Load([Item(0, Sword)]);

        Move(52, InventoryType.Bag, 0, InventoryType.Bank, 0);

        SItemResultPacket result = Assert.Single(Results());
        Assert.Equal(ItemRequestResult.BankClosed, result.Result);
        InventorySlotUpdateDto slot = Assert.Single(result.Slots);
        Assert.Equal((ushort)InventoryType.Bag, slot.Container);
    }

    [Fact]
    public void Include_bank_slots_in_a_refusal_while_the_bank_is_open()
    {
        _w.OpenBank();
        _w.Character.Container(InventoryType.Bag).Load([Item(0, Potion, count: 5)]);
        _w.Character.Container(InventoryType.Bank).Load([Item(0, Potion, count: 20)]);

        Move(53, InventoryType.Bag, 0, InventoryType.Bank, 0);

        SItemResultPacket result = Assert.Single(Results());
        Assert.Equal(ItemRequestResult.TargetFull, result.Result);
        Assert.Equal(2, result.Slots.Length);
    }

    [Fact]
    public void Refresh_the_stats_and_keep_the_share_of_health_when_gear_goes_on()
    {
        _w.Character.CurrentHealth = 120;
        _w.Character.Container(InventoryType.Bag).Load([Item(0, EquipTemplates.Chestguard)]);

        Move(54, InventoryType.Bag, 0, InventoryType.Equipment, EquipmentSlots.Chest);

        Assert.Equal(ItemRequestResult.Ok, Assert.Single(Results()).Result);
        Assert.Equal(260u, _w.Character.Health);
        Assert.Equal(130u, _w.Character.CurrentHealth);
        Assert.Equal(8u, _w.Character.Stats!.Value.Armor);
    }

    [Fact]
    public void Refresh_the_stats_when_gear_comes_off()
    {
        _w.Character.Container(InventoryType.Equipment).Load([Item(EquipmentSlots.Chest, EquipTemplates.Chestguard)]);
        CharacterStatsRefresh.Apply(_w.Character, _w.Data, CurrentValues.Refill);
        Assert.Equal(260u, _w.Character.Health);

        Move(55, InventoryType.Equipment, EquipmentSlots.Chest, InventoryType.Bag, 0);

        Assert.Equal(240u, _w.Character.Health);
        Assert.Equal(240u, _w.Character.CurrentHealth);
    }

    [Fact]
    public void Leave_the_stats_alone_for_a_move_inside_the_bag()
    {
        _w.Character.Container(InventoryType.Bag).Load([Item(0, EquipTemplates.Chestguard)]);
        _w.Character.CurrentHealth = 120;
        _w.Character.SaveState.Acknowledge(_w.Character.SaveState.TakeMarks());

        Move(56, InventoryType.Bag, 0, InventoryType.Bag, 1);

        Assert.Equal(120u, _w.Character.CurrentHealth);
        Assert.False(_w.Character.SaveState.StatsDirty);
    }

    [Fact]
    public void Answer_NotFound_when_the_inventory_throws()
    {
        var economy = Substitute.For<ICharacterEconomy>();
        economy.InventoryOf(Arg.Any<CharacterEntity>()).Returns(_ => throw new InvalidOperationException("boom"));

        MoveHandler(economy).Execute(_w.Connection, new CItemMovePacket
        {
            RequestId = 57, FromContainer = (uint)InventoryType.Bag, FromSlot = 0,
            ToContainer = (uint)InventoryType.Bag, ToSlot = 1,
        });

        SItemResultPacket result = Assert.Single(Results());
        Assert.Equal((57u, ItemRequestResult.NotFound), (result.RequestId, result.Result));
    }

    [Fact]
    public void Destroy_part_of_a_stack_and_answer_Ok()
    {
        _w.Character.Container(InventoryType.Bag).Load([Item(0, Potion, count: 5)]);

        Destroy(60, InventoryType.Bag, 0, count: 2);

        Assert.Equal((60u, ItemRequestResult.Ok), (Results()[0].RequestId, Results()[0].Result));
        Assert.Equal(3u, At(_w.Character, InventoryType.Bag, 0).Count);
    }

    [Fact]
    public void Refuse_to_destroy_what_cannot_be_destroyed_and_name_its_slot()
    {
        _w.Character.Container(InventoryType.Bag).Load([Item(2, EquipTemplates.Heirloom)]);

        Destroy(61, InventoryType.Bag, 2);

        SItemResultPacket result = Assert.Single(Results());
        Assert.Equal(ItemRequestResult.CannotDestroy, result.Result);
        Assert.Equal(EquipTemplates.Heirloom.Id.Value, Assert.Single(result.Slots).Item!.ItemTemplateId);
    }

    [Fact]
    public void Refresh_the_stats_when_a_worn_item_is_destroyed()
    {
        _w.Character.Container(InventoryType.Equipment).Load([Item(EquipmentSlots.Chest, EquipTemplates.Chestguard)]);
        CharacterStatsRefresh.Apply(_w.Character, _w.Data, CurrentValues.Refill);

        Destroy(62, InventoryType.Equipment, EquipmentSlots.Chest);

        Assert.Equal(ItemRequestResult.Ok, Assert.Single(Results()).Result);
        Assert.Equal(240u, _w.Character.Health);
    }
}
