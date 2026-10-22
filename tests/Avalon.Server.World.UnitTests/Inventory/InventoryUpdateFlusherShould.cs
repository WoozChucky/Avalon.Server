using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using NSubstitute;
using ProtoBuf;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Inventory;

/// <summary>
/// One packet per connection per tick, absolute values, each touched slot at its final value.
/// A lost or duplicated packet cannot leave the client drifting.
/// </summary>
public class InventoryUpdateFlusherShould
{
    private readonly List<OutboundPacket> _sent = [];

    private IWorldConnection ConnectionFor(CharacterEntity character)
    {
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        connection.When(c => c.Send(Arg.Any<OutboundPacket>())).Do(call => _sent.Add(call.Arg<OutboundPacket>()));
        return connection;
    }

    private static SInventoryUpdatePacket Read(OutboundPacket packet)
    {
        Assert.Equal(NetworkPacketType.SMSG_INVENTORY_UPDATE, packet.Header.Type);
        using var stream = new MemoryStream(packet.PayloadMemory.ToArray());
        return Serializer.Deserialize<SInventoryUpdatePacket>(stream);
    }

    [Fact]
    public void Collapse_several_changes_to_a_slot_into_its_final_value()
    {
        CharacterEntity character = New();
        IWorldConnection connection = ConnectionFor(character);

        InventoryFor(character).TryAdd(Potion.Id, 3);
        InventoryFor(character).TryAdd(Potion.Id, 2);
        InventoryUpdateFlusher.Flush(connection);

        SInventoryUpdatePacket update = Read(Assert.Single(_sent));
        InventorySlotUpdateDto slot = Assert.Single(update.Slots);
        Assert.Equal(InventoryType.Bag, slot.Container);
        Assert.Equal((byte)0, slot.Slot);
        Assert.Equal(5u, slot.Item!.Count);
        Assert.Equal(Potion.Id.Value, slot.Item.ItemTemplateId);
        Assert.Null(update.Gold);
    }

    [Fact]
    public void Include_gold_only_when_it_changed()
    {
        CharacterEntity character = New(gold: 40);
        IWorldConnection connection = ConnectionFor(character);

        new CharacterWallet(character, ulong.MaxValue).TryAddGold(2);
        InventoryUpdateFlusher.Flush(connection);

        SInventoryUpdatePacket update = Read(Assert.Single(_sent));
        Assert.Equal(42UL, update.Gold);
        Assert.Empty(update.Slots ?? []);   // protobuf-net sends an empty repeated field as null
    }

    /// <summary>A balance spent to zero is a change, and must not read as "unchanged" on the wire.</summary>
    [Fact]
    public void Send_a_balance_spent_to_zero_as_present()
    {
        CharacterEntity character = New(gold: 40);
        IWorldConnection connection = ConnectionFor(character);

        new CharacterWallet(character, ulong.MaxValue).TrySpend(40);
        InventoryUpdateFlusher.Flush(connection);

        Assert.Equal(0UL, Read(Assert.Single(_sent)).Gold);
    }

    [Fact]
    public void Send_once_and_then_nothing_until_the_next_change()
    {
        CharacterEntity character = New();
        IWorldConnection connection = ConnectionFor(character);

        InventoryFor(character).TryAdd(Potion.Id, 1);
        InventoryUpdateFlusher.Flush(connection);
        InventoryUpdateFlusher.Flush(connection);

        Assert.Single(_sent);
        Assert.False(character.ClientChanges.HasChanges);
    }

    /// <summary>
    /// Regression guard: already passed before bank slots were recorded (they were never recorded
    /// then). Pins that a closed bank still keeps every Bank slot off the wire and forgets it.
    /// </summary>
    [Fact]
    public void Leave_bank_slots_out_and_forget_them_while_the_bank_is_closed()
    {
        CharacterEntity character = New();
        InventoryItem banked = Item(4, Sword);
        character.Container(InventoryType.Bank).Load([banked]);
        IWorldConnection connection = ConnectionFor(character);

        InventoryFor(character).TryRemove(banked.InstanceId, 1);
        InventoryUpdateFlusher.Flush(connection);

        Assert.Empty(_sent);
        Assert.False(character.ClientChanges.HasChanges);
    }

    [Fact]
    public void Send_bank_slots_with_the_rest_while_the_bank_is_open()
    {
        CharacterEntity character = New();
        InventoryItem banked = Item(4, Sword);
        character.Container(InventoryType.Bank).Load([banked]);
        IWorldConnection connection = ConnectionFor(character);
        var banker = new ObjectGuid(ObjectType.Creature, 90);
        connection.CurrentDialogue = (banker, new DialogueNodeId(1));
        character.OpenBankNpc = banker;

        InventoryFor(character).TryRemove(banked.InstanceId, 1);
        InventoryFor(character).TryAdd(Potion.Id, 1);
        InventoryUpdateFlusher.Flush(connection);

        SInventoryUpdatePacket update = Read(Assert.Single(_sent));
        Assert.Equal(2, update.Slots.Length);
        InventorySlotUpdateDto bank = update.Slots.Single(s => s.Container == InventoryType.Bank);
        Assert.Equal((byte)4, bank.Slot);
        Assert.Null(bank.Item);
    }

    /// <summary>
    /// Regression guard: already passed before bank slots were recorded. Pins that dropping the
    /// closed bank's slots does not drop the Bag slots changed in the same tick.
    /// </summary>
    [Fact]
    public void Send_the_bag_but_not_the_bank_from_one_tick_while_the_bank_is_closed()
    {
        CharacterEntity character = New();
        InventoryItem banked = Item(4, Sword);
        character.Container(InventoryType.Bank).Load([banked]);
        IWorldConnection connection = ConnectionFor(character);

        InventoryFor(character).TryRemove(banked.InstanceId, 1);
        InventoryFor(character).TryAdd(Potion.Id, 1);
        InventoryUpdateFlusher.Flush(connection);

        InventorySlotUpdateDto slot = Assert.Single(Read(Assert.Single(_sent)).Slots);
        Assert.Equal(InventoryType.Bag, slot.Container);
    }

    /// <summary>
    /// The order is the client's to rely on, whatever order the tick touched the slots in: Equipment, then the Bag,
    /// then the Bank, each by slot.
    /// </summary>
    [Fact]
    public void Send_the_slots_by_container_and_then_by_slot_whatever_order_they_changed_in()
    {
        CharacterEntity character = New();
        IWorldConnection connection = ConnectionFor(character);
        var banker = new ObjectGuid(ObjectType.Creature, 90);
        connection.CurrentDialogue = (banker, new DialogueNodeId(1));
        character.OpenBankNpc = banker;

        (InventoryType Container, byte Slot)[] touched =
        [
            (InventoryType.Bank, 2), (InventoryType.Bag, 11), (InventoryType.Equipment, 9), (InventoryType.Bag, 29),
            (InventoryType.Bank, 0), (InventoryType.Bag, 3), (InventoryType.Equipment, 1),
        ];
        foreach ((InventoryType container, byte slot) in touched)
            character.ClientChanges.RecordSlot(container, slot);
        InventoryUpdateFlusher.Flush(connection);

        Assert.Equal(
            [
                (InventoryType.Equipment, 1), (InventoryType.Equipment, 9), (InventoryType.Bag, 3), (InventoryType.Bag, 11),
                (InventoryType.Bag, 29), (InventoryType.Bank, 0), (InventoryType.Bank, 2),
            ],
            Read(Assert.Single(_sent)).Slots.Select(s => (s.Container, s.Slot)));
    }
}
