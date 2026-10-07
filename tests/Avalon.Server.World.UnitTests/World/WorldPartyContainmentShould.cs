using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Party;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Maps;
using Avalon.World.Parties;
using Avalon.World.Public;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Scripts.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.World;

/// <summary>
/// A party call that throws is contained where World makes it (party play, final review): the party tick must not
/// leave World.Update before the instances tick, and a party's online or instance-change bookkeeping must not cost
/// the character its online row or its transfer. The party service is made to throw through its clock.
/// </summary>
public class WorldPartyContainmentShould
{
    private static readonly MapTemplateId TownId = new(1);
    private readonly BreakableClock _clock = new();
    private readonly TestLog _log = new();
    private readonly PartyService _parties;

    public WorldPartyContainmentShould() =>
        _parties = new PartyService(Options.Create(new GameConfiguration()), _clock, NullLogger<PartyService>.Instance);

    private sealed class SilentReloader : IScriptHotReloader
    {
        public void Update(out List<Type> scriptTypes) => scriptTypes = [];

        public event ScriptsHotReloadedEventHandler? ScriptsHotReloaded;

        public void Start() => ScriptsHotReloaded?.Invoke([]);

        public void Stop() { }
    }

    [Fact]
    public async Task Tick_the_instances_when_the_party_tick_throws_and_log_it_once_per_interval()
    {
        using MapInstance town = TestMapInstances.Build(NewWorld(), mapType: MapType.Town);
        MapInstanceClient inTown = Join(town, 700_001);
        IAvalonMapManager mapManager = Substitute.For<IAvalonMapManager>();
        mapManager.Templates.Returns([new MapTemplate { Id = TownId, MapType = MapType.Town }]);
        IChunkLayoutInstanceFactory factory = Substitute.For<IChunkLayoutInstanceFactory>();
        factory.BuildAsync(default!, default, default).ReturnsForAnyArgs(town);
        Avalon.World.World world = await BuildAsync(mapManager, factory);
        await world.InstanceRegistry.GetOrCreateTownInstanceAsync(TownId, maxPlayers: 100).Published(world);
        _clock.Broken = true;

        Exception? thrown = Record.Exception(() =>
        {
            world.Update(TimeSpan.FromSeconds(1d / 60d));
            world.Update(TimeSpan.FromSeconds(1d / 60d));
        });

        Assert.Null(thrown);
        inTown.Connection.Received(2).UpdateMap();
        Assert.Single(_log.Errors, e => e.Message.Contains("The party tick", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Mark_the_character_online_when_bringing_it_online_in_its_party_throws()
    {
        Avalon.World.World world = await BuildAsync();
        (IWorldConnection a, CharacterEntity character) = InParty();
        character.Data!.Online = false;
        _clock.Broken = true;

        Exception? thrown = Record.Exception(() => world.SpawnInInstance(a, Substitute.For<IMapInstance>()));

        Assert.Null(thrown);
        Assert.True(character.Data.Online);
        Assert.Contains(_log.Errors, e => e.Exception is InvalidOperationException { Message: "clock broken" });
    }

    [Fact]
    public async Task Finish_the_transfer_when_telling_the_party_throws()
    {
        Avalon.World.World world = await BuildAsync();
        (IWorldConnection a, CharacterEntity character) = InParty();
        IMapInstance target = Substitute.For<IMapInstance>();
        var targetId = Guid.NewGuid();
        target.InstanceId.Returns(targetId);
        _clock.Broken = true;

        Exception? thrown = Record.Exception(() => world.TransferPlayer(a, target));

        Assert.Null(thrown);
        target.Received(1).AddCharacter(a);
        Assert.Equal(targetId, character.InstanceId);
        Assert.Contains(_log.Errors, e => e.Exception is InvalidOperationException { Message: "clock broken" });
    }

    private Task<Avalon.World.World> BuildAsync(IAvalonMapManager? mapManager = null, IChunkLayoutInstanceFactory? factory = null) =>
        ScriptHotReloadPollingShould.BuildWorldAsync(new SilentReloader(), intervalSeconds: 60, mapManager, factory,
            _parties, _log);

    /// <summary>Two characters online in one party; the leader is returned.</summary>
    private (IWorldConnection Connection, CharacterEntity Character) InParty()
    {
        (IWorldConnection a, CharacterEntity leader) = Online(700_011);
        (IWorldConnection _, CharacterEntity member) = Online(700_012);
        Assert.Equal(PartyResult.Ok, _parties.Invite(leader.Guid.Id, member.Name));
        Assert.Equal(PartyResult.Ok, _parties.Respond(member.Guid.Id, accept: true));
        return (a, leader);
    }

    private (IWorldConnection, CharacterEntity) Online(uint id)
    {
        CharacterEntity character = TestCharacters.New(id);
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        connection.AccountId.Returns(new AccountId(id));
        connection.Locale.Returns(AccountLocale.enUS);
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        _parties.CharacterOnline(connection);
        return (connection, character);
    }
}
