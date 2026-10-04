using Avalon.Common.GameAuth;
using Avalon.Server.World.UnitTests.GameAuth;
using System.Net;
using System.Net.Sockets;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Combat;
using Avalon.Common.Cryptography;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Configuration;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Hosting.Networking;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Generic;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Maps;
using Avalon.World.Parties;
using Avalon.World.Quests;
using Avalon.World.Persistence;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Maps;
using Avalon.World.Pvp;
using Avalon.World.Respawn;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ProtoBuf;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Characters;

/// <summary>
/// Change Character (#663) end to end, with everything real but the database: the world and its
/// despawn, a town instance, the save chain, the world server's connection list and tick, and the
/// leave, list and select handlers on one real connection. The logout save is held open at the
/// repository, so "saved before answered" is observed rather than assumed.
/// </summary>
public class CharacterLeaveShould : IDisposable
{
    /// <summary>
    /// How long a test waits for work that finishes on the thread pool (the logout save, the leave's
    /// answer) before it fails. It decides no outcome: the select's save wait runs on a clock that
    /// never moves here, so a slow runner only makes a test slower, and this bound only stops a
    /// broken one from hanging. Generous, because a loaded runner can hold pool work back for seconds.
    /// </summary>
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);
    private static readonly CharacterId TheCharacter = new(7);
    private static readonly CharacterId AnotherCharacter = new(8);
    private static readonly AccountId TheAccount = new(42L);

    private readonly List<TcpClient> _sockets = [];
    private readonly TaskCompletionSource _commit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<CharacterSaveBatch> _written = [];
    private readonly ICharacterSaveRepository _saves = Substitute.For<ICharacterSaveRepository>();
    private readonly ICharacterRepository _characters = Substitute.For<ICharacterRepository>();
    private readonly CharacterSaver _saver;

    // Which character each select read, and whether the leave's logout save had committed by then.
    private readonly List<(CharacterId Id, bool AfterCommit)> _selectReads = [];
    private int _committed;

    // Set by a test whose logout save must fail. A flag read by the one configured write, rather than
    // a second Returns configured over it: a write that fell through to the held write above waited
    // out the whole Limit on a commit nothing sends, and failed the test on the same deadline.
    private int _failWrites;

    public CharacterLeaveShould()
    {
        _saves.WriteAsync(Arg.Any<IReadOnlyList<CharacterSaveBatch>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                if (Volatile.Read(ref _failWrites) == 1)
                    throw new InvalidOperationException("simulated write failure");

                lock (_written)
                    _written.AddRange(call.Arg<IReadOnlyList<CharacterSaveBatch>>());
                await _commit.Task.WaitAsync(Limit);
                Volatile.Write(ref _committed, 1);
            });
        _saver = new CharacterSaver(_saves, NullLogger<CharacterSaver>.Instance);

        _characters.FindByAccountAsync(TheAccount, Arg.Any<CancellationToken>())
            .Returns(new List<Character>
            {
                new() { Id = TheCharacter, AccountId = TheAccount, Name = "Tester7", Level = 3 },
                new() { Id = AnotherCharacter, AccountId = TheAccount, Name = "Tester8", Level = 1 },
            });
        // What happens after a select's read is the rest of the select chain, covered elsewhere.
        _characters.FindForGameplayAsync(Arg.Is<GameplayWriteAuthority>(a => a.AccountId == TheAccount), Arg.Any<CharacterId>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                lock (_selectReads)
                    _selectReads.Add((call.Arg<CharacterId>(), Volatile.Read(ref _committed) == 1));
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
    /// Acceptance cases 1 and 2: from a live map, the character leaves its instance, its logout save
    /// writes it offline, Left is answered only once that save has committed, and the same connection
    /// then lists the account's characters and selects a different one, with no new login.
    /// </summary>
    [Fact]
    public async Task Leave_answer_Left_after_the_logout_save_then_list_and_select_on_the_same_connection()
    {
        (MapInstance town, TestWorldServer server, Handlers h) = await BuildAsync();
        RecordingConnection connection = Connect(server);
        CharacterEntity live = Spawn(connection, town);

        h.Leave.Execute(connection, new CCharacterLeavePacket());

        Assert.Null(connection.Character);
        Assert.True(connection.LeaveInProgress);
        Assert.DoesNotContain(live.Guid, town.Characters.Keys);
        CharacterSaveBatch logout = await WrittenAsync();
        Assert.Equal(TheCharacter, logout.Row.Id);
        Assert.False(logout.Row.Online);

        connection.FlushContinuations();
        Assert.Empty(connection.Results());

        _commit.SetResult();
        await UntilAsync(connection, () => !connection.LeaveInProgress);

        Assert.Equal([CharacterLeaveResult.Left], connection.Results());
        Assert.False(connection.IsClosing);

        h.List.Execute(connection, new CCharacterListPacket());
        await UntilAsync(connection, () => connection.Read<SCharacterListPacket>(NetworkPacketType.SMSG_CHARACTER_LIST).Count > 0);
        SCharacterListPacket list = Assert.Single(connection.Read<SCharacterListPacket>(NetworkPacketType.SMSG_CHARACTER_LIST));
        Assert.Equal([TheCharacter.Value, AnotherCharacter.Value], list.Characters.Select(c => c.CharacterId));

        h.Select.Execute(connection, new CCharacterSelectedPacket { CharacterId = AnotherCharacter });
        Assert.True(connection.SelectInProgress);
        await UntilAsync(connection, () => SelectReads().Count > 0);
        Assert.Equal(AnotherCharacter, Assert.Single(SelectReads()).Id);
        Assert.False(connection.IsClosing);
        Assert.Single(_written);
    }

    /// <summary>
    /// Acceptance case 2, the race: a select of the same character from another session of the
    /// account, landing while the leave's logout save is still writing, reads only after it commits.
    /// It kicks the leaving session, which is then never answered.
    /// </summary>
    [Fact]
    public async Task Hold_a_reselect_of_the_same_character_until_the_logout_save_commits()
    {
        (MapInstance town, TestWorldServer server, Handlers h) = await BuildAsync();
        RecordingConnection leaving = Connect(server);
        RecordingConnection other = Connect(server);
        Spawn(leaving, town);

        h.Leave.Execute(leaving, new CCharacterLeavePacket());
        await WrittenAsync();
        h.Select.Execute(other, new CCharacterSelectedPacket { CharacterId = TheCharacter });

        other.FlushContinuations();
        Assert.Empty(SelectReads());
        Assert.True(leaving.IsClosing);

        _commit.SetResult();
        await UntilAsync(other, () => SelectReads().Count > 0);

        Assert.Equal((TheCharacter, true), Assert.Single(SelectReads()));
        leaving.FlushContinuations();
        Assert.Empty(leaving.Results());
        Assert.Single(_written);
    }

    /// <summary>
    /// Acceptance case 3: while a leave is under way the connection is not yet back at character
    /// selection, and a list sent before the answer is refused as one sent mid-select is.
    /// </summary>
    [Fact]
    public async Task Refuse_a_character_list_sent_before_the_leave_is_answered()
    {
        (MapInstance town, TestWorldServer server, Handlers h) = await BuildAsync();
        RecordingConnection connection = Connect(server);
        Spawn(connection, town);

        h.Leave.Execute(connection, new CCharacterLeavePacket());
        h.List.Execute(connection, new CCharacterListPacket());

        Assert.True(connection.IsClosing);
        await _characters.DidNotReceiveWithAnyArgs().FindByAccountAsync(default!, default);
    }

    /// <summary>Acceptance case 3: a second leave is answered AlreadyLeaving, and the logout save runs once.</summary>
    [Fact]
    public async Task Answer_a_second_leave_AlreadyLeaving_and_save_once()
    {
        (MapInstance town, TestWorldServer server, Handlers h) = await BuildAsync();
        RecordingConnection connection = Connect(server);
        Spawn(connection, town);

        h.Leave.Execute(connection, new CCharacterLeavePacket());
        h.Leave.Execute(connection, new CCharacterLeavePacket());
        _commit.SetResult();
        await UntilAsync(connection, () => !connection.LeaveInProgress);

        Assert.Equal([CharacterLeaveResult.AlreadyLeaving, CharacterLeaveResult.Left], connection.Results());
        await _saver.WhenIdle(TheCharacter).WaitAsync(Limit);
        Assert.Single(_written);
    }

    /// <summary>
    /// Acceptance case 4: a disconnect while leaving is never answered, and the close that follows
    /// finds nothing left to save a second time.
    /// </summary>
    [Fact]
    public async Task Not_answer_or_save_twice_when_the_connection_drops_while_leaving()
    {
        (MapInstance town, TestWorldServer server, Handlers h) = await BuildAsync();
        RecordingConnection connection = Connect(server);
        Spawn(connection, town);

        h.Leave.Execute(connection, new CCharacterLeavePacket());
        await connection.CloseAsync().WaitAsync(Limit);
        server.Tick();

        _commit.SetResult();
        await _saver.WhenIdle(TheCharacter).WaitAsync(Limit);
        connection.FlushContinuations();

        Assert.Single(_written);
        Assert.Empty(connection.Results());
    }

    /// <summary>
    /// Acceptance case 4: a logout save that fails is never answered as a success. The connection is
    /// closed with a reason instead of being handed back to character selection half reset.
    /// </summary>
    [Fact]
    public async Task Close_with_CharacterSaveFailed_when_the_logout_save_fails()
    {
        Volatile.Write(ref _failWrites, 1);
        (MapInstance town, TestWorldServer server, Handlers h) = await BuildAsync();
        RecordingConnection connection = Connect(server);
        Spawn(connection, town);

        h.Leave.Execute(connection, new CCharacterLeavePacket());
        await UntilAsync(connection, () => connection.IsClosing);

        Assert.Empty(connection.Results());
        Assert.Equal(DisconnectReason.CharacterSaveFailed,
            Assert.Single(connection.Read<SDisconnectPacket>(NetworkPacketType.SMSG_DISCONNECT)).ReasonCode);
        Assert.Null(connection.Character);
    }

    private sealed record Handlers(CharacterLeaveHandler Leave, CharacterListHandler List, CharacterSelectHandler Select);

    private List<(CharacterId Id, bool AfterCommit)> SelectReads()
    {
        lock (_selectReads)
            return [.. _selectReads];
    }

    private async Task<CharacterSaveBatch> WrittenAsync()
    {
        DateTime deadline = DateTime.UtcNow + Limit;
        while (true)
        {
            lock (_written)
            {
                if (_written.Count > 0)
                    return _written[0];
            }

            Assert.True(DateTime.UtcNow < deadline, "no logout save reached the repository");
            await Task.Delay(5);
        }
    }

    /// <summary>Runs the connection's continuations, as the tick does, until <paramref name="done" /> holds.</summary>
    private static async Task UntilAsync(RecordingConnection connection, Func<bool> done)
    {
        DateTime deadline = DateTime.UtcNow + Limit;
        while (true)
        {
            connection.FlushContinuations();
            if (done())
                return;
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting on the connection");
            await Task.Delay(5);
        }
    }

    private static CharacterEntity Spawn(RecordingConnection connection, MapInstance town)
    {
        CharacterEntity live = New(TheCharacter.Value);
        live.Spells.Load(Array.Empty<IAbility>());
        live.InstanceId = town.InstanceId;
        connection.Character = live;
        town.AddCharacter(connection);
        return live;
    }

    private async Task<(MapInstance Town, TestWorldServer Server, Handlers Handlers)> BuildAsync()
    {
        MapInstance town = Town(Substitute.For<ICharacterSaveScheduler>());
        Avalon.World.World world = await LoadedWorldAsync(_saver, town);
        var server = new TestWorldServer(world, _saver);

        var leave = new CharacterLeaveHandler(NullLogger<CharacterLeaveHandler>.Instance, world);
        var list = new CharacterListHandler(NullLogger<CharacterListHandler>.Instance, _characters, world);
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
            server,
            // Never advanced: the select's save wait ends only when the leave's save does, so a slow
            // runner cannot time it out while a test is still holding the save.
            new ManualTimerClock(),
            databaseWork: InlineDatabaseWork.Instance);

        return (town, server, new Handlers(leave, list, select));
    }

    private RecordingConnection Connect(TestWorldServer server)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var clientSide = new TcpClient();
        clientSide.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint!).Port);
        TcpClient serverSide = listener.AcceptTcpClient();
        listener.Stop();
        _sockets.Add(clientSide);
        _sockets.Add(serverSide);

        var connection = new RecordingConnection(server, clientSide) { AccountId = TheAccount };
        GameplayTestAdmission.Admit(connection);
        server.Add(connection);
        return connection;
    }

    /// <summary>
    /// A real connection whose crypto session is paired with a client's, so what it sends can be
    /// read back. Sends are recorded instead of written to the socket.
    /// </summary>
    private sealed class RecordingConnection : Avalon.World.WorldConnection
    {
        private readonly AvalonCryptoSession _client;
        private readonly List<NetworkPacket> _sent = [];

        public RecordingConnection(IWorldServer server, TcpClient client)
            : base(server, client, NullLoggerFactory.Instance, Substitute.For<IPacketReader>())
        {
            // A session reports its public key only once initialized, so each end's is taken from its pair.
            var clientKeys = AsymmetricCipher.GenerateECDHKeyPair();
            _client = new AvalonCryptoSession(CryptoRole.Client, clientKeys);
            CryptoSession.Initialize(PublicKey(clientKeys));
            _client.Initialize(PublicKey(ServerCrypto.GetKeyPair()));
        }

        private static byte[] PublicKey(Org.BouncyCastle.Crypto.AsymmetricCipherKeyPair keys) =>
            AsymmetricCipher.GetPublicKeyBytes(AsymmetricCipher.GetPublicKeyFromKeyPair(keys));

        public override void Send(NetworkPacket packet)
        {
            lock (_sent)
                _sent.Add(packet);
        }

        public List<T> Read<T>(NetworkPacketType type)
        {
            List<NetworkPacket> sent;
            lock (_sent)
                sent = _sent.Where(p => p.Header.Type == type).ToList();

            return sent.Select(p =>
            {
                byte[] payload = p.Payload;
                if (p.Header.Flags.HasFlag(NetworkPacketFlags.Encrypted))
                {
                    var plain = new byte[p.Payload.Length];
                    int length = _client.Decrypt(p.Payload, plain);
                    payload = plain[..length];
                }

                using var stream = new MemoryStream(payload);
                return Serializer.Deserialize<T>(stream);
            }).ToList();
        }

        public List<CharacterLeaveResult> Results() =>
            Read<SCharacterLeaveResultPacket>(NetworkPacketType.SMSG_CHARACTER_LEAVE_RESULT).Select(p => p.Result).ToList();
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
            dialogue, LootRepositories.Empty(), Avalon.Server.World.UnitTests.Chat.ChatLimits.Off());

        await world.LoadAsync(CancellationToken.None);
        if (town is not null)
            await world.InstanceRegistry.GetOrCreateTownInstanceAsync(new MapTemplateId(1), 30).Published(world).WaitAsync(Limit);
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
            saver,
            new PartyService(Options.Create(new GameConfiguration()), TimeProvider.System, NullLogger<PartyService>.Instance))
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
        private QuestService? _quests;
        private Avalon.World.Items.ItemUseService? _itemUses;

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(ILoggerFactory)) return NullLoggerFactory.Instance;

            // PvpToggleHandler takes the one PvP toggle (#164), a class with settings and a clock.
            if (serviceType == typeof(PvpToggle))
                return new PvpToggle(Options.Create(new GameConfiguration()), TimeProvider.System);

            // The party handlers (2026-09-30) take the one party service, a class with settings, a clock and a logger.
            // The chat handler (#722) takes the one chat limiter, a class with settings and a clock; off, as the defaults are.
            if (serviceType == typeof(Avalon.World.Chat.ChatRateLimiter))
                return new Avalon.World.Chat.ChatRateLimiter(Options.Create(new GameConfiguration()), TimeProvider.System);

            if (serviceType == typeof(PartyService))
                return new PartyService(Options.Create(new GameConfiguration()), TimeProvider.System,
                    NullLogger<PartyService>.Instance);

            // The quest handlers (#433) and the world server take the one quest service, as production's singleton.
            if (serviceType == typeof(QuestService))
                return _quests ??= Avalon.Server.World.UnitTests.Quests.InertQuestService.Create();

            // ItemUseHandler takes the one item use service, as production's singleton.
            if (serviceType == typeof(Avalon.World.Items.ItemUseService))
                return _itemUses ??= Avalon.Server.World.UnitTests.ItemUse.InertItemUseService.Create();

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
