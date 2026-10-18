using System.Diagnostics.Metrics;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.Telemetry;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;
using Avalon.Network.Packets.State;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Parties;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Maps;
using Avalon.World.Parties;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using Avalon.World.Scripts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Handlers;

public class EnterMapHandlerShould
{
    private const ushort TownMap = 1;
    private const ushort TargetMap = 2;

    /// <summary>
    /// A connection kicked by a select of its character on another connection has released it
    /// before the target instance is ready. The continuation still runs, and must not dereference
    /// the character it no longer holds, nor transfer an entity that has already left.
    /// </summary>
    [Fact]
    public void Do_nothing_when_the_character_has_left_the_connection_before_the_target_instance_is_ready()
    {
        CharacterEntity character = New(7);
        MapInstance source = Source();
        character.InstanceId = source.InstanceId;
        character.Position = Vector3.zero;

        IInstanceRegistry registry = Substitute.For<IInstanceRegistry>();
        registry.GetInstanceById(source.InstanceId).Returns(source);
        IMapInstance target = Substitute.For<IMapInstance>();
        registry.GetOrCreateTownInstanceAsync(Arg.Any<MapTemplateId>(), Arg.Any<ushort>()).Returns(target);

        IWorld world = Substitute.For<IWorld>();
        world.InstanceRegistry.Returns(registry);
        world.MapTemplates.Returns(new List<MapTemplate>
        {
            new() { Id = new MapTemplateId(TargetMap), MapType = MapType.Town, Name = "target", Description = "" }
        });

        IWorldConnection connection = Substitute.For<IWorldConnection, ISettledContinuations>();
        connection.InGame.Returns(true);
        connection.Character.Returns(character);
        Action<Task<IMapInstance>>? onInstance = null;
        ((ISettledContinuations)connection)
            .When(c => c.EnqueueSettled(Arg.Any<Task<IMapInstance>>(), Arg.Any<Action<Task<IMapInstance>>>()))
            .Do(call => onInstance = call.Arg<Action<Task<IMapInstance>>>());

        ICharacterSaver saver = Substitute.For<ICharacterSaver>();
        var handler = new EnterMapHandler(NullLogger<EnterMapHandler>.Instance, saver, world);

        handler.Execute(connection, new CEnterMapPacket { TargetMapId = TargetMap });
        Assert.NotNull(onInstance);

        // The connection cleared the move as the character left it, and the one selected since has a move of its own
        // under way: that flag is not this arrival's to clear.
        connection.Character.Returns((ICharacter?)null);
        connection.RespawnInFlight = true;

        Exception? escaped = Record.Exception(() => onInstance!(Task.FromResult(target)));

        Assert.Null(escaped);
        world.DidNotReceiveWithAnyArgs().TransferPlayer(default!, default!);
        saver.DidNotReceiveWithAnyArgs().Save(default!, default!);
        Assert.True(connection.RespawnInFlight);
    }

    /// <summary>
    /// A scroll return, an item teleport, a respawn or a party return under way decides where the character goes, so a
    /// portal entry meanwhile is answered MoveInProgress before anything is looked up or built.
    /// </summary>
    [Fact]
    public void Refuse_a_portal_entry_while_another_move_is_under_way()
    {
        CharacterEntity character = New(7);
        MapInstance source = Source();
        character.InstanceId = source.InstanceId;
        character.Position = Vector3.zero;

        IInstanceRegistry registry = Substitute.For<IInstanceRegistry>();
        registry.GetInstanceById(source.InstanceId).Returns(source);
        IWorld world = Substitute.For<IWorld>();
        world.InstanceRegistry.Returns(registry);
        world.MapTemplates.Returns(new List<MapTemplate>
        {
            new() { Id = new MapTemplateId(TargetMap), MapType = MapType.Town, Name = "target", Description = "" }
        });

        IWorldConnection connection = Substitute.For<IWorldConnection, ISettledContinuations>();
        connection.InGame.Returns(true);
        var sent = new List<OutboundPacket>();
        TestTown.Record(connection, character, sent);
        connection.RespawnInFlight.Returns(true);

        var handler = new EnterMapHandler(NullLogger<EnterMapHandler>.Instance, Substitute.For<ICharacterSaver>(),
            world);
        handler.Execute(connection, new CEnterMapPacket { TargetMapId = TargetMap });

        Assert.Equal(MapTransitionResult.MoveInProgress,
            Assert.Single(TestTown.Read<SMapTransitionPacket>(sent, NetworkPacketType.SMSG_MAP_TRANSITION)).Result);
        ((ISettledContinuations)connection).DidNotReceiveWithAnyArgs().EnqueueSettled(default(Task<IMapInstance>)!, default!);
        registry.DidNotReceiveWithAnyArgs().GetInstanceById(default);
        _ = world.DidNotReceive().MapTemplates;
    }

    /// <summary>
    /// A request the handler accepts is a move under way until it ends. Continuations run after the packet pass, so
    /// without that a second request in the same pass would pass every check the first one passed and arrive, and
    /// save, once more. Once the first has arrived the move is over, and the next request is taken.
    /// </summary>
    [Fact]
    public void Refuse_a_second_portal_entry_while_the_first_is_under_way_and_take_the_next_once_it_has_arrived()
    {
        var entry = new Entry();

        entry.Request();
        entry.Request(); // in the same pass: the first one's continuation has not run yet
        entry.Flush();

        entry.World.Received(1).TransferPlayer(entry.Connection, entry.Target);
        entry.Saver.Received(1).Save(entry.Connection, entry.Character);
        Assert.Equal([MapTransitionResult.MoveInProgress, MapTransitionResult.Success], entry.Results());
        Assert.False(entry.Connection.RespawnInFlight);

        entry.StandOnThePortal(); // the arrival moved the character to the target
        entry.Request();
        entry.Flush();

        entry.World.Received(2).TransferPlayer(entry.Connection, entry.Target);
        Assert.Equal(MapTransitionResult.Success, entry.Results()[^1]);
    }

    /// <summary>
    /// The character arrives at the target's entry spawn, at the far end of its layout from where it stood, and the
    /// target's spatial index has it there (#982): its own client lists it on its first tick there, and so does a client
    /// standing at the spawn. A position written to the character's row alone moves the character without its index entry,
    /// which stays where it stood, out of reach of every view around the spawn, its own included.
    /// </summary>
    [Fact]
    public void Arrive_where_the_target_s_spatial_index_has_the_character()
    {
        PlacedChunk[] chunks = Enumerable.Range(0, 10)
            .Select(x => new PlacedChunk(new ChunkTemplateId(1), (short)x, 0, 0, new Vector3(x * 30f, 0f, 0f)))
            .ToArray();
        var spawn = new Vector3(285f, 0f, 15f);
        var layout = new ChunkLayout(0, chunks, chunks[0], null, [], spawn, 30f);
        using MapInstance target = TestMapInstances.Build(MapInstanceClients.NewWorld(), layout: layout);
        var entry = new Entry(target: target);
        entry.Character.Spells.Load(Array.Empty<IAbility>());   // the tick updates abilities; an unloaded list throws
        entry.World.When(w => w.TransferPlayer(entry.Connection, target)).Do(_ =>
        {
            entry.Character.InstanceId = target.InstanceId;
            target.AddCharacter(entry.Connection);
        });
        MapInstanceClient there = MapInstanceClients.Join(target, New(8));
        there.Character.Position = spawn;

        entry.Request();
        entry.Flush();
        target.Update(TimeSpan.FromSeconds(1d / 60d));

        Assert.Equal(MapTransitionResult.Success, entry.Results()[^1]);
        Assert.Equal(spawn, entry.Character.Position);
        Assert.Contains(entry.Character.Guid.RawValue, entry.Added());
        Assert.Contains(entry.Character.Guid.RawValue, there.Added().Select(s => s.Guid));
    }

    public enum Ending
    {
        TheBuildFaults,
        TheBuildFaultsAndItsLogLineThrows,
        TheBuildIsCancelled,
        TheRequestForTheInstanceThrows,
        TheInstanceIsReleasedTwice,
        TheInstanceIsReleasedAndTheSecondRequestThrows,
        TheTransferThrows,
        TheCharacterDiesWhileItsInstanceIsBuilt,
        TheBuildFaultsAndTheCharacterDied,
        TheBuildFaultsAndTheCharacterLeft,
    }

    /// <summary>
    /// A portal entry that ends without arriving still ends: the move is no longer under way, so the character's next
    /// portal entry, return to town or teleport is not refused for the rest of its session.
    /// </summary>
    [Theory]
    [InlineData(Ending.TheBuildFaults)]
    [InlineData(Ending.TheBuildFaultsAndItsLogLineThrows)]
    [InlineData(Ending.TheBuildIsCancelled)]
    [InlineData(Ending.TheRequestForTheInstanceThrows)]
    [InlineData(Ending.TheInstanceIsReleasedTwice)]
    [InlineData(Ending.TheInstanceIsReleasedAndTheSecondRequestThrows)]
    [InlineData(Ending.TheTransferThrows)]
    [InlineData(Ending.TheCharacterDiesWhileItsInstanceIsBuilt)]
    [InlineData(Ending.TheBuildFaultsAndTheCharacterDied)]
    [InlineData(Ending.TheBuildFaultsAndTheCharacterLeft)]
    public void Take_a_later_portal_entry_after_one_that_ended_without_arriving(Ending ending)
    {
        // The failed build's Error line is written on the tick, in the arrival, by a logger that rethrows what its
        // providers throw.
        var log = new TestLog();
        var entry = new Entry(log: ending == Ending.TheBuildFaultsAndItsLogLineThrows
            ? new ThrowingLog()
            : new Logger<EnterMapHandler>(log));
        bool failing = true;
        switch (ending)
        {
            case Ending.TheBuildFaults or Ending.TheBuildFaultsAndItsLogLineThrows
                or Ending.TheBuildFaultsAndTheCharacterDied or Ending.TheBuildFaultsAndTheCharacterLeft:
                entry.Registry.GetOrCreateTownInstanceAsync(Arg.Any<MapTemplateId>(), Arg.Any<ushort>()).Returns(
                    Task.FromException<IMapInstance>(new InvalidOperationException("bake failed")),
                    Task.FromResult(entry.Target));
                break;
            case Ending.TheBuildIsCancelled:
                entry.Registry.GetOrCreateTownInstanceAsync(Arg.Any<MapTemplateId>(), Arg.Any<ushort>()).Returns(
                    Task.FromCanceled<IMapInstance>(new CancellationToken(canceled: true)),
                    Task.FromResult(entry.Target));
                break;
            case Ending.TheRequestForTheInstanceThrows:
                entry.Registry.GetOrCreateTownInstanceAsync(Arg.Any<MapTemplateId>(), Arg.Any<ushort>()).Returns(
                    _ => failing ? throw new InvalidOperationException("no registry") : Task.FromResult(entry.Target));
                break;
            case Ending.TheInstanceIsReleasedTwice:
                entry.Registry.GetInstanceById(entry.Target.InstanceId).Returns(_ => failing ? null : entry.Target);
                break;
            case Ending.TheInstanceIsReleasedAndTheSecondRequestThrows:
                // The arrival finds its instance released and asks for it again, and that second ask throws: the
                // move was never handed on, so the arrival that threw is the one that ends it.
                int asked = 0;
                entry.Registry.GetInstanceById(entry.Target.InstanceId).Returns(_ => failing ? null : entry.Target);
                entry.Registry.GetOrCreateTownInstanceAsync(Arg.Any<MapTemplateId>(), Arg.Any<ushort>()).Returns(
                    _ => failing && ++asked == 2
                        ? throw new InvalidOperationException("no registry")
                        : Task.FromResult(entry.Target));
                break;
            case Ending.TheTransferThrows:
                entry.World.When(w => w.TransferPlayer(entry.Connection, entry.Target)).Do(_ =>
                {
                    if (failing)
                        throw new InvalidOperationException("transfer failed");
                });
                break;
        }

        // The dispatch contains what the handler throws, and the continuation drain what an arrival throws.
        Record.Exception(() => entry.Request());
        if (ending is Ending.TheCharacterDiesWhileItsInstanceIsBuilt or Ending.TheBuildFaultsAndTheCharacterDied)
            entry.Character.IsDead = true; // asked alive; dead by the time the instance is ready
        if (ending == Ending.TheBuildFaultsAndTheCharacterLeft)
            entry.Connection.Character.Returns((ICharacter?)null); // a select of it elsewhere took it
        entry.Flush();

        // A failed build is logged whatever became of the character: nobody else logs it.
        if (ending is Ending.TheBuildFaultsAndTheCharacterDied or Ending.TheBuildFaultsAndTheCharacterLeft)
            Assert.Single(log.Errors, e => e.Exception?.InnerException is InvalidOperationException);

        if (ending == Ending.TheBuildFaultsAndTheCharacterLeft)
        {
            // Not this arrival's to clear: the connection cleared it as the character left (ResetCharacterState).
            Assert.True(entry.Connection.RespawnInFlight);
            return;
        }

        if (ending == Ending.TheInstanceIsReleasedTwice)
        {
            // Released once: the arrival resolved the target again, and the move is that second resolve now.
            Assert.True(entry.Connection.RespawnInFlight);
            entry.Flush();
        }

        Assert.DoesNotContain(MapTransitionResult.Success, entry.Results());
        if (ending == Ending.TheCharacterDiesWhileItsInstanceIsBuilt)
            entry.World.DidNotReceiveWithAnyArgs().TransferPlayer(default!, default!);
        Assert.False(entry.Connection.RespawnInFlight);

        failing = false;
        entry.Character.Revive();
        entry.Request();
        entry.Flush();

        Assert.Equal(MapTransitionResult.Success, entry.Results()[^1]);
        Assert.False(entry.Connection.RespawnInFlight);
    }

    /// <summary>
    /// The refusals log at Debug, which a world does not write as shipped, so <c>world.portal_entry.refusals</c> is what
    /// shows them: one count per refusal, under the reason of the place that refused, and none for an entry that arrives.
    /// </summary>
    [Theory]
    [InlineData("dead")]
    [InlineData("move_in_progress")]
    [InlineData("unknown_map")]
    [InlineData("no_instance")]
    [InlineData("no_portal")]
    [InlineData("not_near_portal")]
    [InlineData("level_too_low")]
    [InlineData("level_too_high")]
    [InlineData("instance_released")]
    [InlineData("not_in_party")]
    [InlineData("instance_full")]
    [InlineData("unknown_choice")]
    [InlineData("not_instanced")]
    [InlineData("instance_unavailable")]
    [InlineData("create_not_in_party")]
    [InlineData("not_party_leader")]
    [InlineData(null)]
    public void Count_a_refused_portal_entry_once_under_its_reason(string? reason)
    {
        PartyTestWorld? parties = reason is "not_in_party" or "instance_full" or "not_party_leader"
            ? new PartyTestWorld()
            : null;
        var entry = new Entry(parties: parties);
        ushort map = TargetMap;
        MapEntryChoice choice = MapEntryChoice.Default;
        switch (reason)
        {
            case "dead":
                entry.Character.IsDead = true;
                break;
            case "move_in_progress":
                entry.Connection.RespawnInFlight = true;
                break;
            case "unknown_map":
                map = 99;
                break;
            case "no_instance":
                entry.Character.InstanceId = Guid.NewGuid();
                break;
            case "no_portal":
                map = Entry.MapWithoutPortal;
                break;
            case "not_near_portal":
                entry.Character.Position = new Vector3(40f, 0f, 0f);
                break;
            case "level_too_low":
                entry.Template.MinLevel = (ushort)(entry.Character.Level + 1);
                break;
            case "level_too_high":
                entry.Character.Level = 5;
                entry.Template.MaxLevel = 4;
                break;
            case "instance_released":
                entry.Registry.GetInstanceById(entry.Target.InstanceId).Returns((IMapInstance?)null);
                break;
            case "instance_full":
                entry.Template.MaxPlayers = 1;
                entry.Target.PlayerCount.Returns(1);
                break;
            case "unknown_choice":
                choice = (MapEntryChoice)9;
                break;
            case "not_instanced": // the harness's portal leads to a town
                choice = MapEntryChoice.Existing;
                break;
            case "instance_unavailable":
                entry.Template.MapType = MapType.Normal;
                entry.World.InstanceChoices.FindChoosable(default, default, default!, default)
                    .ReturnsForAnyArgs((IMapInstance?)null);
                choice = MapEntryChoice.Existing;
                break;
            case "create_not_in_party":
                entry.Template.MapType = MapType.Normal;
                choice = MapEntryChoice.NewParty;
                break;
            case "not_party_leader":
                Assert.Equal(PartyResult.Ok, parties!.Parties.Promote(entry.Character.Guid.Id, 8));
                choice = MapEntryChoice.NewParty;
                break;
        }

        using var counted = new RefusalProbe();
        entry.Request(map, choice);
        if (reason == "not_in_party")
            parties!.Parties.Leave(entry.Character.Guid.Id); // the party is gone before its instance is ready
        entry.Flush();
        entry.Flush(); // an instance released before the arrival is resolved again, and refused the second time

        Assert.Equal(reason is null ? [] : [reason], counted.Reasons);
        Assert.Equal(reason is null, entry.Results().Contains(MapTransitionResult.Success));
    }

    /// <summary>
    /// A portal's reach is a distance on the ground: a character beside the portal is near it however far above or
    /// below it stands (the harness's portal is at the origin, with a 5 m reach).
    /// </summary>
    [Theory]
    [InlineData(4.5f, 3f, 0f, true)]    // 4.5 m away on the ground, 5.4 m in a straight line: near
    [InlineData(0f, -2f, 4.9f, true)]
    [InlineData(5.5f, 0f, 0f, false)]   // past the reach on the ground: refused
    public void Measure_a_portal_s_reach_on_the_ground(float x, float y, float z, bool accepted)
    {
        var entry = new Entry();
        entry.Character.Position = new Vector3(x, y, z);

        using var counted = new RefusalProbe();
        entry.Request();
        entry.Flush();
        entry.Flush();

        Assert.Equal(accepted ? [] : ["not_near_portal"], counted.Reasons);
        Assert.Equal(accepted, entry.Results().Contains(MapTransitionResult.Success));
    }

    /// <summary>
    /// #1104: a character with two private instances of the forest picks the older one by id and enters it, and from
    /// then on a plain entry takes it; the new one it opened before was the default once it had arrived there.
    /// </summary>
    [Fact]
    public void Enter_a_picked_instance_of_the_character_s_own_and_make_it_the_default()
    {
        using var portal = new Portal();
        PartyClient kaela = portal.Join(7, "Kaela");
        IMapInstance first = portal.Enter(kaela);

        IMapInstance opened = portal.Enter(kaela, MapEntryChoice.NewPrivate);
        Assert.NotSame(first, opened);
        Assert.Same(opened, portal.DefaultOf(kaela));

        Assert.Same(first, portal.Enter(kaela, MapEntryChoice.Existing, first.InstanceId));
        Assert.Same(first, portal.DefaultOf(kaela));
        Assert.Same(first, portal.Enter(kaela));
        Assert.Equal(2, portal.Builds); // the pick built nothing
    }

    /// <summary>
    /// #1104: an instance picked by id is entered only if it is live, of the portal's map, and the character's own or
    /// its current party's. Whatever else the id names, the answer is the same, and nothing is moved, saved or built: a
    /// pick released before the arrival is not resolved again to another instance.
    /// </summary>
    [Theory]
    [InlineData("other character's private")]
    [InlineData("other party's")]
    [InlineData("former party's")]
    [InlineData("expired")]
    [InlineData("unknown id")]
    [InlineData("released before the arrival")]
    public void Refuse_a_picked_instance_the_character_may_not_enter(string case_)
    {
        using var portal = new Portal();
        PartyClient kaela = portal.Join(7, "Kaela");
        PartyClient borin = portal.Join(8, "Borin");
        PartyClient mira = portal.Join(9, "Mira");
        Guid asked;
        switch (case_)
        {
            case "other character's private":
                asked = portal.Enter(borin).InstanceId;
                break;
            case "other party's":
                portal.Parties.Form(borin, mira);
                asked = portal.Enter(borin).InstanceId;
                break;
            case "former party's":
                portal.Parties.Form(borin, kaela, mira);
                asked = portal.Enter(borin).InstanceId;
                Assert.Equal(PartyResult.Ok, portal.Parties.Parties.Leave(kaela.Id)); // the party goes on without her
                break;
            case "expired":
                asked = portal.Enter(kaela).InstanceId;
                portal.StandOnThePortal(kaela); // empty from here
                portal.Clock.Advance(Portal.Lifetime); // expired, not yet freed
                break;
            case "unknown id":
                portal.Enter(kaela);
                asked = Guid.NewGuid();
                break;
            default: // her own, freed between the pick and the arrival
                asked = portal.Enter(kaela).InstanceId;
                break;
        }

        portal.StandOnThePortal(kaela);
        portal.Saver.ClearReceivedCalls();
        int builds = portal.Builds;
        portal.Request(kaela, MapEntryChoice.Existing, asked);
        if (case_ == "released before the arrival")
            portal.Registry.RemoveInstance(asked);
        portal.Settle();

        Assert.Equal(MapTransitionResult.InstanceUnavailable, portal.Results(kaela)[^1]);
        Assert.Equal(portal.Town.InstanceId, kaela.Character.InstanceId);
        portal.Saver.DidNotReceiveWithAnyArgs().Save(default!, default!);
        Assert.Equal(builds, portal.Builds);
        Assert.False(kaela.Connection.RespawnInFlight);
    }

    /// <summary>
    /// #1104: a new instance may be opened once every Game:InstanceCreateCooldownSeconds (10): a second create inside
    /// the window is refused, with the time left as a system line, a plain entry meanwhile is not held back, and the
    /// refused create did not move the window's end.
    /// </summary>
    [Fact]
    public void Refuse_a_second_create_inside_the_cooldown_and_take_it_at_its_end()
    {
        using var portal = new Portal();
        PartyClient kaela = portal.Join(7, "Kaela");
        IMapInstance opened = portal.Enter(kaela, MapEntryChoice.NewPrivate);

        portal.Clock.Advance(TimeSpan.FromSeconds(9.9));
        portal.StandOnThePortal(kaela);
        kaela.Clear();
        using (var counted = new RefusalProbe())
        {
            portal.Request(kaela, MapEntryChoice.NewPrivate);
            portal.Settle();
            Assert.Equal(["create_on_cooldown"], counted.Reasons);
        }

        Assert.Equal([MapTransitionResult.CreateOnCooldown], portal.Results(kaela));
        Assert.Single(kaela.Lines());
        Assert.Same(opened, portal.Enter(kaela));
        Assert.Equal(1, portal.Builds);

        portal.Clock.Advance(TimeSpan.FromSeconds(0.1)); // 10 s after the first create
        Assert.NotSame(opened, portal.Enter(kaela, MapEntryChoice.NewPrivate));
    }

    /// <summary>
    /// #1104: a new party instance is the leader's to open, and a choice is for a Normal map only (a town has one
    /// shared instance); an unknown choice is a bad request. Each is refused before anything is built or moved.
    /// </summary>
    [Theory]
    [InlineData(MapEntryChoice.NewParty, MapType.Normal, false, MapTransitionResult.NotInParty)]
    [InlineData(MapEntryChoice.NewParty, MapType.Normal, true, MapTransitionResult.NotPartyLeader)]
    [InlineData(MapEntryChoice.Existing, MapType.Town, false, MapTransitionResult.NotInstanced)]
    [InlineData((MapEntryChoice)9, MapType.Normal, false, MapTransitionResult.MapNotFound)]
    public void Refuse_a_choice_the_character_may_not_make(MapEntryChoice choice, MapType target, bool inParty,
        MapTransitionResult expected)
    {
        using var portal = new Portal(target);
        PartyClient kaela = portal.Join(7, "Kaela");
        if (inParty)
            portal.Parties.Form(portal.Join(8, "Borin"), kaela); // led by Borin

        portal.Request(kaela, choice);
        portal.Settle();

        Assert.Equal([expected], portal.Results(kaela));
        Assert.Equal(portal.Town.InstanceId, kaela.Character.InstanceId);
        Assert.Equal(0, portal.Builds);
        Assert.False(kaela.Connection.RespawnInFlight);
    }

    /// <summary>
    /// #1104: while the leader's new party instance builds, a member's plain entry still takes the party's current
    /// one. Once the leader has arrived the new one is the party's default, every other online member is told, and the
    /// members inside the old one stay there; a member who goes back to the old one by id leaves the default alone.
    /// </summary>
    [Fact]
    public void Move_the_party_default_and_tell_the_others_once_the_leader_arrives_in_its_new_instance()
    {
        using var portal = new Portal();
        PartyClient kaela = portal.Join(1, "Kaela");
        PartyClient borin = portal.Join(2, "Borin");
        PartyClient mira = portal.Join(3, "Mira");
        PartyClient tovin = portal.Join(4, "Tovin");
        portal.Parties.Form(kaela, borin, mira, tovin);
        IMapInstance old = portal.Enter(borin);

        portal.Request(kaela, MapEntryChoice.NewParty);
        portal.Request(mira);
        portal.Flush(); // the leader's instance is still building
        Assert.Equal(old.InstanceId, mira.Character.InstanceId);
        Assert.All(new[] { kaela, borin, mira }, c => Assert.Empty(c.Lines()));

        portal.Settle();
        IMapInstance opened = portal.Registry.GetInstanceById(kaela.Character.InstanceId)!;
        Assert.NotSame(old, opened);
        Assert.Empty(kaela.Lines());
        Assert.All(new[] { borin, mira, tovin }, c => Assert.Equal(["Kaela opened a new forest."], c.Lines()));
        Assert.Equal(old.InstanceId, borin.Character.InstanceId);
        Assert.Equal(old.InstanceId, mira.Character.InstanceId);

        Assert.Same(opened, portal.Enter(tovin));
        Assert.Same(old, portal.Enter(tovin, MapEntryChoice.Existing, old.InstanceId));
        Assert.Same(opened, portal.DefaultOf(tovin));
    }

    /// <summary>
    /// #1104: a leader who left the party while its new instance built is refused on arrival, as any character no longer
    /// in the party an instance is for; the party's default stays where it was and nobody is told anything.
    /// </summary>
    [Fact]
    public void Leave_the_party_default_alone_when_the_leader_left_the_party_while_its_instance_built()
    {
        using var portal = new Portal();
        PartyClient kaela = portal.Join(1, "Kaela");
        PartyClient borin = portal.Join(2, "Borin");
        PartyClient mira = portal.Join(3, "Mira");
        portal.Parties.Form(kaela, borin, mira);
        IMapInstance old = portal.Enter(borin);

        portal.Request(kaela, MapEntryChoice.NewParty);
        Assert.Equal(PartyResult.Ok, portal.Parties.Parties.Leave(kaela.Id));
        borin.Clear();
        mira.Clear();
        portal.Settle();

        Assert.Equal(MapTransitionResult.MapNotFound, portal.Results(kaela)[^1]);
        Assert.Equal(portal.Town.InstanceId, kaela.Character.InstanceId);
        Assert.Empty(borin.Lines());
        Assert.Empty(mira.Lines());
        Assert.Same(old, portal.Enter(mira));
    }

    /// <summary>
    /// The reasons counted in <c>world.portal_entry.refusals</c> on this test's own flow: the counter is static and
    /// shared, and other test classes refuse portal entries in parallel.
    /// </summary>
    private sealed class RefusalProbe : IDisposable
    {
        private static readonly AsyncLocal<RefusalProbe?> s_current = new();
        private readonly MeterListener _listener = new();

        public RefusalProbe()
        {
            s_current.Value = this;
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter == DiagnosticsConfig.World.Meter && instrument.Name == "world.portal_entry.refusals")
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                if (s_current.Value != this)
                    return;

                foreach (KeyValuePair<string, object?> tag in tags)
                {
                    if (tag.Key == "reason")
                        Reasons.Add((string)tag.Value!);
                }
            });
            _listener.Start();
        }

        public List<string> Reasons { get; } = [];

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>A log whose Error lines throw, as a provider that fails does through the logger that calls it.</summary>
    private sealed class ThrowingLog : ILogger<EnterMapHandler>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                throw new InvalidOperationException("the log provider failed");
        }
    }

    /// <summary>
    /// A character standing on a portal to the town, behind a connection that records what it is sent and queues the
    /// arrivals it is handed for <see cref="Flush" />, which runs them as the connection's drain runs a continuation
    /// queued whatever its task's outcome (<see cref="ISettledContinuations" />), once the packet pass is over: when
    /// the task has ended, succeeded or not, with a callback that throws contained. An arrival handed to the ordinary
    /// queue instead, whose drain drops the callback of a task that failed, is never run here. Given a party world,
    /// the character leads a party of two and the portal leads to a Normal map, so to the party's instance of it.
    /// </summary>
    private sealed class Entry
    {
        private readonly List<(Task<IMapInstance> Task, Action<Task<IMapInstance>> Callback)> _queued = [];
        private readonly List<OutboundPacket> _sent = [];
        private readonly EnterMapHandler _handler;
        private readonly MapInstance _source = Source();

        /// <param name="target">The instance the portal leads to; a substitute when omitted.</param>
        public Entry(ILogger<EnterMapHandler>? log = null, PartyTestWorld? parties = null, MapInstance? target = null)
        {
            StandOnThePortal();

            if (target is null)
                Target.InstanceId.Returns(Guid.NewGuid());
            else
                Target = target;
            Registry.GetInstanceById(_source.InstanceId).Returns(_source);
            Registry.GetInstanceById(Target.InstanceId).Returns(Target);
            Registry.GetOrCreateTownInstanceAsync(Arg.Any<MapTemplateId>(), Arg.Any<ushort>()).Returns(Task.FromResult(Target));

            World.InstanceRegistry.Returns(Registry);
            World.MapTemplates.Returns(new List<MapTemplate>
            {
                Template,
                new() { Id = new MapTemplateId(MapWithoutPortal), MapType = MapType.Town, Name = "elsewhere", Description = "" },
            });

            Connection.InGame.Returns(true);
            TestTown.Record(Connection, Character, _sent);
            ((ISettledContinuations)Connection)
                .When(c => c.EnqueueSettled(Arg.Any<Task<IMapInstance>>(), Arg.Any<Action<Task<IMapInstance>>>()))
                .Do(call => _queued.Add((call.Arg<Task<IMapInstance>>(), call.Arg<Action<Task<IMapInstance>>>())));

            if (parties is not null)
            {
                Connection.AccountId.Returns(new AccountId(7));
                parties.Parties.CharacterOnline(Connection);
                PartyClient other = parties.Online(8);
                Assert.Equal(PartyResult.Ok, parties.Parties.Invite(Character.Guid.Id, other.Character.Name));
                Assert.Equal(PartyResult.Ok, parties.Parties.Respond(other.Id, accept: true));
                _sent.Clear();

                IPartyInstanceRegistry partyInstances = Substitute.For<IPartyInstanceRegistry>();
                partyInstances.GetOrCreatePartyInstanceAsync(Arg.Any<PartyId>(), Arg.Any<MapTemplateId>())
                    .Returns(Task.FromResult(Target));
                World.PartyInstances.Returns(partyInstances);
                World.Configuration.Returns(parties.Config);
                Template.MapType = MapType.Normal;
            }

            _handler = new EnterMapHandler(log ?? NullLogger<EnterMapHandler>.Instance, Saver, World, parties?.Parties);
        }

        /// <summary>A map the world has and the source instance has no portal to.</summary>
        public const ushort MapWithoutPortal = 3;

        public CharacterEntity Character { get; } = New(7);

        /// <summary>The town the portal leads to.</summary>
        public MapTemplate Template { get; } =
            new() { Id = new MapTemplateId(TargetMap), MapType = MapType.Town, Name = "target", Description = "" };
        public IMapInstance Target { get; } = Substitute.For<IMapInstance>();

        /// <summary>The guid of every object the connection was told is in view, in order.</summary>
        public List<ulong> Added() => TestTown.Read<SInstanceStateAddPacket>(_sent, NetworkPacketType.SMSG_WORLD_STATE_ADD)
            .SelectMany(p => p.Adds ?? [])
            .Select(s => s.Guid)
            .ToList();
        public IInstanceRegistry Registry { get; } = Substitute.For<IInstanceRegistry>();
        public IWorld World { get; } = Substitute.For<IWorld>();
        public IWorldConnection Connection { get; } = Substitute.For<IWorldConnection, ISettledContinuations>();
        public ICharacterSaver Saver { get; } = Substitute.For<ICharacterSaver>();

        public void Request(ushort map = TargetMap, MapEntryChoice choice = MapEntryChoice.Default) =>
            _handler.Execute(Connection, new CEnterMapPacket { TargetMapId = map, Choice = choice });

        /// <summary>Puts the character in the source instance, on its portal to the town.</summary>
        public void StandOnThePortal()
        {
            Character.InstanceId = _source.InstanceId;
            Character.Position = Vector3.zero;
        }

        /// <summary>Runs the continuations queued so far, as one tick's flush; any they queue wait for the next call.</summary>
        public void Flush()
        {
            List<(Task<IMapInstance> Task, Action<Task<IMapInstance>> Callback)> due = [.. _queued];
            _queued.Clear();
            foreach ((Task<IMapInstance> task, Action<Task<IMapInstance>> callback) in due)
            {
                if (task.IsCompleted)
                    Record.Exception(() => callback(task));
                else
                    _queued.Add((task, callback));
            }
        }

        /// <summary>The result of every transition the connection was sent, in order.</summary>
        public List<MapTransitionResult> Results() =>
            TestTown.Read<SMapTransitionPacket>(_sent, NetworkPacketType.SMSG_MAP_TRANSITION).ConvertAll(t => t.Result);
    }

    /// <summary>
    /// #1104: characters on the portal to the forest (map 2) in a real town instance, over the world's real registry,
    /// whose builds finish when <see cref="Publish" /> says, a real party service, and a world whose transfer moves
    /// instance membership as World.TransferPlayer does. The characters, the instances and the parties share one clock.
    /// </summary>
    private sealed class Portal : IDisposable
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
        private static readonly MapTemplateId s_forest = new(TargetMap);

        private readonly List<(TaskCompletionSource<MapInstance> Build, MapTemplateId Template, uint? Character, PartyId? Party)> _builds = [];
        private readonly List<MapInstance> _instances = [];
        private readonly List<(Task<IMapInstance> Task, Action<Task<IMapInstance>> Callback)> _queued = [];
        private readonly EnterMapHandler _handler;

        /// <param name="forestType">The type of the map the portal leads to.</param>
        public Portal(MapType forestType = MapType.Normal)
        {
            var town = new MapTemplate { Id = new MapTemplateId(TownMap), MapType = MapType.Town, Name = "town", Description = "" };
            var forest = new MapTemplate { Id = s_forest, MapType = forestType, Name = "forest", Description = "" };
            IAvalonMapManager maps = Substitute.For<IAvalonMapManager>();
            maps.Templates.Returns([town, forest]);

            IChunkLayoutInstanceFactory factory = Substitute.For<IChunkLayoutInstanceFactory>();
            factory.BuildAsync(default!, default, default, default).ReturnsForAnyArgs(call =>
            {
                if (call.ArgAt<MapTemplate>(0).Id == town.Id)
                    return Task.FromResult(Town);

                var build = new TaskCompletionSource<MapInstance>(); // inline, so a finished build is queued at once
                _builds.Add((build, call.ArgAt<MapTemplate>(0).Id, call.ArgAt<uint?>(1), call.ArgAt<PartyId?>(3)));
                return build.Task;
            });
            Registry = new InstanceRegistry(NullLoggerFactory.Instance, maps, factory, abandonedInstanceLifetime: Lifetime,
                time: Clock);
            Parties.Parties.AttachInstances(Registry);
            _ = Registry.GetOrCreateTownInstanceAsync(town.Id, 30);
            Registry.PublishFinished();

            World.InstanceRegistry.Returns(Registry);
            World.PartyInstances.Returns(Registry);
            World.InstanceChoices.Returns(Registry);
            World.Configuration.Returns(Parties.Config);
            World.MapTemplates.Returns(new List<MapTemplate> { town, forest });
            World.When(w => w.TransferPlayer(Arg.Any<IWorldConnection>(), Arg.Any<IMapInstance>()))
                .Do(call => Move(call.ArgAt<IWorldConnection>(0), (MapInstance)call.ArgAt<IMapInstance>(1)));

            _handler = new EnterMapHandler(NullLogger<EnterMapHandler>.Instance, Saver, World, Parties.Parties);
        }

        public PartyTestWorld Parties { get; } = new();
        public ManualTimerClock Clock => Parties.Clock;
        public InstanceRegistry Registry { get; }
        public IWorld World { get; } = Substitute.For<IWorld>();
        public ICharacterSaver Saver { get; } = Substitute.For<ICharacterSaver>();

        /// <summary>The town every character joins in, standing on its portal to the forest.</summary>
        public MapInstance Town { get; } = Source();

        /// <summary>How many forest builds were started.</summary>
        public int Builds => _builds.Count;

        /// <summary>Character <paramref name="id" /> online, in the town, on the portal.</summary>
        public PartyClient Join(uint id, string name)
        {
            PartyClient client = Parties.Online(id, name, instance: Town.InstanceId, time: Clock);
            client.Character.Spells.Load(Array.Empty<IAbility>()); // the tick updates abilities; an unloaded list throws
            client.Character.Position = Vector3.zero;
            client.Connection.InGame.Returns(true);
            Town.AddCharacter(client.Connection);
            ((ISettledContinuations)client.Connection)
                .When(c => c.EnqueueSettled(Arg.Any<Task<IMapInstance>>(), Arg.Any<Action<Task<IMapInstance>>>()))
                .Do(call => _queued.Add((call.Arg<Task<IMapInstance>>(), call.Arg<Action<Task<IMapInstance>>>())));
            return client;
        }

        public void Request(PartyClient client, MapEntryChoice choice = MapEntryChoice.Default, Guid instanceId = default) =>
            _handler.Execute(client.Connection,
                new CEnterMapPacket { TargetMapId = TargetMap, Choice = choice, InstanceId = instanceId });

        /// <summary>From the portal, a request carried through to its arrival, which must succeed; the instance entered.</summary>
        public IMapInstance Enter(PartyClient client, MapEntryChoice choice = MapEntryChoice.Default, Guid instanceId = default)
        {
            StandOnThePortal(client);
            Request(client, choice, instanceId);
            Settle();
            Assert.Equal(MapTransitionResult.Success, Results(client)[^1]);
            return Registry.GetInstanceById(client.Character.InstanceId)!;
        }

        /// <summary>Back in the town, on the portal.</summary>
        public void StandOnThePortal(PartyClient client)
        {
            if (client.Character.InstanceId != Town.InstanceId)
                Move(client.Connection, Town);
            client.Character.Position = Vector3.zero;
        }

        /// <summary>The instance a plain entry of the character takes: its party's default in a party, its own otherwise.</summary>
        public IMapInstance DefaultOf(PartyClient client)
        {
            var listed = new List<InstanceChoice>();
            Registry.ListChoosable(client.Id, Parties.Parties.PartyOf(client.Id)?.Id, s_forest, listed);
            return Assert.Single(listed, c => c.IsDefault).Instance;
        }

        /// <summary>One tick: the finished builds published, then the connections' flush.</summary>
        public void Settle()
        {
            Publish();
            Flush();
        }

        /// <summary>Finishes every forest build still running and publishes it, as the tick does first (#639).</summary>
        public void Publish()
        {
            foreach ((TaskCompletionSource<MapInstance> build, MapTemplateId template, uint? character, PartyId? party) in _builds)
            {
                if (build.Task.IsCompleted)
                    continue;

                MapInstance instance = TestMapInstances.Build(MapInstanceClients.NewWorld(), time: Clock,
                    ownerPartyId: party, templateId: template, ownerCharacterId: character);
                _instances.Add(instance);
                build.SetResult(instance);
            }

            Registry.PublishFinished();
        }

        /// <summary>Runs the arrivals whose instance request has ended, as the connections' flush does; the rest wait.</summary>
        public void Flush()
        {
            List<(Task<IMapInstance> Task, Action<Task<IMapInstance>> Callback)> due = [.. _queued];
            _queued.Clear();
            foreach ((Task<IMapInstance> task, Action<Task<IMapInstance>> callback) in due)
            {
                if (task.IsCompleted)
                    callback(task);
                else
                    _queued.Add((task, callback));
            }
        }

        public List<MapTransitionResult> Results(PartyClient client) =>
            client.Read<SMapTransitionPacket>(NetworkPacketType.SMSG_MAP_TRANSITION).ConvertAll(t => t.Result);

        public void Dispose()
        {
            foreach (MapInstance instance in _instances)
                instance.Dispose();
            Town.Dispose();
        }

        /// <summary>What World.TransferPlayer does to instance membership.</summary>
        private void Move(IWorldConnection connection, MapInstance to)
        {
            var character = (CharacterEntity)connection.Character!;
            (Registry.GetInstanceById(character.InstanceId) as MapInstance)?.RemoveCharacter(connection);
            character.InstanceId = to.InstanceId;
            to.AddCharacter(connection);
        }
    }

    /// <summary>A real instance with a layout and a portal to the target map at the origin, so Execute reaches the continuation.</summary>
    private static MapInstance Source()
    {
        IServiceProvider serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IScriptManager)).Returns(Substitute.For<IScriptManager>());
        serviceProvider.GetService(typeof(CombatConfig)).Returns(new CombatConfig());
        serviceProvider.GetService(typeof(IChunkLibrary)).Returns(new AnyChunkLibrary());

        IWorld world = Substitute.For<IWorld>();
        world.Configuration.Returns(new GameConfiguration());

        var entryChunk = new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero);
        var layout = new ChunkLayout(Seed: 0, Chunks: [entryChunk], EntryChunk: entryChunk, BossChunk: null,
            Portals: [], EntrySpawnWorldPos: Vector3.zero, CellSize: 30f, Config: null);

        var source = new MapInstance(NullLoggerFactory.Instance, serviceProvider, world, new MapTemplateId(TownMap),
            ownerCharacterId: null, layout, Substitute.For<IMapNavigator>(), seed: 0);
        source.AddPortal(new PortalInstance(new ObjectGuid(ObjectType.Portal, 1), Vector3.zero, 5f, TargetMap, 1));
        return source;
    }
}
