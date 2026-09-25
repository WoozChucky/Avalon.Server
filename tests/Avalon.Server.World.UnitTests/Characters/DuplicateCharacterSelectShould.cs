using System.Net;
using System.Net.Sockets;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Characters;
using Avalon.Hosting.Networking;
using Avalon.Common.Mathematics;
using Avalon.Domain.World;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Maps;
using Avalon.World.Persistence;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
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
    private static readonly CharacterId AnotherCharacter = new(8);
    private static readonly CharacterId ThirdCharacter = new(9);
    private static readonly AccountId TheAccount = new(42L);
    private static readonly AccountId OtherAccount = new(43L);

    private readonly List<TcpClient> _sockets = [];
    private readonly TaskCompletionSource _commit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<CharacterSaveBatch> _written = [];
    private int _committed;
    private readonly TaskCompletionSource<bool> _read = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Per-character holds on top of _commit, for a test that kicks more than one character and
    // releases their saves one at a time. Filled before the select, read by the save mock.
    private readonly Dictionary<CharacterId, TaskCompletionSource> _gates = [];
    private readonly HashSet<CharacterId> _committedIds = [];

    private readonly ICharacterSaveRepository _saves = Substitute.For<ICharacterSaveRepository>();
    private readonly ICharacterRepository _characters = Substitute.For<ICharacterRepository>();
    private readonly CharacterSaver _saver;

    public DuplicateCharacterSelectShould()
    {
        _saves.WriteAsync(Arg.Any<IReadOnlyList<CharacterSaveBatch>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                IReadOnlyList<CharacterSaveBatch> batches = call.Arg<IReadOnlyList<CharacterSaveBatch>>();
                lock (_written)
                    _written.AddRange(batches);
                await _commit.Task.WaitAsync(Limit);
                foreach (CharacterSaveBatch batch in batches)
                {
                    if (_gates.TryGetValue(batch.Row.Id, out TaskCompletionSource? gate))
                        await gate.Task.WaitAsync(Limit);
                }

                lock (_committedIds)
                {
                    foreach (CharacterSaveBatch batch in batches)
                        _committedIds.Add(batch.Row.Id);
                }

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
        foreach (TaskCompletionSource gate in _gates.Values)
            gate.TrySetResult();
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
    /// The prerequisite from #474: one session per account, not only one copy per character. With
    /// character A of the account live on the first connection, selecting character B on the second
    /// disconnects the first and despawns A, and B is read only once A's logout save has committed.
    /// Before, A was left live beside B.
    /// </summary>
    [Fact]
    public async Task Kick_a_session_of_the_account_holding_another_character_and_read_only_after_its_logout_save_commits()
    {
        (TestWorldServer server, CharacterSelectHandler select) = await BuildAsync();
        Avalon.World.WorldConnection first = Connect(server);
        Avalon.World.WorldConnection second = Connect(server);
        first.Character = New(AnotherCharacter.Value);

        select.Execute(second, new CCharacterSelectedPacket { CharacterId = TheCharacter });

        Assert.Null(first.Character);
        await first.CloseAsync().WaitAsync(Limit);
        Assert.DoesNotContain(server.Connections, c => ReferenceEquals(c, first));
        Assert.False(_read.Task.IsCompleted, "the second character was read while the account's first one was still live");

        _commit.SetResult();

        Assert.True(await _read.Task.WaitAsync(Limit), "the second character was read before the first one's logout save committed");
        CharacterSaveBatch logout = Assert.Single(_written);
        Assert.Equal(AnotherCharacter, logout.Row.Id);
        Assert.False(logout.Row.Online);

        // The kicked connection's own close then despawns nothing a second time.
        server.Tick();
        await _saver.WhenIdle(AnotherCharacter).WaitAsync(Limit);
        Assert.Single(_written);
    }

    /// <summary>
    /// Several kicked characters, several saves: the read waits for every one of them, not the
    /// first to finish. They are released one at a time so the order is observed.
    /// </summary>
    [Fact]
    public async Task Wait_for_every_kicked_characters_logout_save_before_reading()
    {
        (TestWorldServer server, CharacterSelectHandler select) = await BuildAsync();
        Avalon.World.WorldConnection first = Connect(server);
        Avalon.World.WorldConnection third = Connect(server);
        Avalon.World.WorldConnection selecting = Connect(server);
        first.Character = New(AnotherCharacter.Value);
        third.SetPendingSpawn(New(ThirdCharacter.Value), Substitute.For<IMapInstance>(), DateTime.UtcNow.Ticks);
        var thirdGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _gates[ThirdCharacter] = thirdGate;

        select.Execute(selecting, new CCharacterSelectedPacket { CharacterId = TheCharacter });

        Assert.Null(first.Character);
        Assert.Null(third.PendingSpawn);
        Assert.Null(third.Character);

        _commit.SetResult();
        await _saver.WhenIdle(AnotherCharacter).WaitAsync(Limit);
        await Task.Delay(100);
        Assert.False(_read.Task.IsCompleted, "the select read while a kicked character's logout save was still running");

        thirdGate.SetResult();

        await _read.Task.WaitAsync(Limit);
        lock (_committedIds)
        {
            Assert.Contains(AnotherCharacter, _committedIds);
            Assert.Contains(ThirdCharacter, _committedIds);
        }

        lock (_written)
            Assert.Equal(2, _written.Count);
    }

    /// <summary>
    /// A connection of the account part way through its own select has no entity yet. It is kicked
    /// like any other: its select is cancelled and it is disconnected, and the new select goes
    /// ahead. That the kicked chain's remaining steps then do nothing, and that the new select waits
    /// for the step it had in flight, is driven through the real chain in CharacterSelectChainShould.
    /// </summary>
    [Fact]
    public async Task Kick_a_session_of_the_account_that_is_still_selecting()
    {
        (TestWorldServer server, CharacterSelectHandler select) = await BuildAsync();
        Avalon.World.WorldConnection first = Connect(server);
        Avalon.World.WorldConnection second = Connect(server);
        first.BeginSelect(DateTime.UtcNow.Ticks);

        select.Execute(second, new CCharacterSelectedPacket { CharacterId = TheCharacter });

        Assert.False(first.SelectInProgress);
        await DisconnectedAsync(first);
        Assert.True(second.SelectInProgress);
        await _read.Task.WaitAsync(Limit);
    }

    /// <summary>Every other session of the account ends, including one still at the character list.</summary>
    [Fact]
    public async Task Kick_a_session_of_the_account_that_has_not_selected_anything()
    {
        (TestWorldServer server, CharacterSelectHandler select) = await BuildAsync();
        Avalon.World.WorldConnection first = Connect(server);
        Avalon.World.WorldConnection second = Connect(server);

        select.Execute(second, new CCharacterSelectedPacket { CharacterId = TheCharacter });

        await DisconnectedAsync(first);
        await _read.Task.WaitAsync(Limit);
        Assert.Empty(_written);
    }

    /// <summary>The rule is per account: another account's session is left alone.</summary>
    [Fact]
    public async Task Leave_a_session_of_another_account_alone()
    {
        (TestWorldServer server, CharacterSelectHandler select) = await BuildAsync();
        Avalon.World.WorldConnection other = Connect(server, OtherAccount);
        Avalon.World.WorldConnection selecting = Connect(server);
        CharacterEntity theirs = New(AnotherCharacter.Value);
        other.Character = theirs;

        select.Execute(selecting, new CCharacterSelectedPacket { CharacterId = TheCharacter });

        // Read at once: there is no save of this account's to wait for.
        await _read.Task.WaitAsync(Limit);
        Assert.Same(theirs, other.Character);
        Assert.True(other.IsConnected);
        Assert.Contains(server.Connections, c => ReferenceEquals(c, other));
        Assert.Empty(_written);
    }

    /// <summary>
    /// A despawn step that throws must still release the character. Before, the connection kept it
    /// whenever an instance step threw: no save was queued, the new select read at once, and the old
    /// entity stayed in its instance, still ticked and still saved periodically, next to the new
    /// session's copy. The kicked connection's close then despawned it a second time and wrote
    /// Online = false behind the new session.
    /// </summary>
    [Fact]
    public async Task Release_the_kicked_character_and_still_queue_its_logout_save_when_leaving_its_instance_throws()
    {
        var scheduler = Substitute.For<ICharacterSaveScheduler>();
        MapInstance town = Town(scheduler);
        (TestWorldServer server, CharacterSelectHandler select) = await BuildAsync(town: town);
        Avalon.World.WorldConnection first = Connect(server);
        Avalon.World.WorldConnection second = Connect(server);

        CharacterEntity live = New(TheCharacter.Value);
        live.Spells.Load(Array.Empty<IAbility>());   // the instance tick updates abilities
        live.InstanceId = town.InstanceId;
        first.Character = live;
        town.AddCharacter(first);

        // A disconnect hook that throws for this character, the way a creature script's could.
        void Throw(ICharacter character)
        {
            if (ReferenceEquals(character, live))
                throw new InvalidOperationException("simulated disconnect hook failure");
        }

        CharacterEntity.CharacterDisconnected += Throw;
        try
        {
            select.Execute(second, new CCharacterSelectedPacket { CharacterId = TheCharacter });
        }
        finally
        {
            CharacterEntity.CharacterDisconnected -= Throw;
        }

        Assert.Null(first.Character);
        Assert.DoesNotContain(live.Guid, town.Characters.Keys);
        town.Update(TimeSpan.FromSeconds(1d / 60d));
        scheduler.DidNotReceiveWithAnyArgs().Tick(default!, default!, default);

        // The logout save was still queued, so the new session waits for it.
        Assert.False(_read.Task.IsCompleted, "the second session read the character while the first still held it");
        _commit.SetResult();
        Assert.True(await _read.Task.WaitAsync(Limit), "the second session read before the first one's logout save committed");
        CharacterSaveBatch logout = Assert.Single(_written);
        Assert.False(logout.Row.Online);

        await first.CloseAsync().WaitAsync(Limit);
        server.Tick();
        await _saver.WhenIdle(TheCharacter).WaitAsync(Limit);
        Assert.Single(_written);
    }

    /// <summary>
    /// The same, for the save step itself. Nothing can be written when taking the snapshot throws,
    /// but the connection must still end holding nothing, or its close despawns the discarded entity
    /// again behind the new session.
    /// </summary>
    [Fact]
    public async Task Release_the_kicked_character_when_queuing_its_logout_save_throws()
    {
        var despawnSaver = Substitute.For<ICharacterSaver>();
        despawnSaver.SaveOnDespawnAsync(Arg.Any<CharacterEntity>(), Arg.Any<Func<Character, CancellationToken, Task>?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new InvalidOperationException("simulated snapshot failure"));
        (TestWorldServer server, CharacterSelectHandler select) = await BuildAsync(despawnSaver);
        Avalon.World.WorldConnection first = Connect(server);
        Avalon.World.WorldConnection second = Connect(server);
        first.Character = New(TheCharacter.Value);

        select.Execute(second, new CCharacterSelectedPacket { CharacterId = TheCharacter });

        Assert.Null(first.Character);

        await first.CloseAsync().WaitAsync(Limit);
        server.Tick();
        despawnSaver.ReceivedWithAnyArgs(1).SaveOnDespawnAsync(default!, default, default);
    }

    /// <summary>Waits, bounded, for the kick's close to drop the socket. The test never closes it itself.</summary>
    private static async Task DisconnectedAsync(Avalon.World.WorldConnection connection)
    {
        DateTime deadline = DateTime.UtcNow + Limit;
        while (connection.IsConnected)
        {
            Assert.True(DateTime.UtcNow < deadline, "the other session of the account was not disconnected");
            await Task.Delay(10);
        }
    }

    private Avalon.World.WorldConnection Connect(TestWorldServer server, AccountId? account = null)
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
            AccountId = account ?? TheAccount
        };
        server.Add(connection);
        return connection;
    }

    private async Task<(TestWorldServer Server, CharacterSelectHandler Select)> BuildAsync(
        ICharacterSaver? despawnSaver = null, MapInstance? town = null)
    {
        Avalon.World.World world = await LoadedWorldAsync(despawnSaver ?? _saver, town);
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
    private static async Task<Avalon.World.World> LoadedWorldAsync(ICharacterSaver saver, MapInstance? town)
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

        // The town, when a test has one, is what the registry builds for map template 1.
        var mapManager = Substitute.For<IAvalonMapManager>();
        var layouts = Substitute.For<IChunkLayoutInstanceFactory>();
        if (town is not null)
        {
            mapManager.Templates.Returns(new List<MapTemplate>
            {
                new() { Id = new MapTemplateId(1), MapType = MapType.Town, Name = "town", Description = "" }
            });
            layouts.BuildAsync(Arg.Any<MapTemplate>(), Arg.Any<uint?>(), Arg.Any<CancellationToken>()).Returns(town);
        }

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IChunkLayoutInstanceFactory)).Returns(layouts);

        var world = new Avalon.World.World(
            NullLoggerFactory.Instance,
            Options.Create(new GameConfiguration { WorldId = new Avalon.Domain.Auth.WorldId(1), CharacterLoadTimeoutSeconds = 15 }),
            serviceProvider,
            worldRepository,
            mapManager,
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
        if (town is not null)
            await world.InstanceRegistry.GetOrCreateTownInstanceAsync(new MapTemplateId(1), 30).WaitAsync(Limit);
        return world;
    }

    /// <summary>A real instance, so what leaving it removes, and what its tick still reaches, is observed.</summary>
    private static MapInstance Town(ICharacterSaveScheduler scheduler)
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IScriptManager)).Returns(Substitute.For<IScriptManager>());
        serviceProvider.GetService(typeof(CombatConfig)).Returns(new CombatConfig());
        serviceProvider.GetService(typeof(ICharacterSaveScheduler)).Returns(scheduler);

        var world = Substitute.For<IWorld>();
        world.Configuration.Returns(new GameConfiguration());

        var entryChunk = new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero);
        var layout = new ChunkLayout(Seed: 0, Chunks: [entryChunk], EntryChunk: entryChunk, BossChunk: null,
            Portals: [], EntrySpawnWorldPos: Vector3.zero, CellSize: 30f, Config: null);

        var town = new MapInstance(NullLoggerFactory.Instance, serviceProvider, world, new MapTemplateId(1),
            ownerCharacterId: null, layout, Substitute.For<IMapNavigator>(), seed: 0);
        town.Dispose();   // detach the static entity events; the test raises only its own
        return town;
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
