using System.IO;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Loot;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ProtoBuf;
using Xunit;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>The wiring around LootPickup: who hears what. The rules themselves are LootPickupShould's.</summary>
public class LootPickupHandlerShould
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly ObjectGuid DropGuid = new(ObjectType.Loot, 1);

    private readonly GroundLootStore _store = new();
    private readonly IMapInstance _instance = Substitute.For<IMapInstance, IGroundLootHost>();
    private readonly CharacterEntity _character = New(id: 7);
    private readonly List<NetworkPacket> _sent = [];
    private readonly IWorldConnection _connection = Substitute.For<IWorldConnection>();
    private readonly LootPickupHandler _handler;

    public LootPickupHandlerShould()
    {
        ((IGroundLootHost)_instance).Drops.Returns(_store);

        // Only the character's own instance is stubbed: any other id comes back null, so a handler
        // that looked the drop up elsewhere would answer NotFound.
        _character.InstanceId = new Guid("46000000-0000-0000-0000-000000000460");
        var registry = Substitute.For<IInstanceRegistry>();
        registry.GetInstanceById(Arg.Any<Guid>()).Returns((IMapInstance?)null);
        registry.GetInstanceById(_character.InstanceId).Returns(_instance);

        var world = Substitute.For<IWorld>();
        world.InstanceRegistry.Returns(registry);
        world.Configuration.Returns(new GameConfiguration());

        var economy = Substitute.For<ICharacterEconomy>();
        economy.InventoryOf(Arg.Any<CharacterEntity>()).Returns(ci => InventoryFor(ci.Arg<CharacterEntity>()));
        economy.WalletOf(Arg.Any<CharacterEntity>()).Returns(ci => new CharacterWallet(ci.Arg<CharacterEntity>(), 1_000));

        _connection.Character.Returns(_character);
        _connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        _connection.When(c => c.Send(Arg.Any<NetworkPacket>())).Do(ci => _sent.Add(ci.Arg<NetworkPacket>()));

        _handler = new LootPickupHandler(NullLogger<LootPickupHandler>.Instance, world, economy, new FixedTimeProvider(Now));
    }

    private void DropGold(Vector3 at) => _store.Add(new GroundLoot
    {
        Guid = DropGuid, Position = at, Gold = 25, OwnerCharacterId = 7, FreeForAllAt = Now.UtcDateTime.AddSeconds(30)
    });

    private void PickUp() => _handler.Execute(_connection, new CLootPickupPacket { LootGuid = DropGuid.RawValue });

    private SLootPickupResultPacket Result()
    {
        NetworkPacket packet = Assert.Single(_sent, p => p.Header.Type == NetworkPacketType.SMSG_LOOT_PICKUP_RESULT);
        using var stream = new MemoryStream(packet.Payload);
        return Serializer.Deserialize<SLootPickupResultPacket>(stream);
    }

    [Fact]
    public void Answer_The_Picker_And_Tell_Everyone_The_Drop_Is_Gone()
    {
        DropGold(Vector3.zero);

        PickUp();

        SLootPickupResultPacket result = Result();
        Assert.Equal(LootPickupResult.Ok, result.Result);
        Assert.Equal(DropGuid.RawValue, result.LootGuid);
        ((IGroundLootHost)_instance).Received(1).BroadcastLootDespawned(
            Arg.Is<IReadOnlyCollection<ObjectGuid>>(g => g.Single() == DropGuid));
        // The inventory update is not sent here: InventoryUpdateFlusher sends it at the end of the tick.
        Assert.True(_character.ClientChanges.MoneyChanged);
    }

    [Fact]
    public void Answer_A_Refusal_And_Tell_Nobody_Else()
    {
        DropGold(new Vector3(0f, 0f, 50f));

        PickUp();

        Assert.Equal(LootPickupResult.TooFar, Result().Result);
        ((IGroundLootHost)_instance).DidNotReceiveWithAnyArgs().BroadcastLootDespawned(default!);
    }

    [Fact]
    public void Answer_Not_Found_And_Tell_Everyone_When_A_Drops_Item_Template_Is_Gone()
    {
        // 999 is in no template list: an Items reload removed it after the kill rolled it.
        _store.Add(new GroundLoot
        {
            Guid = DropGuid, Position = Vector3.zero, ItemTemplateId = new ItemTemplateId(999), Count = 1,
            OwnerCharacterId = 7, FreeForAllAt = Now.UtcDateTime.AddSeconds(30)
        });

        PickUp();

        Assert.Equal(LootPickupResult.NotFound, Result().Result);
        Assert.Equal(0, _store.Count);
        ((IGroundLootHost)_instance).Received(1).BroadcastLootDespawned(
            Arg.Is<IReadOnlyCollection<ObjectGuid>>(g => g.Single() == DropGuid));
    }

    [Fact]
    public void Answer_Not_Found_When_The_Characters_Instance_Is_Gone()
    {
        var registry = Substitute.For<IInstanceRegistry>();
        registry.GetInstanceById(Arg.Any<Guid>()).Returns((IMapInstance?)null);
        var world = Substitute.For<IWorld>();
        world.InstanceRegistry.Returns(registry);
        world.Configuration.Returns(new GameConfiguration());
        var handler = new LootPickupHandler(NullLogger<LootPickupHandler>.Instance, world,
            Substitute.For<ICharacterEconomy>(), new FixedTimeProvider(Now));

        handler.Execute(_connection, new CLootPickupPacket { LootGuid = DropGuid.RawValue });

        Assert.Equal(LootPickupResult.NotFound, Result().Result);
    }

    [Fact]
    public void Say_Nothing_To_A_Dead_Character()
    {
        DropGold(Vector3.zero);
        _character.IsDead = true;

        PickUp();

        Assert.Empty(_sent);
        Assert.Equal(1, _store.Count);
    }

    [Fact]
    public void Say_Nothing_To_A_Connection_Without_A_Character()
    {
        _connection.Character.Returns((ICharacter?)null);

        PickUp();

        Assert.Empty(_sent);
    }

    // R9: the pickup reaches the client through the normal end-of-tick path, not a packet of its own.
    private SInventoryUpdatePacket FlushedUpdate()
    {
        Assert.DoesNotContain(_sent, p => p.Header.Type == NetworkPacketType.SMSG_INVENTORY_UPDATE);

        InventoryUpdateFlusher.Flush(_connection);

        NetworkPacket packet = Assert.Single(_sent, p => p.Header.Type == NetworkPacketType.SMSG_INVENTORY_UPDATE);
        using var stream = new MemoryStream(packet.Payload);
        return Serializer.Deserialize<SInventoryUpdatePacket>(stream);
    }

    [Fact]
    public void Send_A_Picked_Up_Item_In_The_End_Of_Tick_Inventory_Update()
    {
        _store.Add(new GroundLoot
        {
            Guid = DropGuid, Position = Vector3.zero, ItemTemplateId = Potion.Id, Count = 3,
            OwnerCharacterId = 7, FreeForAllAt = Now.UtcDateTime.AddSeconds(30)
        });

        PickUp();
        SInventoryUpdatePacket update = FlushedUpdate();

        InventorySlotUpdateDto slot = Assert.Single(update.Slots);
        Assert.Equal((ushort)InventoryType.Bag, slot.Container);
        Assert.Equal((ushort)0, slot.Slot);
        Assert.Equal(Potion.Id.Value, slot.Item!.ItemTemplateId);
        Assert.Equal(3u, slot.Item.Count);
        Assert.Null(update.Money);
    }

    [Fact]
    public void Send_Picked_Up_Gold_In_The_End_Of_Tick_Inventory_Update()
    {
        DropGold(Vector3.zero);

        PickUp();
        SInventoryUpdatePacket update = FlushedUpdate();

        Assert.Equal(25UL, update.Money);
        Assert.Empty(update.Slots ?? []);   // protobuf-net sends an empty repeated field as null
    }
}
