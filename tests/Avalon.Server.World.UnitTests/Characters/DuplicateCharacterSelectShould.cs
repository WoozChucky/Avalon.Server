using System.Net;
using System.Net.Sockets;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Characters;
using Avalon.Hosting.Networking;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Maps;
using Avalon.World.Persistence;
using Avalon.World.Public.Instances;
using Avalon.World.Respawn;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Characters;

/// <summary>
/// One character, one live copy. Two copies of a character each hold their own inventory and money
/// in memory, and whichever saves last writes its copy over the other: an item sold by one comes
/// back through the other, and once items can change hands the same thing duplicates them. The
/// auth server's online flag cannot prevent it, because an auth connection that drops clears the
/// flag while the world session stays up, so the world server, the one place that knows which
/// connection holds which character, has to.
/// </summary>
/// <remarks>
/// Everything here is real except the database: the world server and its connection list, two
/// connections, the world's despawn, the save chain and the select handler. The despawn save is
/// held open at the repository, so the order of "old session committed" and "new session read" is
/// observed, not assumed.
/// </remarks>
public class DuplicateCharacterSelectShould : IDisposable
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);
    private static readonly CharacterId TheCharacter = new(7);
    private static readonly AccountId TheAccount = new(42L);

    private readonly List<TcpClient> _sockets = [];
    private readonly TaskCompletionSource _commit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<CharacterSaveBatch> _written = [];
    private int _committed;
    private readonly TaskCompletionSource<bool> _read = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly ICharacterSaveRepository _saves = Substitute.For<ICharacterSaveRepository>();
    private readonly ICharacterRepository _characters = Substitute.For<ICharacterRepository>();
    private readonly CharacterSaver _saver;

    public DuplicateCharacterSelectShould()
    {
        _saves.WriteAsync(Arg.Any<IReadOnlyList<CharacterSaveBatch>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                lock (_written)
                    _written.AddRange(call.Arg<IReadOnlyList<CharacterSaveBatch>>());
                await _commit.Task.WaitAsync(Limit);
                Volatile.Write(ref _committed, 1);
            });
        _saver = new CharacterSaver(_saves, NullLogger<CharacterSaver>.Instance);

        // The read records whether the old session's save had committed when it ran, then finds
        // nothing: what happens after the read is the rest of the select chain, covered elsewhere.
        _characters.FindByIdAndAccountAsync(TheCharacter, TheAccount, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _read.TrySetResult(Volatile.Read(ref _committed) == 1);
                return Task.FromResult<Character?>(null);
            });
    }

    public void Dispose()
    {
        _commit.TrySetResult();
        foreach (TcpClient socket in _sockets)
            socket.Dispose();
    }

    /// <summary>
    /// The prerequisite from the issue. With the character live on the first connection, selecting
    /// it on the second must not leave two live copies: the first connection is disconnected and
    /// despawned, and the second reads the database only once that despawn's save has committed.
    /// </summary>
    [Fact]
    public async Task Kick_the_session_holding_the_character_and_read_only_after_its_logout_save_commits()
    {
        (TestWorldServer server, CharacterSelectHandler select) = await BuildAsync();
        Avalon.World.WorldConnection first = Connect(server);
        Avalon.World.WorldConnection second = Connect(server);
        CharacterEntity live = New(TheCharacter.Value);
        first.Character = live;

        select.Execute(second, new CCharacterSelectedPacket { CharacterId = TheCharacter });

        Assert.Null(first.Character);
        await first.CloseAsync().WaitAsync(Limit);
        Assert.DoesNotContain(server.Connections, c => ReferenceEquals(c, first));
        Assert.False(_read.Task.IsCompleted, "the second session read the character while the first still held it");

        _commit.SetResult();

        Assert.True(await _read.Task.WaitAsync(Limit), "the second session read before the first one's logout save committed");
        CharacterSaveBatch logout = Assert.Single(_written);
        Assert.Equal(TheCharacter, logout.Row.Id);
        Assert.False(logout.Row.Online);

        // The tick then dequeues the kicked connection's own close. The character has already left
        // with the kick, so there is nothing for that despawn to save a second time.
        server.Tick();
        await _saver.WhenIdle(TheCharacter).WaitAsync(Limit);
        Assert.Single(_written);
    }

    /// <summary>
    /// A character built by a select and waiting on its client to load is as live as a spawned one:
    /// it is marked online and saves at despawn. Kicking only spawned characters would leave it.
    /// </summary>
    [Fact]
    public async Task Kick_a_session_whose_copy_is_still_waiting_on_its_client()
    {
        (TestWorldServer server, CharacterSelectHandler select) = await BuildAsync();
        Avalon.World.WorldConnection first = Connect(server);
        Avalon.World.WorldConnection second = Connect(server);
        first.SetPendingSpawn(New(TheCharacter.Value), Substitute.For<IMapInstance>(), DateTime.UtcNow.Ticks);

        select.Execute(second, new CCharacterSelectedPacket { CharacterId = TheCharacter });

        Assert.Null(first.PendingSpawn);
        Assert.Null(first.Character);
        Assert.False(_read.Task.IsCompleted, "the second session read the character while the first still held it");

        _commit.SetResult();

        Assert.True(await _read.Task.WaitAsync(Limit));
        Assert.Equal(TheCharacter, Assert.Single(_written).Row.Id);
    }

    /// <summary>
    /// The race. A connection that closes by itself leaves the connection list at once, but its
    /// despawn, and so its logout save, is only started by the next tick. A select landing in
    /// between finds no save queued to wait for, and without a check of its own it reads the
    /// character as it was before that save.
    /// </summary>
    [Fact]
    public async Task Not_read_before_a_closed_sessions_logout_save_has_been_queued_and_committed()
    {
        (TestWorldServer server, CharacterSelectHandler select) = await BuildAsync();
        Avalon.World.WorldConnection first = Connect(server);
        Avalon.World.WorldConnection second = Connect(server);
        first.Character = New(TheCharacter.Value);

        await first.CloseAsync().WaitAsync(Limit);
        Assert.DoesNotContain(server.Connections, c => ReferenceEquals(c, first));
        Assert.True(_saver.WhenIdle(TheCharacter).IsCompleted, "no tick has run, so no despawn save should be queued yet");

        select.Execute(second, new CCharacterSelectedPacket { CharacterId = TheCharacter });

        Assert.False(_read.Task.IsCompleted, "the select read the character before the closed session's logout save was queued");

        _commit.SetResult();

        Assert.True(await _read.Task.WaitAsync(Limit), "the select read before the closed session's logout save committed");
        Assert.Equal(TheCharacter, Assert.Single(_written).Row.Id);

        server.Tick();
        await _saver.WhenIdle(TheCharacter).WaitAsync(Limit);
        Assert.Single(_written);
    }

    /// <summary>
    /// A connection of the same account part way through its own select has no entity yet, and
    /// nothing says which character it is reading: characters belong to accounts, so it may be this
    /// one. Kicking it would not stop the reads it already has in flight, so the second select is
    /// refused instead, reading nothing; the client can select again once the first one settles.
    /// </summary>
    [Fact]
    public async Task Refuse_the_select_while_another_session_of_the_account_is_still_selecting()
    {
        (TestWorldServer server, CharacterSelectHandler select) = await BuildAsync();
        Avalon.World.WorldConnection first = Connect(server);
        Avalon.World.WorldConnection second = Connect(server);
        first.BeginSelect(DateTime.UtcNow.Ticks);

        select.Execute(second, new CCharacterSelectedPacket { CharacterId = TheCharacter });

        Assert.False(second.SelectInProgress);
        Assert.True(first.SelectInProgress);
        Assert.True(first.IsConnected);
        await _characters.DidNotReceiveWithAnyArgs().FindByIdAndAccountAsync(default!, default!, default);
    }

    /// <summary>The decision is about one character, not one account: another character of the account is left alone.</summary>
    [Fact]
    public async Task Leave_a_session_holding_another_character_of_the_account_alone()
    {
        (TestWorldServer server, CharacterSelectHandler select) = await BuildAsync();
        Avalon.World.WorldConnection first = Connect(server);
        Avalon.World.WorldConnection second = Connect(server);
        CharacterEntity other = New(8);
        first.Character = other;

        select.Execute(second, new CCharacterSelectedPacket { CharacterId = TheCharacter });

        // Read at once: there is no save of this character to wait for.
        await _read.Task.WaitAsync(Limit);
        Assert.Same(other, first.Character);
        Assert.True(first.IsConnected);
        Assert.Empty(_written);
    }

    private Avalon.World.WorldConnection Connect(TestWorldServer server)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var clientSide = new TcpClient();
        clientSide.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint!).Port);
        TcpClient serverSide = listener.AcceptTcpClient();
        listener.Stop();
        _sockets.Add(clientSide);
        _sockets.Add(serverSide);

        var connection = new Avalon.World.WorldConnection(
            server, clientSide, NullLoggerFactory.Instance, Substitute.For<IPacketReader>())
        {
            AccountId = TheAccount
        };
        server.Add(connection);
        return connection;
    }

    private async Task<(TestWorldServer Server, CharacterSelectHandler Select)> BuildAsync()
    {
        Avalon.World.World world = await LoadedWorldAsync(_saver);
        var server = new TestWorldServer(world, _saver);

        var select = new CharacterSelectHandler(
            NullLogger<CharacterSelectHandler>.Instance,
            NullLoggerFactory.Instance,
            _characters,
            Substitute.For<ICharacterInventoryRepository>(),
            Substitute.For<IItemInstanceRepository>(),
            Substitute.For<ICharacterAbilityRepository>(),
            Substitute.For<IChunkLibrary>(),
            world,
            Substitute.For<IRespawnTargetResolver>(),
            Options.Create(new RegenConfiguration()),
            Substitute.For<IAccountRepository>(),
            _saver,
            server);

        return (server, select);
    }

    /// <summary>The real world, for its real despawn. It reads the instance registry, which only exists after LoadAsync.</summary>
    private static async Task<Avalon.World.World> LoadedWorldAsync(ICharacterSaver saver)
    {
        var scopedProvider = Substitute.For<IServiceProvider>();
        scopedProvider.GetService(typeof(ICharacterSaver)).Returns(saver);
        scopedProvider.GetService(typeof(IRespawnTargetResolver)).Returns(Substitute.For<IRespawnTargetResolver>());
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(scopedProvider);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var worldRepository = Substitute.For<IWorldRepository>();
        worldRepository.FindByIdAsync(Arg.Any<Avalon.Domain.Auth.WorldId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new Avalon.Domain.Auth.World
            {
                Name = "test", Host = "127.0.0.1", Port = 0, MinVersion = "0.0.1", Version = "1.0.0"
            });

        var levels = Substitute.For<ICharacterLevelExperienceRepository>();
        levels.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<Avalon.Domain.World.CharacterLevelExperience>());
        var stats = Substitute.For<IClassLevelStatRepository>();
        stats.FindAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<Avalon.Domain.World.ClassLevelStat>());
        var createInfos = Substitute.For<ICharacterCreateInfoRepository>();
        createInfos.FindAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Avalon.Domain.World.CharacterCreateInfo>());
        var items = Substitute.For<IItemTemplateRepository>();
        items.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<Avalon.Domain.World.ItemTemplate>());
        var abilityTemplates = Substitute.For<IAbilityTemplateRepository>();
        abilityTemplates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<Avalon.Domain.World.AbilityTemplate>());
        var localizedText = Substitute.For<ILocalizedTextRepository>();
        localizedText.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Avalon.Domain.World.LocalizedText>>([]));
        localizedText.GetAllLocalesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Avalon.Domain.World.LocalizedTextLocale>>([]));
        localizedText.GetAllClassNamesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Avalon.Domain.World.CharacterClassName>>([]));
        var dialogue = Substitute.For<IDialogueRepository>();
        dialogue.GetAllNodesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Avalon.Domain.World.DialogueNode>>([]));
        dialogue.GetAllOptionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Avalon.Domain.World.DialogueOption>>([]));
        var creatureTemplates = Substitute.For<ICreatureTemplateRepository>();
        creatureTemplates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<Avalon.Domain.World.CreatureTemplate>()));
        var baseStats = Substitute.For<ICreatureBaseStatRepository>();
        baseStats.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Avalon.Domain.World.CreatureBaseStat>>(
                [new Avalon.Domain.World.CreatureBaseStat { Level = 1, Health = 1, DamageMin = 1, DamageMax = 1, Experience = 1 }]));
        var rarities = Substitute.For<ICreatureRarityModifierRepository>();
        rarities.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Avalon.Domain.World.CreatureRarityModifier>>([]));

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IChunkLayoutInstanceFactory))
            .Returns(Substitute.For<IChunkLayoutInstanceFactory>());

        var world = new Avalon.World.World(
            NullLoggerFactory.Instance,
            Options.Create(new GameConfiguration { WorldId = new Avalon.Domain.Auth.WorldId(1), CharacterLoadTimeoutSeconds = 15 }),
            serviceProvider,
            worldRepository,
            Substitute.For<IAvalonMapManager>(),
            scopeFactory,
            createInfos,
            stats,
            items,
            abilityTemplates,
            levels,
            creatureTemplates,
            baseStats,
            rarities,
            localizedText,
            Substitute.For<IScriptHotReloader>(),
            Substitute.For<IChunkLibrary>(),
            dialogue);

        await world.LoadAsync(CancellationToken.None);
        return world;
    }

    /// <summary>Reaches one tick, and the connection list, without the socket loop that normally drives them.</summary>
    private sealed class TestWorldServer : WorldServer
    {
        public TestWorldServer(IWorld world, ICharacterSaver saver) : base(
            Substitute.For<IPacketManager>(),
            NullLoggerFactory.Instance,
            new AnyServiceProvider(),
            Options.Create(new HostingConfiguration { Host = "127.0.0.1", Port = 0 }),
            world,
            Substitute.For<IScriptManager>(),
            Substitute.For<IReplicatedCache>(),
            Substitute.For<IScriptHotReloader>(),
            saver)
        { }

        public void Add(Avalon.World.WorldConnection connection) => AddConnection(connection);

        public void Tick() => Update(TimeSpan.FromMilliseconds(16), 0);
    }

    /// <summary>
    /// The world server reflects over every packet handler in the assembly and activates each one,
    /// so standing it up needs a container that answers for all of their dependencies.
    /// </summary>
    private sealed class AnyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(ILoggerFactory)) return NullLoggerFactory.Instance;

            if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(ILogger<>))
                return Activator.CreateInstance(
                    typeof(NullLogger<>).MakeGenericType(serviceType.GenericTypeArguments[0]));

            if (serviceType.IsInterface || serviceType.IsAbstract)
                return Substitute.For([serviceType], []);

            return serviceType.GetConstructor(Type.EmptyTypes) is not null
                ? Activator.CreateInstance(serviceType)
                : null;
        }
    }
}
