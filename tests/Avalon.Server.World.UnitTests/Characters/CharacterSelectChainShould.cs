using System.Net;
using System.Net.Sockets;
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
using Avalon.World.Configuration;
using Avalon.World.Handlers;
using Avalon.World.Public;
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

    [Fact]
    public void Say_a_select_is_under_way_for_every_step_before_the_pending_spawn_exists()
    {
        StartSelect();

        // Five steps: find character, resolve instance, persist row, load inventory, load abilities.
        for (int step = 0; step < 4; step++)
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
        Step(5);

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

        var inventory = Substitute.For<ICharacterInventoryRepository>();
        inventory.GetByCharacterIdAsync(TheCharacter, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CharacterInventory>());

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
            inventory,
            abilities,
            Substitute.For<IChunkLibrary>(),
            world,
            Substitute.For<IRespawnTargetResolver>(),
            Options.Create(new RegenConfiguration()));
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

        var data = new StaticData(createInfos, stats, items, abilityTemplates, levels);
        data.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        return data;
    }
}
