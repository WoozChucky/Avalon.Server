using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Server.World.UnitTests.World;
using Avalon.World.Characters;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Instances;
using Avalon.World.Maps;
using Avalon.World.Parties;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Avalon.World.Scripts.Abstractions;
using Avalon.World.Social;
using Avalon.World.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Threading;

/// <summary>
/// #639: what only the tick may change asserts it is on the tick. With the tick bound to the test's thread, each such
/// call made from another thread throws the guard's error naming it, and the same call on the bound thread does not.
/// </summary>
public class TickThreadGuardSitesShould
{
    private readonly TickThreadGuard _guard = new();

    public TickThreadGuardSitesShould() => _guard.Bind();

    private sealed class SilentReloader : IScriptHotReloader
    {
        public void Update(out List<Type> scriptTypes) => scriptTypes = [];

        public event ScriptsHotReloadedEventHandler? ScriptsHotReloaded;

        public void Start() => ScriptsHotReloaded?.Invoke([]);

        public void Stop() { }
    }

    private static void AssertRefusedOffTick(Action call, string operation)
    {
        Exception? thrown = TickThreadGuardProbe.OffThread(call);

        InvalidOperationException refused = Assert.IsType<InvalidOperationException>(thrown);
        Assert.Contains(operation, refused.Message, StringComparison.Ordinal);
        Assert.Contains("tick thread", refused.Message, StringComparison.Ordinal);
    }

    private static IWorldConnection ConnectionHolding(uint characterId)
    {
        ICharacter character = Substitute.For<ICharacter>();
        character.Guid.Returns(new ObjectGuid(ObjectType.Character, characterId));
        character.Name.Returns("Kaela");
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        return connection;
    }

    /// <summary>A loaded world holding the guard, built on the pool and then bound again to this thread, so the calls
    /// below run where the tick is bound whatever thread the load finished on.</summary>
    private Avalon.World.World LoadedWorld()
    {
        Avalon.World.World world = Task.Run(() =>
                ScriptHotReloadPollingShould.BuildWorldAsync(new SilentReloader(), intervalSeconds: 60, tickThread: _guard))
            .GetAwaiter().GetResult();
        _guard.Bind();
        return world;
    }

    [Fact]
    public void Guard_World_TransferPlayer()
    {
        Avalon.World.World world = LoadedWorld();
        IMapInstance target = Substitute.For<IMapInstance>();

        AssertRefusedOffTick(() => world.TransferPlayer(ConnectionHolding(1), target), "World.TransferPlayer");
        world.TransferPlayer(ConnectionHolding(1), target);
        target.ReceivedWithAnyArgs(1).AddCharacter(default!);
    }

    [Fact]
    public void Guard_World_SpawnInInstance()
    {
        Avalon.World.World world = LoadedWorld();
        IMapInstance instance = Substitute.For<IMapInstance>();

        AssertRefusedOffTick(() => world.SpawnInInstance(ConnectionHolding(1), instance), "World.SpawnInInstance");
        world.SpawnInInstance(ConnectionHolding(1), instance);
        instance.ReceivedWithAnyArgs(1).AddCharacter(default!);
    }

    /// <summary>The synchronous part refuses, to the caller, never into a task a despawn may not await.</summary>
    [Fact]
    public async Task Guard_World_LeaveWorldAsync_and_the_despawn_that_calls_it()
    {
        Avalon.World.World world = LoadedWorld();
        IWorldConnection empty = Substitute.For<IWorldConnection>(); // holds no character, so the leave ends at once
        empty.Character.Returns((ICharacter?)null);
        empty.TakePendingSpawn().Returns((PendingSpawn?)null);

        AssertRefusedOffTick(() => _ = world.LeaveWorldAsync(empty), "World.LeaveWorldAsync");
        AssertRefusedOffTick(() => _ = world.DeSpawnPlayerAsync(empty), "World.LeaveWorldAsync");
        Task<bool> left = world.LeaveWorldAsync(empty);
        Assert.True(left.IsCompletedSuccessfully);
        Assert.False(await left);
    }

    [Fact]
    public void Guard_publishing_built_instances()
    {
        Avalon.World.World world = LoadedWorld();

        AssertRefusedOffTick(world.PublishBuiltInstances, "InstanceRegistry.PublishFinished");
        world.PublishBuiltInstances();
    }

    public static TheoryData<string> RegistryWrites() =>
    [
        "InstanceRegistry.GetOrCreateTownInstanceAsync",
        "InstanceRegistry.GetOrCreateNormalInstanceAsync",
        "InstanceRegistry.GetOrCreatePartyInstanceAsync",
        "InstanceRegistry.ForgetParty",
        "InstanceRegistry.RemoveInstance",
        "InstanceRegistry.ProcessExpiredInstances",
        "InstanceRegistry.PublishFinished",
    ];

    [Theory]
    [MemberData(nameof(RegistryWrites))]
    public void Guard_every_registry_index_write(string operation)
    {
        IAvalonMapManager mapManager = Substitute.For<IAvalonMapManager>();
        mapManager.Templates.Returns([]); // every build fails at once: only the guard is under test
        var registry = new InstanceRegistry(NullLoggerFactory.Instance, mapManager,
            Substitute.For<IChunkLayoutInstanceFactory>(), _guard);
        Action call = operation switch
        {
            "InstanceRegistry.GetOrCreateTownInstanceAsync" => () => _ = registry.GetOrCreateTownInstanceAsync(new MapTemplateId(1), 10),
            "InstanceRegistry.GetOrCreateNormalInstanceAsync" => () => _ = registry.GetOrCreateNormalInstanceAsync(1, new MapTemplateId(2)),
            "InstanceRegistry.GetOrCreatePartyInstanceAsync" => () => _ = registry.GetOrCreatePartyInstanceAsync(new PartyId(1), new MapTemplateId(2)),
            "InstanceRegistry.ForgetParty" => () => registry.ForgetParty(new PartyId(1)),
            "InstanceRegistry.RemoveInstance" => () => registry.RemoveInstance(Guid.NewGuid()),
            "InstanceRegistry.ProcessExpiredInstances" => () => registry.ProcessExpiredInstances(TimeSpan.FromMinutes(15)),
            _ => () => registry.PublishFinished(),
        };

        AssertRefusedOffTick(call, operation);
        call();
    }

    public static TheoryData<string> PartyMutators() =>
    [
        "InstanceChanged", "CharacterOnline", "CharacterOffline", "Invite", "HideInviteFrom", "Respond", "Leave", "Kick",
        "Promote", "SetExperienceMode", "Tick", "StartReturns", "ReturnFailed", "LevelChanged", "FlushMemberStatus",
    ];

    [Theory]
    [MemberData(nameof(PartyMutators))]
    public void Guard_every_party_service_mutator(string mutator)
    {
        var parties = new PartyService(Options.Create(new GameConfiguration()), TimeProvider.System,
            NullLogger<PartyService>.Instance, new OnlineCharacters(_guard), _guard);
        IWorldConnection connection = ConnectionHolding(1);
        Action call = mutator switch
        {
            "InstanceChanged" => () => parties.InstanceChanged(connection),
            "CharacterOnline" => () => parties.CharacterOnline(connection),
            "CharacterOffline" => () => parties.CharacterOffline(connection, connection.Character!),
            "Invite" => () => parties.Invite(1, "Borin"),
            "HideInviteFrom" => () => parties.HideInviteFrom(1, 2),
            "Respond" => () => parties.Respond(1, accept: true),
            "Leave" => () => parties.Leave(1),
            "Kick" => () => parties.Kick(1, 2),
            "Promote" => () => parties.Promote(1, 2),
            "SetExperienceMode" => () => parties.SetExperienceMode(1, Avalon.Network.Packets.Party.PartyExperienceMode.Even),
            "Tick" => () => parties.Tick(),
            "StartReturns" => () => parties.StartReturns([], null!),
            "ReturnFailed" => () => parties.ReturnFailed(1, new InvalidOperationException("return failed")),
            "LevelChanged" => () => parties.LevelChanged(connection.Character!),
            _ => () => parties.FlushMemberStatus(),
        };

        AssertRefusedOffTick(call, $"PartyService.{mutator}");
        call();
    }

    [Fact]
    public void Guard_the_writes_of_who_is_online()
    {
        var online = new OnlineCharacters(_guard);
        IWorldConnection connection = ConnectionHolding(7);

        AssertRefusedOffTick(() => online.Add(connection), "OnlineCharacters.Add");
        online.Add(connection);
        AssertRefusedOffTick(() => online.Remove(connection, connection.Character!), "OnlineCharacters.Remove");
        Assert.True(online.Remove(connection, connection.Character!));
    }

    [Fact]
    public void Guard_the_writes_of_an_ignore_list()
    {
        var ignores = new IgnoreList(new SaveStateTracker(), _guard);

        AssertRefusedOffTick(() => ignores.Load([]), "IgnoreList.Load");
        AssertRefusedOffTick(() => ignores.Add(2, "Borin", DateTime.UnixEpoch), "IgnoreList.Add");
        Assert.True(ignores.Add(2, "Borin", DateTime.UnixEpoch));
        AssertRefusedOffTick(() => ignores.Remove(2), "IgnoreList.Remove");
        Assert.True(ignores.Remove(2));
        ignores.Load([]);
    }

    /// <summary>The reads stay free: telemetry and the presence writer may read without the tick.</summary>
    [Fact]
    public void Let_reads_through_off_the_tick()
    {
        var online = new OnlineCharacters(_guard);
        var registry = new InstanceRegistry(NullLoggerFactory.Instance, Substitute.For<IAvalonMapManager>(),
            Substitute.For<IChunkLayoutInstanceFactory>(), _guard);

        Assert.Null(TickThreadGuardProbe.OffThread(() =>
        {
            _ = online.IsOnline(7);
            _ = registry.ActiveInstances;
            _ = registry.GetInstanceById(Guid.NewGuid());
        }));
    }
}
