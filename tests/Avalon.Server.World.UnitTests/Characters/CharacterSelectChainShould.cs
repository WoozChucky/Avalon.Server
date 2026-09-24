using System.Net;
using System.Net.Sockets;
using Avalon.Common;
using Avalon.Common.Cryptography;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Character;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Characters;
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

namespace Avalon.Server.World.UnitTests.Characters;

/// <summary>
/// The select chain driven from the packet that starts it, against a real WorldConnection with its
/// real continuation queue -- because the interval this is about is the one where Character and
/// PendingSpawn are BOTH still null. A fixture handed a ready-made PendingSpawn starts after that
/// interval has closed and can only ever agree with itself.
/// </summary>
public class CharacterSelectChainShould : IDisposable
{
    private const ushort TownMapId = 1;
    private static readonly CharacterId TheCharacter = new(7);
    private static readonly CharacterId AnotherCharacter = new(8);
    private static readonly AccountId TheAccount = new(42L);

    private readonly TcpClient _clientSide;
    private readonly TcpClient _serverSide;
    private readonly Avalon.World.WorldConnection _connection;
    private readonly ICharacterRepository _characters = Substitute.For<ICharacterRepository>();
    private readonly ICharacterInventoryRepository _inventory = Substitute.For<ICharacterInventoryRepository>();
    private readonly IItemInstanceRepository _itemInstances = Substitute.For<IItemInstanceRepository>();
    private readonly CharacterSelectHandler _select;

    public CharacterSelectChainShould()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _clientSide = new TcpClient();
        _clientSide.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint!).Port);
        _serverSide = listener.AcceptTcpClient();
        listener.Stop();

        var server = Substitute.For<IWorldServer, IServerBase>();
        ((IServerBase)server).SendBufferCapacity.Returns(256);
        _connection = new Avalon.World.WorldConnection(
            server, _clientSide, NullLoggerFactory.Instance, Substitute.For<IPacketReader>())
        {
            AccountId = TheAccount
        };

        // The select handler sends, and a real connection seals what it sends. Agreeing a key with
        // a throwaway peer is the cheapest way to make Encrypt work.
        _connection.CryptoSession.Initialize(new CryptoManager().GetPublicKey());

        _select = BuildSelectHandler();
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
        _serverSide.Dispose();
    }

    /// <summary>
    /// ProcessContinuations snapshots its queue, so a continuation enqueued by one it just ran is
    /// deferred to the next call. One flush is therefore one step of the chain.
    /// </summary>
    private void Step(int times = 1)
    {
        for (int i = 0; i < times; i++)
            _connection.FlushContinuations();
    }

    private void StartSelect() =>
        _select.Execute(_connection, new CCharacterSelectedPacket { CharacterId = TheCharacter });

    /// <summary>
    /// A select that dies mid-chain leaves the connection in the guard's "mid-select" state
    /// permanently: SelectInProgress true, Character and PendingSpawn both null. Every handler that
    /// gates on selection state -- select, create, delete and list -- refuses from then on, so the
    /// player cannot do anything with their characters until they reconnect. The readiness barrier
    /// does not cover it: its sweep only inspects connections that already have a pending spawn,
    /// and a chain that faulted never produced one.
    /// </summary>
    [Fact]
    public void Cancel_A_Select_Whose_Chain_Faulted_Once_It_Has_Waited_Too_Long()
    {
        _inventory.GetByCharacterIdAsync(TheCharacter, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyCollection<CharacterInventory>>(
                new InvalidOperationException("the database went away mid-select")));

        StartSelect();
        Step(4); // far enough to run the inventory load, which faults

        Assert.True(_connection.SelectInProgress, "the faulted chain should have left the select wedged");
        Assert.Null(_connection.Character);
        Assert.Null(_connection.PendingSpawn);

        CharacterReadinessBarrier.CancelExpiredSelects(
            [_connection],
            _connection.SelectStartedTicks + TimeSpan.FromSeconds(30).Ticks,
            TimeSpan.FromSeconds(15),
            NullLogger.Instance);

        Assert.False(_connection.SelectInProgress);
        Assert.False(_connection.IsConnected);
    }

    /// <summary>
    /// The converse, so the sweep cannot be made to pass by cancelling everything: a select still
    /// inside its window is a select still working, and six database round trips take time.
    /// </summary>
    [Fact]
    public void Leave_A_Select_Alone_While_It_Is_Still_Inside_Its_Window()
    {
        StartSelect();
        Step(2);

        CharacterReadinessBarrier.CancelExpiredSelects(
            [_connection],
            _connection.SelectStartedTicks + TimeSpan.FromSeconds(5).Ticks,
            TimeSpan.FromSeconds(15),
            NullLogger.Instance);

        Assert.True(_connection.SelectInProgress);
        Assert.True(_connection.IsConnected);
    }

    [Fact]
    public void Say_a_select_is_under_way_for_every_step_before_the_pending_spawn_exists()
    {
        StartSelect();

        // Six steps now: find character, resolve instance, persist row, load inventory rows,
        // load item instances, load abilities.
        for (int step = 0; step < 5; step++)
        {
            Assert.True(_connection.SelectInProgress, $"select not marked in progress at step {step}");
            Assert.Null(_connection.Character);
            Assert.Null(_connection.PendingSpawn);
            Step();
        }

        Step();

        Assert.NotNull(_connection.PendingSpawn);
        Assert.False(_connection.SelectInProgress);
        Assert.Null(_connection.Character);
    }

    /// <summary>
    /// The pipelined case: CMSG_CHARACTER_DELETE behind CMSG_CHARACTER_SELECTED for the same
    /// account. Ownership is checked by the delete handler; state was not, so the row went.
    /// </summary>
    [Fact]
    public void Refuse_a_delete_that_lands_while_the_select_is_still_loading()
    {
        StartSelect();
        Step(2); // mid-chain: entity built, instance resolved, no pending spawn yet

        Assert.Null(_connection.Character);
        Assert.Null(_connection.PendingSpawn);

        new CharacterDeletetHandler(NullLogger<CharacterDeletetHandler>.Instance, _characters)
            .Execute(_connection, new CCharacterDeletePacket { CharacterId = TheCharacter });
        Step(2);

        _characters.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
        // One lookup, the select's: the delete was refused before it made its own.
        _characters.Received(1).FindByIdAndAccountAsync(
            TheCharacter, TheAccount, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Refuse_a_second_select_that_lands_while_the_first_is_still_loading()
    {
        StartSelect();
        Step(2);

        _select.Execute(_connection, new CCharacterSelectedPacket { CharacterId = AnotherCharacter });

        _characters.DidNotReceive().FindByIdAndAccountAsync(
            AnotherCharacter, TheAccount, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Refuse_a_character_list_that_lands_while_the_select_is_still_loading()
    {
        StartSelect();
        Step(2);

        new CharacterListHandler(NullLogger<CharacterListHandler>.Instance, _characters,
                Substitute.For<IWorld>())
            .Execute(_connection, new CCharacterListPacket());
        Step(2);

        _characters.DidNotReceiveWithAnyArgs().FindByAccountAsync(default!, default);
    }

    [Fact]
    public void Refuse_a_character_create_that_lands_while_the_select_is_still_loading()
    {
        StartSelect();
        Step(2);

        new CharacterCreateHandler(NullLogger<CharacterCreateHandler>.Instance, _characters,
                Substitute.For<ICharacterStatsRepository>(),
                Substitute.For<ICharacterAbilityRepository>(),
                Substitute.For<ICharacterInventoryRepository>(),
                Substitute.For<IItemInstanceRepository>(),
                Substitute.For<IWorld>())
            .Execute(_connection, new CCharacterCreatePacket());
        Step(2);

        _characters.DidNotReceiveWithAnyArgs().FindByAccountAsync(default!, default);
        _characters.DidNotReceiveWithAnyArgs().CreateAsync(default(Character)!, default);
    }

    /// <summary>
    /// A select that never reaches a pending spawn must not leave the connection permanently
    /// refusing everything.
    /// </summary>
    [Fact]
    public void Stop_saying_a_select_is_under_way_when_the_character_is_not_found()
    {
        _characters.FindByIdAndAccountAsync(AnotherCharacter, TheAccount, Arg.Any<CancellationToken>())
            .Returns((Character?)null);

        _select.Execute(_connection, new CCharacterSelectedPacket { CharacterId = AnotherCharacter });
        Assert.True(_connection.SelectInProgress);

        Step();

        Assert.False(_connection.SelectInProgress);
    }

    /// <summary>
    /// The row must not claim the player is in the world while their client is still loading it.
    /// </summary>
    [Fact]
    public void Persist_the_row_offline_while_the_client_is_still_loading()
    {
        StartSelect();
        Step(6);

        Assert.NotNull(_connection.PendingSpawn);
        _characters.Received(1).UpdateAsync(
            Arg.Is<Character>(c => c.Id == TheCharacter && !c.Online), Arg.Any<CancellationToken>());
    }

    private CharacterSelectHandler BuildSelectHandler()
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

        _characters.FindByIdAndAccountAsync(TheCharacter, TheAccount, Arg.Any<CancellationToken>())
            .Returns(row);
        _characters.UpdateAsync(Arg.Any<Character>(), Arg.Any<CancellationToken>()).Returns(row);

        _inventory.GetByCharacterIdAsync(TheCharacter, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CharacterInventory>());
        _itemInstances.GetByCharacterIdAsync(TheCharacter, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ItemInstance>());

        var abilities = Substitute.For<ICharacterAbilityRepository>();
        abilities.GetCharacterAbilitiesAsync(TheCharacter, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CharacterAbility>());

        var instance = Substitute.For<IMapInstance>();
        instance.InstanceId.Returns(Guid.NewGuid());
        var registry = Substitute.For<IInstanceRegistry>();
        registry.GetOrCreateTownInstanceAsync(new MapTemplateId(TownMapId), Arg.Any<ushort>())
            .Returns(Task.FromResult(instance));

        StaticData staticData = EmptyStaticData();

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

        return new CharacterSelectHandler(
            NullLogger<CharacterSelectHandler>.Instance,
            NullLoggerFactory.Instance,
            _characters,
            _inventory,
            _itemInstances,
            abilities,
            Substitute.For<IChunkLibrary>(),
            world,
            Substitute.For<IRespawnTargetResolver>(),
            Options.Create(new RegenConfiguration()));
    }

    private void GiveTheCharacter(params (InventoryType Container, ushort Slot, ulong Template)[] items)
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

        _inventory.GetByCharacterIdAsync(TheCharacter, Arg.Any<CancellationToken>()).Returns(rows);
        _itemInstances.GetByCharacterIdAsync(TheCharacter, Arg.Any<CancellationToken>())
            .Returns(instances);
    }

    /// <summary>
    /// All three containers -- equipment, bag and bank -- load from the rows InventoryAssembler
    /// joins, and Load() replaces their contents. This is chain/Load coverage, not wire coverage:
    /// it checks entity state reached through the pending spawn, never a packet. What actually
    /// reaches the client (equipment and bag only, never the bank) is asserted at the wire in
    /// CharacterSelectHandlerShould.Send_Equipment_And_Bag_Items_In_The_Snapshot_But_Not_The_Bank.
    /// </summary>
    [Fact]
    public void Load_Equipment_Bag_And_Bank_Into_Their_Containers()
    {
        GiveTheCharacter(
            (InventoryType.Equipment, 0, 10), (InventoryType.Equipment, 1, 11),
            (InventoryType.Bag, 0, 20), (InventoryType.Bag, 1, 21), (InventoryType.Bag, 2, 22),
            (InventoryType.Bank, 0, 30));

        StartSelect();
        Step(6);

        ICharacter character = _connection.PendingSpawn!.Character;
        Assert.Equal(2, character[InventoryType.Equipment].Items.Count);
        Assert.Equal(3, character[InventoryType.Bag].Items.Count);
        Assert.Single(character[InventoryType.Bank].Items);
    }

    /// <summary>
    /// An empty inventory is a fact the client needs, not an absence of one. Skipping the packet
    /// would leave it unable to tell "nothing" from "not told yet".
    /// </summary>
    [Fact]
    public void Reach_The_Pending_Spawn_With_An_Empty_Inventory()
    {
        StartSelect();
        Step(6);

        Assert.NotNull(_connection.PendingSpawn);
        Assert.Empty(_connection.PendingSpawn!.Character[InventoryType.Bag].Items);
    }

    private static StaticData EmptyStaticData()
    {
        var levels = Substitute.For<ICharacterLevelExperienceRepository>();
        levels.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<CharacterLevelExperience>());
        var stats = Substitute.For<IClassLevelStatRepository>();
        stats.FindAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ClassLevelStat>());
        var createInfos = Substitute.For<ICharacterCreateInfoRepository>();
        createInfos.FindAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<CharacterCreateInfo>());
        var items = Substitute.For<IItemTemplateRepository>();
        items.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<ItemTemplate>());
        var abilityTemplates = Substitute.For<IAbilityTemplateRepository>();
        abilityTemplates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<AbilityTemplate>());

        var data = new StaticData(createInfos, stats, items, abilityTemplates, levels,
            Substitute.For<ICreatureBaseStatRepository>(),
            Substitute.For<ICreatureRarityModifierRepository>());
        data.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        return data;
    }
}
