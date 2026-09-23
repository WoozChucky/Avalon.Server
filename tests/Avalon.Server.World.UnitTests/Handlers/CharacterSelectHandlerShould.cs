using System.IO;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Server.World.UnitTests.Characters;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Handlers;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Respawn;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>
/// Select builds a character; it no longer puts one in the world. Spawning at select made a
/// character visible to MapInstance.Update while its client was still assembling the map it had
/// just been sent, so other players saw someone who was not there yet.
/// </summary>
public class CharacterSelectHandlerShould
{
    private const ushort TownMapId = 1;
    private static readonly CharacterId TheCharacter = new(7);
    private static readonly AccountId TheAccount = new(42L);

    private sealed class Fixture
    {
        public required CharacterSelectHandler Handler { get; init; }
        public required IWorldConnection Connection { get; init; }
        public required IWorld World { get; init; }
        public required IMapInstance Instance { get; init; }
        public required List<NetworkPacketType> Sent { get; init; }
        public required List<NetworkPacket> SentPackets { get; init; }
    }

    private static async Task<Fixture> BuildAsync(
        IReadOnlyCollection<CharacterInventory>? inventoryRows = null,
        IReadOnlyCollection<ItemInstance>? itemInstances = null)
    {
        var row = new Character
        {
            Id = TheCharacter,
            AccountId = TheAccount,
            Name = "Tester",
            Class = CharacterClass.Warrior,
            Level = 1,
            Map = TownMapId,
            X = 1, Y = 2, Z = 3
        };

        var characterRepository = Substitute.For<ICharacterRepository>();
        characterRepository.FindByIdAndAccountAsync(TheCharacter, TheAccount, Arg.Any<CancellationToken>())
            .Returns(row);
        characterRepository.UpdateAsync(row, Arg.Any<CancellationToken>()).Returns(row);

        var inventoryRepository = Substitute.For<ICharacterInventoryRepository>();
        inventoryRepository.GetByCharacterIdAsync(TheCharacter, Arg.Any<CancellationToken>())
            .Returns(inventoryRows ?? Array.Empty<CharacterInventory>());

        var itemInstanceRepository = Substitute.For<IItemInstanceRepository>();
        itemInstanceRepository.GetByCharacterIdWithTemplateAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ItemInstance>)(itemInstances?.ToList() ?? new List<ItemInstance>()));

        var abilityRepository = Substitute.For<ICharacterAbilityRepository>();
        abilityRepository.GetCharacterAbilitiesAsync(TheCharacter, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CharacterAbility>());

        var instance = Substitute.For<IMapInstance>();
        instance.InstanceId.Returns(Guid.NewGuid());

        var registry = Substitute.For<IInstanceRegistry>();
        registry.GetOrCreateTownInstanceAsync(new MapTemplateId(TownMapId), Arg.Any<ushort>())
            .Returns(Task.FromResult(instance));

        // Built first: configuring a substitute inside a Returns() argument breaks NSubstitute's
        // last-call tracking.
        StaticData staticData = await EmptyStaticDataAsync();

        IWorld world = Substitute.For<IWorld>();
        world.InstanceRegistry.Returns(registry);
        world.MapTemplates.Returns(new List<MapTemplate>
        {
            new()
            {
                Id = new MapTemplateId(TownMapId), MapType = MapType.Town,
                Name = "town", Description = "town", MaxPlayers = 30
            }
        });
        world.Data.Returns(staticData);

        var sent = new List<NetworkPacketType>();
        var sentPackets = new List<NetworkPacket>();
        IWorldConnection connection = PendingSpawnConnection.Create();
        connection.AccountId.Returns(TheAccount);
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        connection.When(c => c.Send(Arg.Any<NetworkPacket>()))
            .Do(ci =>
            {
                NetworkPacket packet = ci.Arg<NetworkPacket>();
                sent.Add(packet.Header.Type);
                sentPackets.Add(packet);
            });
        RunContinuationsInline<Character>(connection);
        RunContinuationsInline<IMapInstance>(connection);
        RunContinuationsInline<IReadOnlyCollection<CharacterInventory>>(connection);
        RunContinuationsInline<IReadOnlyList<ItemInstance>>(connection);
        RunContinuationsInline<IReadOnlyCollection<CharacterAbility>>(connection);

        var handler = new CharacterSelectHandler(
            NullLogger<CharacterSelectHandler>.Instance,
            NullLoggerFactory.Instance,
            characterRepository,
            inventoryRepository,
            itemInstanceRepository,
            abilityRepository,
            Substitute.For<IChunkLibrary>(),
            world,
            Substitute.For<IRespawnTargetResolver>(),
            Options.Create(new RegenConfiguration()));

        return new Fixture
        {
            Handler = handler, Connection = connection, World = world,
            Instance = instance, Sent = sent, SentPackets = sentPackets
        };
    }

    /// <summary>
    /// A row for every slot, paired with the item instance it points at -- the two halves
    /// InventoryAssembler joins. Mirrors CharacterSelectChainShould.GiveTheCharacter.
    /// </summary>
    private static (List<CharacterInventory> Rows, List<ItemInstance> Instances) BuildInventory(
        params (InventoryType Container, ushort Slot, ulong Template)[] items)
    {
        var rows = new List<CharacterInventory>();
        var instances = new List<ItemInstance>();

        foreach ((InventoryType container, ushort slot, ulong template) in items)
        {
            var id = new ItemInstanceId(Guid.NewGuid());
            rows.Add(new CharacterInventory
            {
                CharacterId = TheCharacter, Container = container, Slot = slot, ItemId = id
            });
            instances.Add(new ItemInstance
            {
                Id = id, TemplateId = new ItemTemplateId(template), CharacterId = TheCharacter,
                Count = 1, Durability = 100, Flags = ItemInstanceFlags.None
            });
        }

        return (rows, instances);
    }

    /// <summary>
    /// Payload bytes are unencrypted: FakeAvalonCryptoSession.Encrypt is a pass-through, so what
    /// SInventorySnapshotPacket.Create wrote is exactly what protobuf-net reads back here.
    /// </summary>
    private static SInventorySnapshotPacket DeserializeInventorySnapshot(Fixture f)
    {
        NetworkPacket packet = Assert.Single(
            f.SentPackets, p => p.Header.Type == NetworkPacketType.SMSG_INVENTORY_SNAPSHOT);
        using var stream = new MemoryStream(packet.Payload);
        return Serializer.Deserialize<SInventorySnapshotPacket>(stream);
    }

    /// <summary>
    /// The select chain is several database round trips deep and hands every result back through
    /// the connection's continuation queue, which the tick drains. Running each one where it is
    /// queued plays the whole chain out inside Execute.
    /// </summary>
    private static void RunContinuationsInline<T>(IWorldConnection connection) =>
        connection.When(c => c.EnqueueContinuation(Arg.Any<Task<T>>(), Arg.Any<Action<T>>()))
            .Do(ci => ci.Arg<Action<T>>()(ci.Arg<Task<T>>().Result));

    private static async Task<StaticData> EmptyStaticDataAsync()
    {
        var levels = Substitute.For<ICharacterLevelExperienceRepository>();
        levels.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<CharacterLevelExperience>());
        var stats = Substitute.For<IClassLevelStatRepository>();
        stats.FindAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ClassLevelStat>());
        var createInfos = Substitute.For<ICharacterCreateInfoRepository>();
        createInfos.FindAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<CharacterCreateInfo>());
        var items = Substitute.For<IItemTemplateRepository>();
        items.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<ItemTemplate>());
        var abilities = Substitute.For<IAbilityTemplateRepository>();
        abilities.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<AbilityTemplate>());

        var data = new StaticData(createInfos, stats, items, abilities, levels);
        await data.LoadAsync(CancellationToken.None);
        return data;
    }

    [Fact]
    public async Task Hold_the_built_character_back_instead_of_spawning_it()
    {
        Fixture f = await BuildAsync();

        f.Handler.Execute(f.Connection, new CCharacterSelectedPacket { CharacterId = TheCharacter });

        f.World.DidNotReceiveWithAnyArgs().SpawnInInstance(default!, default!);
        Assert.Null(f.Connection.Character);
        f.Connection.Received(1).SetPendingSpawn(Arg.Any<ICharacter>(), f.Instance, Arg.Any<long>());
    }

    /// <summary>
    /// The character is held back; everything the client needs in order to do the loading is not.
    /// Sending less than this leaves a client with nothing to report having loaded.
    /// </summary>
    [Fact]
    public async Task Still_send_the_character_and_its_abilities()
    {
        Fixture f = await BuildAsync();

        f.Handler.Execute(f.Connection, new CCharacterSelectedPacket { CharacterId = TheCharacter });

        Assert.Contains(NetworkPacketType.SMSG_CHARACTER_SELECTED, f.Sent);
        Assert.Contains(NetworkPacketType.SMSG_CHARACTER_ABILITIES, f.Sent);
    }

    /// <summary>
    /// Asserts the wire contents, not just container counts: a bank item leaking into the DTO
    /// array would pass a container-level check (the bank container legitimately holds it) but
    /// must fail here, since the packet is what the client actually receives.
    /// </summary>
    [Fact]
    public async Task Send_Equipment_And_Bag_Items_In_The_Snapshot_But_Not_The_Bank()
    {
        (List<CharacterInventory> rows, List<ItemInstance> instances) = BuildInventory(
            (InventoryType.Equipment, 0, 10), (InventoryType.Equipment, 1, 11),
            (InventoryType.Bag, 0, 20), (InventoryType.Bag, 1, 21), (InventoryType.Bag, 2, 22),
            (InventoryType.Bank, 0, 30));
        Fixture f = await BuildAsync(rows, instances);

        f.Handler.Execute(f.Connection, new CCharacterSelectedPacket { CharacterId = TheCharacter });

        SInventorySnapshotPacket snapshot = DeserializeInventorySnapshot(f);
        Assert.Equal(5, snapshot.Items.Length);
        Assert.DoesNotContain(snapshot.Items, item => item.Container == (ushort)InventoryType.Bank);
    }

    /// <summary>
    /// The packet must still be sent for an empty inventory: an absent packet and an empty one
    /// mean different things to the client, and only the wire content distinguishes them.
    /// </summary>
    [Fact]
    public async Task Send_An_Empty_Snapshot_When_The_Character_Has_No_Items()
    {
        Fixture f = await BuildAsync();

        f.Handler.Execute(f.Connection, new CCharacterSelectedPacket { CharacterId = TheCharacter });

        SInventorySnapshotPacket snapshot = DeserializeInventorySnapshot(f);
        // protobuf-net writes nothing for a zero-length repeated field, so an empty array round
        // trips as null rather than []; either is "no items" on the wire.
        Assert.Empty(snapshot.Items ?? []);
    }

    [Fact]
    public async Task Stamp_the_pending_spawn_with_the_moment_it_started_waiting()
    {
        Fixture f = await BuildAsync();
        long before = DateTime.UtcNow.Ticks;

        f.Handler.Execute(f.Connection, new CCharacterSelectedPacket { CharacterId = TheCharacter });

        long after = DateTime.UtcNow.Ticks;
        f.Connection.Received(1).SetPendingSpawn(
            Arg.Any<ICharacter>(), f.Instance,
            Arg.Is<long>(ticks => ticks >= before && ticks <= after));
    }

    /// <summary>
    /// A character waiting on the barrier is built and already marked online in the database. A
    /// second select would overwrite the pending spawn and leave that row online with nothing
    /// holding the entity that would have cleared it.
    /// </summary>
    [Fact]
    public async Task Close_a_connection_that_selects_again_while_a_spawn_is_pending()
    {
        Fixture f = await BuildAsync();
        // Built outside the Returns(): configuring a substitute inside one breaks NSubstitute's
        // last-call tracking.
        ICharacter pendingCharacter = PendingSpawnConnection.Character();
        f.Connection.PendingSpawn.Returns(new PendingSpawn(
            pendingCharacter, f.Instance, DateTime.UtcNow.Ticks));

        f.Handler.Execute(f.Connection, new CCharacterSelectedPacket { CharacterId = TheCharacter });

        f.Connection.Received().Close(Arg.Any<bool>());
        f.Connection.DidNotReceiveWithAnyArgs().SetPendingSpawn(default!, default!, default);
    }
}
