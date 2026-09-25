using System.IO;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
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
    private readonly List<NetworkPacket> _sent = [];

    private IWorldConnection ConnectionFor(CharacterEntity character)
    {
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        connection.When(c => c.Send(Arg.Any<NetworkPacket>())).Do(call => _sent.Add(call.Arg<NetworkPacket>()));
        return connection;
    }

    private static SInventoryUpdatePacket Read(NetworkPacket packet)
    {
        Assert.Equal(NetworkPacketType.SMSG_INVENTORY_UPDATE, packet.Header.Type);
        using var stream = new MemoryStream(packet.Payload);
        return Serializer.Deserialize<SInventoryUpdatePacket>(stream);
    }

    [Fact]
    public void Send_nothing_when_nothing_changed()
    {
        InventoryUpdateFlusher.Flush(ConnectionFor(New()));

        Assert.Empty(_sent);
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
        Assert.Equal((ushort)InventoryType.Bag, slot.Container);
        Assert.Equal((ushort)0, slot.Slot);
        Assert.Equal(5u, slot.Item!.Count);
        Assert.Equal(Potion.Id.Value, slot.Item.ItemTemplateId);
        Assert.Null(update.Money);
    }

    [Fact]
    public void Send_an_emptied_slot_with_no_item()
    {
        CharacterEntity character = New();
        InventoryItem potion = Item(4, Potion, count: 2);
        character.Container(InventoryType.Bag).Load([potion]);
        IWorldConnection connection = ConnectionFor(character);

        InventoryFor(character).TryRemove(potion.InstanceId, 2);
        InventoryUpdateFlusher.Flush(connection);

        InventorySlotUpdateDto slot = Assert.Single(Read(Assert.Single(_sent)).Slots);
        Assert.Equal((ushort)4, slot.Slot);
        Assert.Null(slot.Item);
    }

    [Fact]
    public void Include_money_only_when_it_changed()
    {
        CharacterEntity character = New(money: 40);
        IWorldConnection connection = ConnectionFor(character);

        new CharacterWallet(character, ulong.MaxValue).TryAddMoney(2);
        InventoryUpdateFlusher.Flush(connection);

        SInventoryUpdatePacket update = Read(Assert.Single(_sent));
        Assert.Equal(42UL, update.Money);
        Assert.Empty(update.Slots ?? []);   // protobuf-net sends an empty repeated field as null
    }

    /// <summary>A balance spent to zero is a change, and must not read as "unchanged" on the wire.</summary>
    [Fact]
    public void Send_a_balance_spent_to_zero_as_present()
    {
        CharacterEntity character = New(money: 40);
        IWorldConnection connection = ConnectionFor(character);

        new CharacterWallet(character, ulong.MaxValue).TrySpend(40);
        InventoryUpdateFlusher.Flush(connection);

        Assert.Equal(0UL, Read(Assert.Single(_sent)).Money);
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
}
