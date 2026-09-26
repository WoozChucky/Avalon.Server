using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Handlers;
using Avalon.World.Inventory;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>
/// The bank end to end (spec #463): opened through the banker's dialogue, used by item requests,
/// closed by the leash, refused once closed, and never sent outside an open bank.
/// </summary>
public class BankFlowShould : IAsyncLifetime
{
    private BankerWorld _w = null!;

    public async Task InitializeAsync() => _w = await BankerWorld.CreateAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private void Talk()
    {
        new InteractHandler(NullLogger<InteractHandler>.Instance, _w.World).Execute(_w.Connection,
            new CInteractPacket { TargetGuid = BankerWorld.BankerGuid.RawValue });
        new DialogueChooseHandler(NullLogger<DialogueChooseHandler>.Instance, _w.World).Execute(_w.Connection,
            new CDialogueChoosePacket
            {
                TargetGuid = BankerWorld.BankerGuid.RawValue, NodeId = BankerWorld.BankerRoot,
                OptionId = BankerWorld.OpenBankOption,
            });
    }

    private void Move(uint request, InventoryType from, uint fromSlot, InventoryType to, uint toSlot) =>
        new ItemMoveHandler(NullLogger<ItemMoveHandler>.Instance, _w.World, new CharacterEconomy(_w.World, new ItemIdAllocator()))
            .Execute(_w.Connection, new CItemMovePacket
            {
                RequestId = request, FromContainer = (uint)from, FromSlot = fromSlot, ToContainer = (uint)to, ToSlot = toSlot,
            });

    private ItemRequestResult LastResult() =>
        _w.Read<SItemResultPacket>(NetworkPacketType.SMSG_ITEM_RESULT).Last().Result;

    private List<SInventoryUpdatePacket> Updates() =>
        _w.Read<SInventoryUpdatePacket>(NetworkPacketType.SMSG_INVENTORY_UPDATE);

    [Fact]
    public void Deposit_and_withdraw_at_the_banker_and_send_the_bank_slots_that_changed()
    {
        _w.Character.Container(InventoryType.Bag).Load([Item(0, Potion, count: 5)]);
        Talk();
        Assert.Single(Updates());   // the bank snapshot

        Move(1, InventoryType.Bag, 0, InventoryType.Bank, 6);
        Assert.Equal(ItemRequestResult.Ok, LastResult());
        InventoryUpdateFlusher.Flush(_w.Connection);

        SInventoryUpdatePacket deposit = Updates().Last();
        Assert.Equal(5u, deposit.Slots.Single(s => s.Container == (ushort)InventoryType.Bank && s.Slot == 6).Item!.Count);
        Assert.Null(deposit.Slots.Single(s => s.Container == (ushort)InventoryType.Bag && s.Slot == 0).Item);

        Move(2, InventoryType.Bank, 6, InventoryType.Bag, 9);
        Assert.Equal(ItemRequestResult.Ok, LastResult());
        Assert.Equal(5u, At(_w.Character, InventoryType.Bag, 9).Count);
        Assert.Empty(_w.Character.Container(InventoryType.Bank).Items);
    }

    [Fact]
    public void Close_the_bank_when_the_player_walks_past_the_leash_and_refuse_it_after()
    {
        _w.Character.Container(InventoryType.Bag).Load([Item(0, Potion, count: 5)]);
        Talk();
        _w.Character.Position = new Vector3(0, 0, 25);

        Move(3, InventoryType.Bag, 0, InventoryType.Bank, 0);

        Assert.Equal(ItemRequestResult.BankClosed, LastResult());
        Assert.Single(_w.Read<SDialogueEndPacket>(NetworkPacketType.SMSG_DIALOGUE_END));
        Assert.Null(_w.Connection.CurrentDialogue);

        // Walking back does not reopen it: that takes the dialogue again.
        _w.Character.Position = Vector3.zero;
        Move(4, InventoryType.Bag, 0, InventoryType.Bank, 0);

        Assert.Equal(ItemRequestResult.BankClosed, LastResult());
        Assert.Single(_w.Read<SDialogueEndPacket>(NetworkPacketType.SMSG_DIALOGUE_END));
        Assert.Equal(5u, At(_w.Character, InventoryType.Bag, 0).Count);
    }

    [Fact]
    public void Refuse_the_bank_after_the_conversation_ends_with_farewell()
    {
        _w.Character.Container(InventoryType.Bag).Load([Item(0, Potion, count: 5)]);
        Talk();
        new DialogueChooseHandler(NullLogger<DialogueChooseHandler>.Instance, _w.World).Execute(_w.Connection,
            new CDialogueChoosePacket
            {
                TargetGuid = BankerWorld.BankerGuid.RawValue, NodeId = BankerWorld.BankerRoot,
                OptionId = BankerWorld.FarewellOption,
            });

        Move(5, InventoryType.Bag, 0, InventoryType.Bank, 0);

        Assert.Equal(ItemRequestResult.BankClosed, LastResult());
    }

    [Fact]
    public void Never_send_a_bank_slot_once_the_bank_has_closed()
    {
        InventoryItem banked = Item(3, Sword);
        _w.Character.Container(InventoryType.Bank).Load([banked]);
        Talk();
        _w.Connection.CurrentDialogue = null;   // the conversation ended some other way: leaving the instance
        int before = Updates().Count;

        new CharacterEconomy(_w.World, new ItemIdAllocator()).InventoryOf(_w.Character).TryRemove(banked.InstanceId, 1);
        InventoryUpdateFlusher.Flush(_w.Connection);

        Assert.Equal(before, Updates().Count);
    }
}
