using System.Net;
using System.Net.Sockets;
using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Cryptography;
using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Character;
using Avalon.Server.World.UnitTests.GameAuth;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Inventory;
using Avalon.World.Maintenance;
using Avalon.World.Persistence;
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
    private static readonly CharacterId s_theCharacter = new(7);
    private static readonly CharacterId s_anotherCharacter = new(8);
    private static readonly AccountId s_theAccount = new(42L);

    /// <summary>
    /// How long a test waits for work that finishes on the thread pool (a save chain, a read) before
    /// it fails. It decides nothing: no outcome under test is timed on the wall clock (the select's
    /// save wait runs on <see cref="_clock" />), so a slow runner only makes a test slower, and this
    /// bound only stops a broken one from hanging.
    /// </summary>
    private static readonly TimeSpan s_patience = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The select handler's clock. It moves only when a test advances it, so the save wait runs out
    /// exactly when a test says so and never on its own while a test is waiting for a save.
    /// </summary>
    private readonly ManualTimerClock _clock = new();

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

        IWorldServer server = Substitute.For<IWorldServer, IServerBase>();
        _connection = new Avalon.World.WorldConnection(
            server, _clientSide, NullLoggerFactory.Instance, Substitute.For<IPacketReader>())
        {
            AccountId = s_theAccount
        };

        // The select handler sends, and a real connection seals what it sends. Agreeing a key with
        // a throwaway peer is the cheapest way to make Encrypt work.
        _connection.CryptoSession.Initialize(new CryptoManager().GetPublicKey());
        GameplayTestAdmission.Admit(_connection);

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
        _select.Execute(_connection, new CCharacterSelectedPacket { CharacterId = s_theCharacter });

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
        _inventory.GetByCharacterIdAsync(s_theCharacter, Arg.Any<CancellationToken>())
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

        new CharacterDeleteHandler(NullLogger<CharacterDeleteHandler>.Instance, _characters)
            .Execute(_connection, new CCharacterDeletePacket { CharacterId = s_theCharacter });
        Step(2);

        _characters.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
        // One lookup, the select's: the delete was refused before it made its own.
        _characters.Received(1).FindForGameplayAsync(Arg.Is<GameplayWriteAuthority>(a => a.AccountId == s_theAccount), s_theCharacter, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Refuse_a_second_select_that_lands_while_the_first_is_still_loading()
    {
        StartSelect();
        Step(2);

        _select.Execute(_connection, new CCharacterSelectedPacket { CharacterId = s_anotherCharacter });

        _characters.DidNotReceive().FindForGameplayAsync(Arg.Is<GameplayWriteAuthority>(a => a.AccountId == s_theAccount), s_anotherCharacter, Arg.Any<CancellationToken>());
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
                Substitute.For<IItemIdAllocator>(),
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
        _characters.FindForGameplayAsync(Arg.Is<GameplayWriteAuthority>(a => a.AccountId == s_theAccount), s_anotherCharacter, Arg.Any<CancellationToken>())
            .Returns((Character?)null);

        _select.Execute(_connection, new CCharacterSelectedPacket { CharacterId = s_anotherCharacter });
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
        _characters.Received(1).UpdateForGameplayAsync(Arg.Any<GameplayWriteAuthority>(),
            Arg.Is<Character>(c => c.Id == s_theCharacter && !c.Online), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The select sends its last packet before its final database steps, so a quick client reports the map loaded
    /// while no spawn exists yet. That report is held, and the sweep after the spawn is armed releases it at once
    /// rather than leaving the player to wait out the whole barrier.
    /// </summary>
    [Fact]
    public void Release_a_load_report_that_landed_mid_select_on_the_next_sweep()
    {
        IWorld world = Substitute.For<IWorld>();
        var loaded = new CharacterLoadedHandler(NullLogger<CharacterLoadedHandler>.Instance, world);
        StartSelect();
        Step(5);
        Assert.Null(_connection.PendingSpawn);

        loaded.Execute(_connection, new CCharacterLoadedPacket());
        Step();
        Assert.NotNull(_connection.PendingSpawn);

        CharacterReadinessBarrier.ReleaseExpired([_connection], world, _connection.PendingSpawn!.SinceTicks,
            TimeSpan.FromSeconds(15), NullLogger.Instance);

        world.Received(1).SpawnInInstance(_connection, Arg.Any<IMapInstance>());
        Assert.NotNull(_connection.Character);
    }

    /// <summary>
    /// A relog must not read the character before the previous session's despawn save commits, or
    /// the new session loads the inventory and money as they were before that save.
    /// </summary>
    [Fact]
    public async Task Read_the_character_only_once_its_despawn_save_has_committed()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int committed = 0;
        ICharacterSaveRepository repository = Substitute.For<ICharacterSaveRepository>();
        repository.WriteAsync(Arg.Any<IReadOnlyList<CharacterSaveBatch>>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await gate.Task.WaitAsync(s_patience);
                Volatile.Write(ref committed, 1);
            });
        var saver = new CharacterSaver(repository, NullLogger<CharacterSaver>.Instance);
        CharacterSelectHandler select = BuildSelectHandler(saver);

        var read = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _characters.When(c => c.FindForGameplayAsync(Arg.Is<GameplayWriteAuthority>(a => a.AccountId == s_theAccount), s_theCharacter, Arg.Any<CancellationToken>()))
            .Do(_ => read.TrySetResult(Volatile.Read(ref committed) == 1));

        Task<bool> despawn = saver.SaveOnDespawnAsync(
            Avalon.Server.World.UnitTests.Inventory.TestCharacters.New(s_theCharacter.Value), prepareRow: null, CancellationToken.None);
        select.Execute(_connection, new CCharacterSelectedPacket { CharacterId = s_theCharacter });

        Assert.False(read.Task.IsCompleted, "the select read the character while its save was still in flight");

        // The select's save wait is on _clock, which never moves here, so only the commit can end it:
        // however slowly the runner gets the save to commit, the select cannot give up first.
        gate.SetResult();
        Assert.True(await despawn.WaitAsync(s_patience));
        Assert.True(await read.Task.WaitAsync(s_patience), "the read ran before the commit");

        // The read is recorded as it is made, a moment before the step holding it completes on the
        // thread pool, so the chain's next flush is polled for rather than stepped once.
        await WaitUntilAsync(() => _connection.PendingSpawn != null || StepOnce());
        Assert.NotNull(_connection.PendingSpawn);
    }

    /// <summary>
    /// A save that never finishes must not strand the select, and must not be read around either:
    /// the row and slots it reads would be the ones from before that save, the save would then
    /// commit, and the new session's first save would write the stale money and slots back over it.
    /// Past the limit the select fails without reading anything, and the client can try again.
    /// </summary>
    [Fact]
    public async Task Fail_the_select_without_reading_once_the_wait_for_its_save_runs_out()
    {
        ICharacterSaver saver = Substitute.For<ICharacterSaver>();
        saver.WhenIdle(s_theCharacter).Returns(new TaskCompletionSource().Task);
        CharacterSelectHandler select = BuildSelectHandler(saver, TimeSpan.FromMilliseconds(50));

        select.Execute(_connection, new CCharacterSelectedPacket { CharacterId = s_theCharacter });
        Assert.True(_connection.SelectInProgress);

        // Just short of the limit the select is still waiting.
        _clock.Advance(TimeSpan.FromMilliseconds(49));
        Step(3);
        Assert.True(_connection.SelectInProgress, "the select gave up before its save wait ran out");

        // The limit runs out inside Advance: the wait's timer fires on this thread, and the step it
        // ended completes with it, so the next flush runs the chain's give-up. No real timer races
        // a real deadline.
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Step();
        Assert.False(_connection.SelectInProgress, "the select was still waiting after its save wait ran out");
        Step(3); // anything the failed select might still have queued

        await _characters.DidNotReceiveWithAnyArgs().FindForGameplayAsync(Arg.Any<GameplayWriteAuthority>(), default!, default);
        await _inventory.DidNotReceiveWithAnyArgs().GetByCharacterIdAsync(default!, default);
        await _itemInstances.DidNotReceiveWithAnyArgs().GetByCharacterIdAsync(default!, default);
        Assert.Null(_connection.PendingSpawn);
        Assert.Null(_connection.Character);
    }

    /// <summary>
    /// One session per account (#474): a select on another connection of the account kicks this
    /// one while its own select is still part way through. Kicking it is not enough on its own:
    /// its chain has steps still queued, and left to run they would write the row it read, load its
    /// inventory and build a pending spawn on a connection that is going away. Each step checks that
    /// its connection still owns the select it belongs to, and does nothing once it does not.
    /// </summary>
    [Fact]
    public void Stop_a_select_kicked_by_another_session_of_the_account_before_it_writes_or_builds_anything()
    {
        IWorldServer server = Substitute.For<IWorldServer>();
        CharacterSelectHandler select = BuildSelectHandler(worldServer: server);
        IWorldConnection kicker = PendingSpawnConnection.Create();
        server.SessionsOf(s_theAccount, kicker).Returns(new List<IWorldConnection> { _connection });

        select.Execute(_connection, new CCharacterSelectedPacket { CharacterId = s_theCharacter });
        Step(); // the character is read; the next step would send it and write its row

        select.Execute(kicker, new CCharacterSelectedPacket { CharacterId = s_anotherCharacter });
        Step(6);

        Assert.False(_connection.SelectInProgress);
        Assert.Null(_connection.PendingSpawn);
        Assert.Null(_connection.Character);
        _characters.DidNotReceiveWithAnyArgs().UpdateForGameplayAsync(default!, default(Character)!, default);
        _inventory.DidNotReceiveWithAnyArgs().GetByCharacterIdAsync(default!, default);
    }

    /// <summary>
    /// A kicked select may have a read in flight when it is kicked, and a step that is already
    /// running cannot be called back. The new select waits for it, bounded like the save wait, so
    /// nothing the kicked select started is still touching the database when the new one reads.
    /// </summary>
    [Fact]
    public async Task Wait_for_the_step_a_kicked_select_has_in_flight_before_reading()
    {
        IWorldServer server = Substitute.For<IWorldServer>();
        CharacterSelectHandler select = BuildSelectHandler(worldServer: server);
        IWorldConnection kicker = PendingSpawnConnection.Create();
        server.SessionsOf(s_theAccount, kicker).Returns(new List<IWorldConnection> { _connection });

        var held = new TaskCompletionSource<Character?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _characters.FindForGameplayAsync(Arg.Is<GameplayWriteAuthority>(a => a.AccountId == s_theAccount), s_theCharacter, Arg.Any<CancellationToken>())
            .Returns(held.Task);
        var readByKicker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _characters.FindForGameplayAsync(Arg.Is<GameplayWriteAuthority>(a => a.AccountId == s_theAccount), s_anotherCharacter, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                readByKicker.TrySetResult();
                return Task.FromResult<Character?>(null);
            });

        select.Execute(_connection, new CCharacterSelectedPacket { CharacterId = s_theCharacter });
        select.Execute(kicker, new CCharacterSelectedPacket { CharacterId = s_anotherCharacter });

        // The kicker's save wait is on _clock, which never moves here, so only the held step can end
        // it: the delay gives a premature read a chance to show, and cannot time the wait out.
        await Task.Delay(100);
        Assert.False(readByKicker.Task.IsCompleted, "the new select read while the kicked one still had a read in flight");

        held.SetResult(new Character
        {
            Id = s_theCharacter,
            AccountId = s_theAccount,
            Name = "Tester",
            Class = CharacterClass.Warrior,
            Level = 1,
            Map = TownMapId
        });

        await readByKicker.Task.WaitAsync(s_patience);
        Step(6);
        Assert.Null(_connection.PendingSpawn);
        await _characters.DidNotReceiveWithAnyArgs().UpdateForGameplayAsync(default!, default(Character)!, default);
    }

    private bool StepOnce()
    {
        Step();
        return false;
    }

    [Fact]
    public async Task Refuse_a_reselect_after_leave_before_BeginSelect_during_maintenance()
    {
        Assert.True(_connection.TryBeginLeave());
        _connection.EndLeave();
        IWorldEntryGate gate = Substitute.For<IWorldEntryGate>();
        gate.CheckAsync(s_theAccount, Arg.Any<CancellationToken>()).Returns(default(WorldEntryDecision));
        CharacterSelectHandler select = BuildSelectHandler(entryGate: gate);

        select.Execute(_connection, new CCharacterSelectedPacket { CharacterId = s_theCharacter });
        Assert.False(_connection.SelectInProgress);
        await WaitUntilAsync(() => _connection.IsClosing || StepOnce());

        Assert.Null(_connection.PendingSpawn);
        await _characters.DidNotReceiveWithAnyArgs().FindForGameplayAsync(Arg.Any<GameplayWriteAuthority>(), default!, default);
    }

    [Fact]
    public async Task Refuse_a_pending_spawn_when_maintenance_starts_during_select()
    {
        IWorldEntryGate gate = Substitute.For<IWorldEntryGate>();
        gate.CheckAsync(s_theAccount, Arg.Any<CancellationToken>())
            .Returns(new WorldEntryDecision(true, DateTime.MaxValue), default(WorldEntryDecision));
        CharacterSelectHandler select = BuildSelectHandler(entryGate: gate);
        select.Execute(_connection, new CCharacterSelectedPacket { CharacterId = s_theCharacter });
        await WaitUntilAsync(() => _connection.PendingSpawn is not null || StepOnce());

        IWorld releaseWorld = Substitute.For<IWorld>();
        var loaded = new CharacterLoadedHandler(NullLogger<CharacterLoadedHandler>.Instance,
            releaseWorld, gate);
        loaded.Execute(_connection, new CCharacterLoadedPacket());
        await WaitUntilAsync(() => _connection.IsClosing || StepOnce());

        releaseWorld.DidNotReceiveWithAnyArgs().SpawnInInstance(default!, default!);
        Assert.Null(_connection.Character);
        Assert.Equal(2, gate.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IWorldEntryGate.CheckAsync)));
    }

    [Fact]
    public async Task Answer_a_select_whose_entry_check_the_saturated_work_queue_refused()
    {
        IWorldEntryGate gate = Substitute.For<IWorldEntryGate>();
        gate.CheckAsync(s_theAccount, Arg.Any<CancellationToken>()).Returns(new WorldEntryDecision(true, DateTime.MaxValue));
        CharacterSelectHandler select = BuildSelectHandler(entryGate: gate, databaseWork: new SaturatedDatabaseWork());

        select.Execute(_connection, new CCharacterSelectedPacket { CharacterId = s_theCharacter });
        await WaitUntilAsync(() => _connection.IsClosing || StepOnce());

        Assert.False(_connection.SelectInProgress);
        Assert.Null(_connection.PendingSpawn);
        await gate.DidNotReceiveWithAnyArgs().CheckAsync(default!, default);
    }

    /// <summary>The handler's work queue with every slot taken: it refuses each operation, as <see cref="WorldDatabaseWork" /> does.</summary>
    private sealed class SaturatedDatabaseWork : IWorldDatabaseWork
    {
        public Task<T> Run<T>(Func<Task<T>> operation) => Task.FromException<T>(new WorldWorkUnavailableException());
    }

    /// <summary>
    /// Polls for work that finishes on the thread pool, not on a timer, bounded by
    /// <see cref="s_patience" /> only so a broken chain fails instead of hanging.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + s_patience;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting for the select chain");
            await Task.Delay(10);
        }
    }

    private CharacterSelectHandler BuildSelectHandler(ICharacterSaver? saver = null, TimeSpan? saveWaitLimit = null,
        IWorldServer? worldServer = null, IAccountRepository? accounts = null, IWorldEntryGate? entryGate = null,
        IWorldDatabaseWork? databaseWork = null)
    {
        var row = new Character
        {
            Id = s_theCharacter,
            AccountId = s_theAccount,
            Name = "Tester",
            Class = CharacterClass.Warrior,
            Level = 1,
            Map = TownMapId,
            X = 1,
            Y = 2,
            Z = 3
        };

        _characters.FindForGameplayAsync(Arg.Is<GameplayWriteAuthority>(a => a.AccountId == s_theAccount), s_theCharacter, Arg.Any<CancellationToken>())
            .Returns(row);
        _characters.UpdateForGameplayAsync(Arg.Any<GameplayWriteAuthority>(), Arg.Any<Character>(), Arg.Any<CancellationToken>()).Returns(row);

        _inventory.GetByCharacterIdAsync(s_theCharacter, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CharacterInventory>());
        _itemInstances.GetByCharacterIdAsync(s_theCharacter, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ItemInstance>());

        ICharacterAbilityRepository abilities = Substitute.For<ICharacterAbilityRepository>();
        abilities.GetCharacterAbilitiesAsync(s_theCharacter, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CharacterAbility>());

        IMapInstance instance = Substitute.For<IMapInstance>();
        instance.InstanceId.Returns(Guid.NewGuid());
        IInstanceRegistry registry = Substitute.For<IInstanceRegistry>();
        registry.GetOrCreateTownInstanceAsync(new MapTemplateId(TownMapId), Arg.Any<ushort>())
            .Returns(Task.FromResult(instance));

        StaticData staticData = EmptyStaticData();

        IWorld world = Substitute.For<IWorld>();
        world.Configuration.Returns(new GameConfiguration());   // the select reads Game:FuryDecayPerSecond (#526)
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
            Options.Create(new RegenConfiguration()),
            accounts ?? Substitute.For<IAccountRepository>(),
            saver ?? Substitute.For<ICharacterSaver>(),
            worldServer ?? Substitute.For<IWorldServer>(),
            _clock,
            entryGate: entryGate,
            databaseWork: databaseWork ?? InlineDatabaseWork.Instance)
        {
            SaveWaitLimit = saveWaitLimit ?? TimeSpan.FromSeconds(5)
        };
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
                CharacterId = s_theCharacter,
                Container = container,
                Slot = slot,
                ItemId = id
            });
            instances.Add(new ItemInstance
            {
                Id = id,
                TemplateId = new ItemTemplateId(template),
                CharacterId = s_theCharacter,
                Count = 1,
                Durability = 100,
                Flags = ItemInstanceFlags.None
            });
        }

        _inventory.GetByCharacterIdAsync(s_theCharacter, Arg.Any<CancellationToken>()).Returns(rows);
        _itemInstances.GetByCharacterIdAsync(s_theCharacter, Arg.Any<CancellationToken>())
            .Returns(instances);
    }

    /// <summary>
    /// #433: the account lookup that carries the locale is not ordered with the select chain. When it lands after
    /// the pending spawn exists, the character's quest lines must still follow the account's locale, not the
    /// connection's enUS default the spawn copied.
    /// </summary>
    [Fact]
    public void Give_the_selected_character_the_accounts_locale_when_the_lookup_lands_after_the_spawn()
    {
        var lookup = new TaskCompletionSource<Avalon.Domain.Auth.Account?>(TaskCreationOptions.RunContinuationsAsynchronously);
        IAccountRepository accounts = Substitute.For<IAccountRepository>();
        accounts.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(lookup.Task);
        CharacterSelectHandler select = BuildSelectHandler(accounts: accounts);

        select.Execute(_connection, new CCharacterSelectedPacket { CharacterId = s_theCharacter });
        Step(6);
        var character = (CharacterEntity)_connection.PendingSpawn!.Character;
        Assert.Equal(Avalon.Common.Accounts.AccountLocale.enUS, character.Quests.Locale);

        lookup.SetResult(AccountIn(Avalon.Common.Accounts.AccountLocale.deDE));
        Step();

        Assert.Equal(Avalon.Common.Accounts.AccountLocale.deDE, _connection.Locale);
        Assert.Equal(Avalon.Common.Accounts.AccountLocale.deDE, character.Quests.Locale);
    }

    private static Avalon.Domain.Auth.Account AccountIn(Avalon.Common.Accounts.AccountLocale locale) => new()
    {
        Id = s_theAccount,
        Username = "TESTER",
        Salt = [],
        Verifier = [],
        Email = "tester@example.com",
        JoinDate = DateTime.UnixEpoch,
        Locale = locale,
    };

    /// <summary>
    /// The account lookup landing before the spawn: Spawn copies the connection's locale onto the character.
    /// </summary>
    [Fact]
    public void Give_the_selected_character_the_accounts_locale_when_the_lookup_lands_first()
    {
        IAccountRepository accounts = Substitute.For<IAccountRepository>();
        accounts.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Avalon.Domain.Auth.Account?>(AccountIn(Avalon.Common.Accounts.AccountLocale.frFR)));
        CharacterSelectHandler select = BuildSelectHandler(accounts: accounts);

        select.Execute(_connection, new CCharacterSelectedPacket { CharacterId = s_theCharacter });
        Step(6);

        Assert.Equal(Avalon.Common.Accounts.AccountLocale.frFR,
            ((CharacterEntity)_connection.PendingSpawn!.Character).Quests.Locale);
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
        ICharacterLevelExperienceRepository levels = Substitute.For<ICharacterLevelExperienceRepository>();
        levels.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<CharacterLevelExperience>());
        IClassLevelStatRepository stats = Substitute.For<IClassLevelStatRepository>();
        stats.FindAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ClassLevelStat>());
        ICharacterCreateInfoRepository createInfos = Substitute.For<ICharacterCreateInfoRepository>();
        createInfos.FindAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<CharacterCreateInfo>());
        IItemTemplateRepository items = Substitute.For<IItemTemplateRepository>();
        items.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<ItemTemplate>());
        IAbilityTemplateRepository abilityTemplates = Substitute.For<IAbilityTemplateRepository>();
        abilityTemplates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new List<AbilityTemplate>());

        ILocalizedTextRepository localizedText = Substitute.For<ILocalizedTextRepository>();
        localizedText.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LocalizedText>>([]));
        localizedText.GetAllLocalesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<LocalizedTextLocale>>([]));
        localizedText.GetAllClassNamesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CharacterClassName>>([]));

        IDialogueRepository dialogue = Substitute.For<IDialogueRepository>();
        dialogue.GetAllNodesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<DialogueNode>>([]));
        dialogue.GetAllOptionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<DialogueOption>>([]));

        ICreatureTemplateRepository creatureTemplates = Substitute.For<ICreatureTemplateRepository>();
        creatureTemplates.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<CreatureTemplate>()));
        ICreatureBaseStatRepository baseStats = Substitute.For<ICreatureBaseStatRepository>();
        baseStats.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CreatureBaseStat>>(
                [new CreatureBaseStat { Level = 1, Health = 1, DamageMin = 1, DamageMax = 1, Experience = 1 }]));
        ICreatureRarityModifierRepository rarities = Substitute.For<ICreatureRarityModifierRepository>();
        rarities.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<CreatureRarityModifier>>([]));

        var data = new StaticData(createInfos, stats, items, abilityTemplates, levels,
            creatureTemplates, baseStats, rarities,
            localizedText, dialogue, LootRepositories.Empty(), NullLoggerFactory.Instance);
        data.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        return data;
    }
}
