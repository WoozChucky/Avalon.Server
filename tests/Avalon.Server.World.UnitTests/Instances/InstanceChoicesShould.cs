using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World.ChunkLayouts;
using Avalon.World.Instances;
using Avalon.World.Maps;
using Avalon.World.Parties;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// #1104: an owner (a character or a party) holds several live instances of a map, one of them the default a plain
/// portal entry takes; a create builds a new one, listed but not the default until it is made so.
/// </summary>
public class InstanceChoicesShould : IDisposable
{
    private static readonly MapTemplateId s_dungeonId = new(2);
    private static readonly MapTemplateId s_caveId = new(3);
    private static readonly TimeSpan s_bound = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan s_lifetime = TimeSpan.FromMinutes(15);
    private static readonly PartyId s_party = new(9);

    private readonly IChunkLayoutInstanceFactory _factory = Substitute.For<IChunkLayoutInstanceFactory>();
    private readonly List<TaskCompletionSource<MapInstance>> _builds = [];
    private readonly List<(MapTemplate Template, uint? Character, PartyId? Party)> _requested = [];
    private readonly List<MapInstance> _instances = [];
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
    private readonly InstanceRegistry _registry;

    public InstanceChoicesShould()
    {
        IAvalonMapManager mapManager = Substitute.For<IAvalonMapManager>();
        mapManager.Templates.Returns([
            new MapTemplate { Id = s_dungeonId, MapType = MapType.Normal },
            new MapTemplate { Id = s_caveId, MapType = MapType.Normal },
        ]);
        _factory.BuildAsync(default!, default, default, default).ReturnsForAnyArgs(call =>
        {
            _requested.Add((call.ArgAt<MapTemplate>(0), call.ArgAt<uint?>(1), call.ArgAt<PartyId?>(3)));
            var build = new TaskCompletionSource<MapInstance>(); // inline, so a finished build is queued at once
            _builds.Add(build);
            return build.Task;
        });
        _registry = new InstanceRegistry(NullLoggerFactory.Instance, mapManager, _factory, abandonedInstanceLifetime: s_lifetime);
    }

    [Fact] // a create leaves the default alone until MakeDefault, and both stay listed
    public async Task Keep_the_default_until_the_created_instance_is_made_default()
    {
        IMapInstance first = await EnterDefault(character: 7);
        Task<IMapInstance> created = _registry.CreatePrivateInstanceAsync(7, s_dungeonId);
        Assert.Equal(2, _builds.Count); // a build of its own, not the live default
        Publish();
        IMapInstance second = await created.WaitAsync(s_bound);

        Assert.Same(first, await _registry.GetOrCreateNormalInstanceAsync(7, s_dungeonId).WaitAsync(s_bound));
        _registry.MakeDefault(second);
        Assert.Same(second, await _registry.GetOrCreateNormalInstanceAsync(7, s_dungeonId).WaitAsync(s_bound));

        var listed = new List<InstanceChoice>();
        _registry.ListChoosable(7, party: null, s_dungeonId, listed);
        Assert.Equal(2, listed.Count);
        Assert.True(listed.Single(c => c.Instance == second).IsDefault);
        Assert.False(listed.Single(c => c.Instance == first).IsDefault);
        Assert.All(listed, c => Assert.Equal(InstanceKind.Private, c.Kind));
    }

    /// <summary>
    /// A create and a default build of the same owner and map in flight together, whichever came first: neither joins
    /// the other, so each requester gets its own instance.
    /// </summary>
    [Fact]
    public async Task Never_join_a_create_and_a_default_build_of_the_same_owner_and_map()
    {
        Task<IMapInstance> privateDefault = _registry.GetOrCreateNormalInstanceAsync(7, s_dungeonId);
        Task<IMapInstance> privateCreate = _registry.CreatePrivateInstanceAsync(7, s_dungeonId);
        Task<IMapInstance> partyCreate = _registry.CreatePartyInstanceAsync(s_party, s_dungeonId);
        Task<IMapInstance> partyDefault = _registry.GetOrCreatePartyInstanceAsync(s_party, s_dungeonId);

        Assert.Equal(4, _builds.Count);
        Publish();
        IMapInstance[] got = await Task.WhenAll(privateDefault, privateCreate, partyCreate, partyDefault).WaitAsync(s_bound);

        Assert.Equal(4, got.Distinct().Count());
    }

    [Fact] // the default, the last of the party's list, expired while an earlier entry is occupied
    public async Task Route_a_party_to_its_last_live_instance_in_default_order_when_the_default_expired()
    {
        Task<IMapInstance> oldBuild = _registry.GetOrCreatePartyInstanceAsync(s_party, s_dungeonId);
        Publish();
        var old = (MapInstance)await oldBuild.WaitAsync(s_bound);
        MapInstanceClients.Join(old, 1); // a member stays inside

        Task<IMapInstance> createdBuild = _registry.CreatePartyInstanceAsync(s_party, s_dungeonId);
        Publish();
        var created = (MapInstance)await createdBuild.WaitAsync(s_bound);
        _registry.MakeDefault(created);
        MapInstanceClient leader = MapInstanceClients.Join(created, 2);
        created.RemoveCharacter(leader.Connection); // the leader left it empty

        _clock.Now += s_lifetime;
        // Expired but not yet freed: skipped for the older live one.
        Assert.Same(old, await _registry.GetOrCreatePartyInstanceAsync(s_party, s_dungeonId).WaitAsync(s_bound));
        _registry.ProcessExpiredInstances(s_lifetime);

        Assert.Same(old, await _registry.GetOrCreatePartyInstanceAsync(s_party, s_dungeonId).WaitAsync(s_bound));
        Assert.Equal(2, _builds.Count);
        var listed = new List<InstanceChoice>();
        _registry.ListChoosable(1, s_party, s_dungeonId, listed);
        InstanceChoice only = Assert.Single(listed);
        Assert.Same(old, only.Instance);
        Assert.True(only.IsDefault);
        Assert.Equal(InstanceKind.Party, only.Kind);
    }

    /// <summary>
    /// Character 7 in party 9 asks for an instance that is not its own private one nor its party's live one of the
    /// map: the answer is null whichever it is.
    /// </summary>
    [Theory]
    [InlineData("other character's private")]
    [InlineData("other party's")]
    [InlineData("expired")]
    [InlineData("unknown id")]
    [InlineData("another map")]
    public async Task Find_no_choosable_instance_the_character_may_not_enter(string case_)
    {
        Guid asked;
        switch (case_)
        {
            case "other character's private":
                asked = (await EnterDefault(character: 8)).InstanceId;
                break;
            case "other party's":
                Task<IMapInstance> theirs = _registry.GetOrCreatePartyInstanceAsync(new PartyId(10), s_dungeonId);
                Publish();
                asked = (await theirs.WaitAsync(s_bound)).InstanceId;
                break;
            case "expired":
                var own = (MapInstance)await EnterDefault(character: 7);
                own.RemoveCharacter(MapInstanceClients.Join(own, 7).Connection);
                _clock.Now += s_lifetime; // expired, not yet freed
                asked = own.InstanceId;
                break;
            case "unknown id":
                await EnterDefault(character: 7);
                asked = Guid.NewGuid();
                break;
            default: // its own, of the cave, asked for the dungeon
                Task<IMapInstance> cave = _registry.GetOrCreateNormalInstanceAsync(7, s_caveId);
                Publish();
                asked = (await cave.WaitAsync(s_bound)).InstanceId;
                break;
        }

        Assert.Null(_registry.FindChoosable(7, s_party, s_dungeonId, asked));
    }

    [Fact] // own private and own party's instances are found
    public async Task Find_the_character_s_own_and_its_party_s_instances()
    {
        IMapInstance own = await EnterDefault(character: 7);
        Task<IMapInstance> createdBuild = _registry.CreatePrivateInstanceAsync(7, s_dungeonId);
        Task<IMapInstance> partyBuild = _registry.GetOrCreatePartyInstanceAsync(s_party, s_dungeonId);
        Publish();
        IMapInstance created = await createdBuild.WaitAsync(s_bound);
        IMapInstance partys = await partyBuild.WaitAsync(s_bound);

        Assert.Same(own, _registry.FindChoosable(7, s_party, s_dungeonId, own.InstanceId));
        Assert.Same(created, _registry.FindChoosable(7, s_party, s_dungeonId, created.InstanceId));
        Assert.Same(partys, _registry.FindChoosable(7, s_party, s_dungeonId, partys.InstanceId));
    }

    [Fact] // a create for a party forgotten while it built is published, unlisted, and MakeDefault does nothing
    public async Task Leave_a_party_create_published_after_the_party_was_forgotten_unlisted()
    {
        Task<IMapInstance> createdBuild = _registry.CreatePartyInstanceAsync(s_party, s_dungeonId);
        _registry.ForgetParty(s_party); // the party disbands mid-build
        Publish();
        IMapInstance orphan = await createdBuild.WaitAsync(s_bound);

        Assert.Same(orphan, _registry.GetInstanceById(orphan.InstanceId));
        _registry.MakeDefault(orphan);

        var listed = new List<InstanceChoice>();
        _registry.ListChoosable(7, s_party, s_dungeonId, listed);
        Assert.Empty(listed);
        Assert.Null(_registry.FindChoosable(7, s_party, s_dungeonId, orphan.InstanceId));
        _ = _registry.GetOrCreatePartyInstanceAsync(s_party, s_dungeonId);
        Assert.Equal(2, _builds.Count); // a new build, not the orphan
    }

    public void Dispose()
    {
        foreach (MapInstance instance in _instances)
            instance.Dispose();
    }

    /// <summary>A plain entry of the character into the dungeon, built and published.</summary>
    private async Task<IMapInstance> EnterDefault(uint character)
    {
        Task<IMapInstance> entry = _registry.GetOrCreateNormalInstanceAsync(character, s_dungeonId);
        Publish();
        return await entry.WaitAsync(s_bound);
    }

    /// <summary>Finishes every build still running, for its owner and map, and publishes them as the tick does (#639).</summary>
    private void Publish()
    {
        for (int i = 0; i < _builds.Count; i++)
        {
            if (_builds[i].Task.IsCompleted)
                continue;

            MapInstance instance = TestMapInstances.Build(MapInstanceClients.NewWorld(), time: _clock,
                ownerPartyId: _requested[i].Party, templateId: _requested[i].Template.Id,
                ownerCharacterId: _requested[i].Character);
            _instances.Add(instance);
            _builds[i].SetResult(instance);
        }

        _registry.PublishFinished();
    }
}
