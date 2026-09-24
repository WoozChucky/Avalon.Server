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
        public required IItemInstanceRepository ItemInstances { get; init; }
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
        itemInstanceRepository.GetByCharacterIdAsync(Arg.Any<CharacterId>(), Arg.Any<CancellationToken>())
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
            Instance = instance, Sent = sent, SentPackets = sentPackets,
            ItemInstances = itemInstanceRepository
        };
    }

    /// <summary>
    /// A row for every slot, paired with the item instance it points at -- the two halves
    /// InventoryAssembler joins. Mirrors CharacterSelectChainShould.GiveTheCharacter.
    ///
    /// Count/Durability/Flags default to 1/100/None so most call sites don't need to spell them
    /// out, but they are still per-item: a fixture that wants to catch a transposed Count/Durability
    /// or a hardcoded Flags = 0 must pass distinct, non-default values for at least one entry, since
    /// every value here landing on the same 1/100/None would make such a bug invisible.
    /// </summary>
    private static (List<CharacterInventory> Rows, List<ItemInstance> Instances) BuildInventory(
        params (InventoryType Container, ushort Slot, ulong Template, uint Count, uint Durability, ItemInstanceFlags Flags)[] items)
    {
        var rows = new List<CharacterInventory>();
        var instances = new List<ItemInstance>();

        foreach ((InventoryType container, ushort slot, ulong template, uint count, uint durability, ItemInstanceFlags flags) in items)
        {
            var id = new ItemInstanceId(Guid.NewGuid());
            rows.Add(new CharacterInventory
            {
                CharacterId = TheCharacter, Container = container, Slot = slot, ItemId = id
            });
            instances.Add(new ItemInstance
            {
                Id = id, TemplateId = new ItemTemplateId(template), CharacterId = TheCharacter,
                Count = count, Durability = durability, Flags = flags
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

        var data = new StaticData(createInfos, stats, items, abilities, levels,
            Substitute.For<ICreatureBaseStatRepository>(),
            Substitute.For<ICreatureRarityModifierRepository>());
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
    ///
    /// The equipment slot carries distinct, non-default Count/Durability/Flags -- a stack of 3
    /// with 77 durability remaining and the Broken flag set -- and every field of that slot is
    /// asserted end to end. A fixture where every item shared the same 1/100/None values would let
    /// CharacterSelectHandler.ToDtos transpose Count and Durability, or hardcode Flags = 0, without
    /// any test noticing: both compile silently (Count and Durability are both uint) and both were
    /// previously unasserted here.
    /// </summary>
    [Fact]
    public async Task Send_Equipment_And_Bag_Items_In_The_Snapshot_But_Not_The_Bank()
    {
        (List<CharacterInventory> rows, List<ItemInstance> instances) = BuildInventory(
            (InventoryType.Equipment, 0, 10, 3u, 77u, ItemInstanceFlags.Broken),
            (InventoryType.Equipment, 1, 11, 1u, 100u, ItemInstanceFlags.None),
            (InventoryType.Bag, 0, 20, 1u, 100u, ItemInstanceFlags.None),
            (InventoryType.Bag, 1, 21, 1u, 100u, ItemInstanceFlags.None),
            (InventoryType.Bag, 2, 22, 1u, 100u, ItemInstanceFlags.None),
            (InventoryType.Bank, 0, 30, 1u, 100u, ItemInstanceFlags.None));
        Fixture f = await BuildAsync(rows, instances);

        f.Handler.Execute(f.Connection, new CCharacterSelectedPacket { CharacterId = TheCharacter });

        SInventorySnapshotPacket snapshot = DeserializeInventorySnapshot(f);
        Assert.Equal(5, snapshot.Items.Length);
        Assert.DoesNotContain(snapshot.Items, item => item.Container == (ushort)InventoryType.Bank);

        ItemSlotDto slot = Assert.Single(
            snapshot.Items, item => item.ItemInstanceId == instances[0].Id.Value);
        Assert.Equal((ushort)InventoryType.Equipment, slot.Container);
        Assert.Equal((ushort)0, slot.Slot);
        Assert.Equal(10ul, slot.ItemTemplateId);
        Assert.Equal(3u, slot.Count);
        Assert.Equal(77u, slot.Durability);
        Assert.Equal((uint)ItemInstanceFlags.Broken, slot.Flags);
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

    /// <summary>
    /// Login reads only TemplateId, Count, Durability and Flags off each instance, and the client
    /// resolves template ids against the vendored item catalog -- so joining the 41-column
    /// ItemTemplate to every carried item loads a row nothing reads. The REST API's inventory
    /// endpoint does project the template, which is why both methods exist.
    ///
    /// Asserted at the repository seam rather than on the SQL: this repository has no integration
    /// test infrastructure, so which method login asks for is the only observable that
    /// distinguishes the two queries.
    /// </summary>
    [Fact]
    public async Task Ask_For_Item_Instances_Without_Joining_Their_Templates()
    {
        Fixture f = await BuildAsync();

        f.Handler.Execute(f.Connection, new CCharacterSelectedPacket { CharacterId = TheCharacter });

        await f.ItemInstances.Received(1)
            .GetByCharacterIdAsync(TheCharacter, Arg.Any<CancellationToken>());
        await f.ItemInstances.DidNotReceiveWithAnyArgs()
            .GetByCharacterIdWithTemplateAsync(default!, default);
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
