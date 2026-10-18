using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Parties;
using Avalon.World;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Parties;
using Avalon.World.Public;
using Avalon.World.Public.Enums;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Handlers;

public class InstanceListHandlerShould
{
    private const ushort TownMap = 1;
    private const ushort ForestMap = 2;

    /// <summary>
    /// #1104: the portal's menu lists the character's own live instances of the forest and its party's, each with its
    /// kind, whether someone is inside, the time left before an empty one expires (Game:AbandonedInstanceLifetimeMinutes,
    /// here 20, once entered; the fixed 15 minutes while nobody has entered it) and the one a plain entry takes: the
    /// default first, then the party's, then the private ones, each in use first and then by most time left.
    /// </summary>
    [Fact]
    public void List_the_character_s_and_its_party_s_instances_with_kind_timer_and_default()
    {
        using var menu = new Menu(abandonedLifetimeMinutes: 20);
        PartyClient kaela = menu.Join(7, "Kaela");
        PartyClient borin = menu.Join(8, "Borin");
        menu.Parties.Form(kaela, borin);
        PartyId party = menu.Parties.Parties.PartyOf(kaela.Id)!.Id;

        MapInstance left = menu.Forest(ownerCharacterId: kaela.Id);
        IWorldConnection visitor = menu.Parties.Online(9).Connection;
        left.AddCharacter(visitor);
        left.RemoveCharacter(visitor); // entered, and empty from here
        menu.Clock.Advance(TimeSpan.FromMinutes(4));
        MapInstance partyDefault = menu.Forest(ownerPartyId: party); // the leader's newest, built, never entered
        MapInstance partyOccupied = menu.Forest(ownerPartyId: party);
        partyOccupied.AddCharacter(menu.Parties.Online(10).Connection);
        MapInstance privateOccupied = menu.Forest(ownerCharacterId: kaela.Id);
        privateOccupied.AddCharacter(menu.Parties.Online(11).Connection);
        MapInstance opened = menu.Forest(ownerCharacterId: kaela.Id); // built, never entered

        // Scrambled, so the answer's order is the handler's: each key apart from the others, a default that is empty
        // ahead of a party instance in use, and a party instance ahead of a private one in use.
        menu.Lists(kaela.Id, party, new InstanceChoice(opened, InstanceKind.Private, false),
            new InstanceChoice(privateOccupied, InstanceKind.Private, false),
            new InstanceChoice(partyOccupied, InstanceKind.Party, false),
            new InstanceChoice(left, InstanceKind.Private, false),
            new InstanceChoice(partyDefault, InstanceKind.Party, true));
        menu.Lists(borin.Id, party, new InstanceChoice(partyOccupied, InstanceKind.Party, false),
            new InstanceChoice(partyDefault, InstanceKind.Party, true));

        menu.Request(kaela);
        menu.Request(borin);

        uint fifteen = (uint)TimeSpan.FromMinutes(15).TotalMilliseconds;
        SInstanceListPacket toKaela = Assert.Single(menu.Answers(kaela));
        Assert.Equal((ForestMap, MapTransitionResult.Success, true, true),
            (toKaela.TargetMapId, toKaela.Result, toKaela.InParty, toKaela.IsLeader));
        Assert.Equal(
        [
            (partyDefault.InstanceId, InstanceKind.Party, false, fifteen, true),
            (partyOccupied.InstanceId, InstanceKind.Party, true, 0u, false),
            (privateOccupied.InstanceId, InstanceKind.Private, true, 0u, false),
            (left.InstanceId, InstanceKind.Private, false, (uint)TimeSpan.FromMinutes(16).TotalMilliseconds, false),
            (opened.InstanceId, InstanceKind.Private, false, fifteen, false),
        ], toKaela.Instances.ConvertAll(e => (e.InstanceId, e.Kind, e.InUse, e.ExpiresInMs, e.IsDefault)));

        SInstanceListPacket toBorin = Assert.Single(menu.Answers(borin));
        Assert.Equal((true, false), (toBorin.InParty, toBorin.IsLeader));
        Assert.Equal([partyDefault.InstanceId, partyOccupied.InstanceId],
            toBorin.Instances.ConvertAll(e => e.InstanceId));
    }

    /// <summary>
    /// #1104: the menu asks the portal what an entry would: a town has one shared instance and nothing to choose, and a
    /// character away from the portal or outside the map's level band is refused as its entry would be. The answer is
    /// the result and no instances; nothing is listed, and nothing is counted as a refused portal entry, since nobody
    /// tried to enter.
    /// </summary>
    [Theory]
    [InlineData("town target", MapTransitionResult.NotInstanced)]
    [InlineData("not near portal", MapTransitionResult.NotNearPortal)]
    [InlineData("level too low", MapTransitionResult.LevelTooLow)]
    public void Answer_a_failure_where_the_portal_refuses(string case_, MapTransitionResult expected)
    {
        using var menu = new Menu();
        PartyClient kaela = menu.Join(7, "Kaela");
        switch (case_)
        {
            case "town target":
                menu.Target.MapType = MapType.Town;
                break;
            case "not near portal":
                kaela.Character.Position = new Vector3(6f, 0f, 0f); // the portal reaches 5 m
                break;
            default:
                menu.Target.MinLevel = (ushort)(kaela.Character.Level + 1);
                break;
        }

        using var counted = new RefusalProbe();
        menu.Request(kaela);

        SInstanceListPacket answer = Assert.Single(menu.Answers(kaela));
        Assert.Equal((ForestMap, expected), (answer.TargetMapId, answer.Result));
        Assert.Empty(answer.Instances);
        menu.World.InstanceChoices.DidNotReceiveWithAnyArgs().ListChoosable(default, default, default!, default!);
        Assert.Empty(counted.Reasons);
    }

    /// <summary>
    /// Characters in a town instance, on its portal to the forest (map 2), and a world whose instance choices list what
    /// the test says. The characters, the instances and the parties share one clock.
    /// </summary>
    private sealed class Menu : IDisposable
    {
        private readonly List<MapInstance> _instances = [];
        private readonly InstanceListHandler _handler;

        public Menu(int abandonedLifetimeMinutes = 15)
        {
            Parties = new PartyTestWorld(config => config.AbandonedInstanceLifetimeMinutes = abandonedLifetimeMinutes);
            Town = TestMapInstances.Build(MapInstanceClients.NewWorld(), mapType: MapType.Town, time: Clock,
                templateId: new MapTemplateId(TownMap));
            Town.AddPortal(new PortalInstance(new ObjectGuid(ObjectType.Portal, 1), Vector3.zero, 5f, ForestMap, 1));
            _instances.Add(Town);

            World.Configuration.Returns(Parties.Config);
            World.MapTemplates.Returns(new List<MapTemplate>
            {
                new() { Id = new MapTemplateId(TownMap), MapType = MapType.Town, Name = "town", Description = "" },
                Target,
            });
            World.InstanceRegistry.GetInstanceById(Town.InstanceId).Returns(Town);

            _handler = new InstanceListHandler(World, Clock, Parties.Parties);
        }

        public PartyTestWorld Parties { get; }
        public ManualTimerClock Clock => Parties.Clock;
        public IWorld World { get; } = Substitute.For<IWorld>();
        public MapInstance Town { get; }

        /// <summary>The forest, the map the portal leads to.</summary>
        public MapTemplate Target { get; } =
            new() { Id = new MapTemplateId(ForestMap), MapType = MapType.Normal, Name = "forest", Description = "" };

        /// <summary>Character <paramref name="id" /> in the game, in the town, on the portal.</summary>
        public PartyClient Join(uint id, string name)
        {
            PartyClient client = Parties.Online(id, name, instance: Town.InstanceId, time: Clock);
            client.Character.Position = Vector3.zero;
            client.Connection.InGame.Returns(true);
            return client;
        }

        /// <summary>A live instance of the forest, built now.</summary>
        public MapInstance Forest(uint? ownerCharacterId = null, PartyId? ownerPartyId = null)
        {
            MapInstance instance = TestMapInstances.Build(MapInstanceClients.NewWorld(), time: Clock,
                templateId: Target.Id, ownerCharacterId: ownerCharacterId, ownerPartyId: ownerPartyId);
            _instances.Add(instance);
            return instance;
        }

        /// <summary>What the instance choices list for the character, in its party, for the forest.</summary>
        public void Lists(uint characterId, PartyId party, params InstanceChoice[] choices) =>
            World.InstanceChoices
                .When(c => c.ListChoosable(Arg.Is(characterId), Arg.Is<PartyId?>(p => party.Equals(p)),
                    Arg.Is(Target.Id), Arg.Any<List<InstanceChoice>>()))
                .Do(call => call.Arg<List<InstanceChoice>>().AddRange(choices));

        public void Request(PartyClient client) =>
            _handler.Execute(client.Connection, new CInstanceListPacket { TargetMapId = ForestMap });

        public List<SInstanceListPacket> Answers(PartyClient client) =>
            client.Read<SInstanceListPacket>(NetworkPacketType.SMSG_INSTANCE_LIST);

        public void Dispose()
        {
            foreach (MapInstance instance in _instances)
                instance.Dispose();
        }
    }
}
