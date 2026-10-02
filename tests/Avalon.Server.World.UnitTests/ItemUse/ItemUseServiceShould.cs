using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Combat;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Inventory;
using Avalon.World.Persistence;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using NSubstitute;
using Xunit;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;
using static Avalon.Server.World.UnitTests.ItemUse.ItemUseWorld;

namespace Avalon.Server.World.UnitTests.ItemUse;

/// <summary>One CMSG_ITEM_USE, one SMSG_ITEM_USE_RESULT: checks in order, then the script or a cast.</summary>
public class ItemUseServiceShould : IAsyncLifetime
{
    private ItemUseWorld _w = null!;

    public async Task InitializeAsync() => _w = await CreateAsync();

    public Task DisposeAsync()
    {
        _w.Dispose();
        return Task.CompletedTask;
    }

    private (uint RequestId, ItemUseResult Result) Only()
    {
        SItemUseResultPacket result = Assert.Single(_w.Results());
        return (result.RequestId, result.Result);
    }

    [Fact]
    public void Answer_Dead_and_change_nothing()
    {
        _w.Bag(Item(0, Tonic, count: 5));
        _w.Character.IsDead = true;

        _w.Use(1, 0);

        Assert.Equal((1u, ItemUseResult.Dead), Only());
        Assert.Equal(5u, At(_w.Character, InventoryType.Bag, 0).Count);
    }

    [Theory]
    [InlineData((uint)InventoryType.Bag, 3u)]     // empty
    [InlineData((uint)InventoryType.Bag, 400u)]   // out of range
    [InlineData((uint)InventoryType.Bank, 0u)]    // not the Bag: only Bag items are used
    [InlineData(9u, 0u)]                          // no such container
    public void Answer_NotFound_for_a_slot_that_holds_nothing_usable(uint container, uint slot)
    {
        _w.Bag(Item(0, Tonic, count: 5));

        _w.Service.Use(_w.Client.Connection, _w.Character, 2, container, slot);

        Assert.Equal((2u, ItemUseResult.NotFound), Only());
    }

    [Fact]
    public void Answer_NotUsable_for_an_item_with_no_script_or_a_template_gone()
    {
        _w.Bag(Item(0, Trinket), Item(1, EquipTemplates.Ghost));

        _w.Use(3, 0);
        _w.Use(4, 1);

        Assert.Equal([ItemUseResult.NotUsable, ItemUseResult.NotUsable], _w.Results().Select(r => r.Result));
    }

    [Fact]
    public void Run_the_script_consume_one_and_answer_Ok()
    {
        _w.Bag(Item(0, Tonic, count: 5));

        _w.Use(5, 0);

        Assert.Equal((5u, ItemUseResult.Ok), Only());
        Assert.Equal(["used"], _w.Lines());
        InventoryItemAssert(4u);
        Assert.Equal(SaveState.Changed, _w.Character.SaveState.ItemState(At(_w.Character, InventoryType.Bag, 0).InstanceId));
    }

    private void InventoryItemAssert(uint count) => Assert.Equal(count, At(_w.Character, InventoryType.Bag, 0).Count);

    /// <summary>The next save deletes the item.</summary>
    [Fact]
    public void Empty_the_slot_and_mark_the_item_removed_when_the_last_one_is_used()
    {
        var last = Item(0, Tonic, count: 1);
        _w.Bag(last);

        _w.Use(6, 0);

        Assert.Equal((6u, ItemUseResult.Ok), Only());
        Assert.False(_w.Character.Container(InventoryType.Bag).TryGet(0, out _));
        Assert.Equal(SaveState.Removed, _w.Character.SaveState.ItemState(last.InstanceId));
    }

    /// <summary>The cooldown starts with the use itself, so a second request in one tick cannot use the item twice.</summary>
    [Fact]
    public void Answer_a_second_use_in_the_same_tick_OnCooldown_and_consume_once()
    {
        _w.Bag(Item(0, Tonic, count: 5));

        _w.Use(7, 0);
        _w.Use(8, 0);

        List<SItemUseResultPacket> results = _w.Results();
        Assert.Equal([ItemUseResult.Ok, ItemUseResult.OnCooldown], results.Select(r => r.Result));
        Assert.Equal(30000u, results[1].CooldownMs);
        InventoryItemAssert(4u);
    }

    [Fact]
    public void Share_the_groups_cooldown_and_count_it_down()
    {
        _w.Bag(Item(0, Tonic, count: 5), Item(1, Elixir, count: 5));

        _w.Use(9, 0);
        _w.Time.Advance(TimeSpan.FromSeconds(10));
        _w.Use(10, 1);
        _w.Time.Advance(TimeSpan.FromSeconds(20));
        _w.Use(11, 1);

        List<SItemUseResultPacket> results = _w.Results();
        Assert.Equal([ItemUseResult.Ok, ItemUseResult.OnCooldown, ItemUseResult.Ok], results.Select(r => r.Result));
        Assert.Equal(20000u, results[1].CooldownMs);
    }

    [Fact]
    public void Answer_AlreadyCasting_while_an_ability_is_casting()
    {
        var casting = Substitute.For<IAbility>();
        casting.Casting.Returns(true);
        _w.Character.Spells.Load([casting]);
        _w.Bag(Item(0, Tonic, count: 5));

        _w.Use(12, 0);

        Assert.Equal((12u, ItemUseResult.AlreadyCasting), Only());
        InventoryItemAssert(5u);
    }

    [Fact]
    public void Answer_Refused_with_the_scripts_line_and_start_no_cooldown()
    {
        _w.Bag(Item(0, Refuser));

        _w.Use(13, 0);

        SItemUseResultPacket result = Assert.Single(_w.Results());
        Assert.Equal((ItemUseResult.Refused, "Not now."), (result.Result, result.Message));
        Assert.Equal(TimeSpan.Zero, _w.Character.ItemCooldowns.Remaining(Refuser.Id, null, _w.Time.GetUtcNow()));
    }

    [Fact]
    public void Answer_InternalError_when_the_script_throws_and_consume_nothing_and_start_no_cooldown()
    {
        _w.Bag(Item(0, Thrower, count: 2));

        _w.Use(14, 0);

        Assert.Equal((14u, ItemUseResult.InternalError), Only());
        InventoryItemAssert(2u);
        Assert.Equal(TimeSpan.Zero, _w.Character.ItemCooldowns.Remaining(Thrower.Id, null, _w.Time.GetUtcNow()));
    }

    [Fact]
    public void Answer_InternalError_for_a_script_that_does_not_exist_or_cannot_be_built()
    {
        _w.Bag(Item(0, Missing), Item(1, Hungry));

        _w.Use(15, 0);
        _w.Use(16, 1);

        Assert.Equal([ItemUseResult.InternalError, ItemUseResult.InternalError], _w.Results().Select(r => r.Result));
    }

    [Fact]
    public void Equip_gear_through_the_same_request()
    {
        _w.Bag(Item(0, EquipTemplates.Longsword));

        _w.Use(17, 0);

        Assert.Equal((17u, ItemUseResult.Ok), Only());
        Assert.Equal(EquipTemplates.Longsword.Id, At(_w.Character, InventoryType.Equipment, EquipmentSlots.MainHand).TemplateId);
    }

    [Fact]
    public void Start_a_cast_bar_and_answer_once_it_completes()
    {
        _w.Bag(Item(0, Scroll, count: 2));

        _w.Use(18, 0);
        Assert.Empty(_w.Results());
        Assert.Equal(["cast started"], _w.Lines());
        SUnitStartCastPacket start = Assert.Single(_w.Client.Read<SUnitStartCastPacket>(NetworkPacketType.SMSG_UNIT_START_CAST));
        Assert.Equal((Scroll.Id.Value, 3f), (start.ItemTemplateId, start.CastTime));

        _w.Tick(3.1);

        Assert.Equal((18u, ItemUseResult.Ok), Only());
        Assert.Equal(["cast started", "used"], _w.Lines());
        Assert.Equal(start.CastId,
            Assert.Single(_w.Client.Read<SUnitFinishCastPacket>(NetworkPacketType.SMSG_UNIT_FINISH_CAST)).CastId);
        InventoryItemAssert(1u);
    }

    [Fact]
    public void Interrupt_the_cast_when_the_character_moves()
    {
        _w.Bag(Item(0, Scroll, count: 2));
        _w.Use(19, 0);

        _w.Character.Position = new Avalon.Common.Mathematics.Vector3(1, 0, 0);
        _w.Tick(0.1);

        Assert.Equal((19u, ItemUseResult.Interrupted), Only());
        Assert.Equal(["cast started", "cast interrupted"], _w.Lines());
        Assert.Single(_w.Client.Read<SCharacterInterruptedCastPacket>(NetworkPacketType.SMSG_INTERRUPTED_CAST));
        InventoryItemAssert(2u);
    }

    [Fact]
    public void Interrupt_the_cast_when_the_character_dies()
    {
        _w.Bag(Item(0, Scroll, count: 2));
        _w.Use(20, 0);

        _w.Character.IsDead = true;
        _w.Tick(0.1);

        Assert.Equal((20u, ItemUseResult.Interrupted), Only());
    }

    /// <summary>At completion the used item must still be the same instance in the same Bag slot.</summary>
    [Fact]
    public void Interrupt_the_cast_when_the_item_left_its_slot()
    {
        _w.Bag(Item(0, Scroll, count: 2));
        _w.Use(21, 0);

        Assert.Equal(ItemRequestResult.Ok,
            _w.Inventory.TryMove(EquipTemplates.Bag(0), EquipTemplates.Bag(5), count: null, bankAccessible: false));
        _w.Tick(3.1);

        Assert.Equal((21u, ItemUseResult.Interrupted), Only());
        Assert.Equal(2u, At(_w.Character, InventoryType.Bag, 5).Count);
        Assert.Empty(_w.Client.Read<SUnitFinishCastPacket>(NetworkPacketType.SMSG_UNIT_FINISH_CAST));
    }

    /// <summary>A use that passes its checks ends the running item cast and goes on.</summary>
    [Fact]
    public void Interrupt_a_running_item_cast_when_another_use_starts()
    {
        _w.Bag(Item(0, Scroll, count: 2), Item(1, Tonic, count: 5));
        _w.Use(22, 0);

        _w.Use(23, 1);

        Assert.Equal([(22u, ItemUseResult.Interrupted), (23u, ItemUseResult.Ok)],
            _w.Results().Select(r => (r.RequestId, r.Result)));
        Assert.Equal(2u, At(_w.Character, InventoryType.Bag, 0).Count);
    }

    [Fact]
    public void Keep_the_cast_when_the_new_use_is_refused()
    {
        _w.Bag(Item(0, Scroll, count: 2), Item(1, Refuser));
        _w.Use(24, 0);

        _w.Use(25, 1);
        _w.Tick(3.1);

        Assert.Equal([(25u, ItemUseResult.Refused), (24u, ItemUseResult.Ok)],
            _w.Results().Select(r => (r.RequestId, r.Result)));
    }

    /// <summary>A cast-time use whose interruption hook throws is answered InternalError, spending nothing.</summary>
    [Fact]
    public void Answer_InternalError_when_the_interruption_hook_throws_and_consume_nothing_and_start_no_cooldown()
    {
        _w.Bag(Item(0, Fizzler, count: 2));
        _w.Use(26, 0);

        _w.Character.Position = new Avalon.Common.Mathematics.Vector3(1, 0, 0);
        _w.Tick(0.1);

        Assert.Equal((26u, ItemUseResult.InternalError), Only());
        InventoryItemAssert(2u);
        Assert.Equal(TimeSpan.Zero, _w.Character.ItemCooldowns.Remaining(Fizzler.Id, null, _w.Time.GetUtcNow()));
    }
}
