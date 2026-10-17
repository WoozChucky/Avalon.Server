using System.Reflection;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.State;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Quests;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Doodads;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Maps;
using Avalon.World.Quests;
using Avalon.World.Scripts;
using Avalon.World.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

using TransferMaps = Avalon.Server.World.UnitTests.Instances.InstanceTransferReplicationShould.Maps;
using WireDoodadState = Avalon.Network.Packets.State.DoodadState;
using WorldDoodadState = Avalon.World.Doodads.DoodadState;

namespace Avalon.Server.World.UnitTests.Doodads;

/// <summary>
/// Doodads are sent to each viewer by its own rules: a doodad's visibility gate decides who is told it exists, its
/// usability gate what each one is told about using it, and both are asked of the viewer's quest log. A gated doodad
/// sent to a viewer its gate refuses shows a quest object to a player who has no business with it, so every test drives
/// a real instance with two viewers, which turns the shared entry encoding on (StatePacketAssembler.MinRecipientsToShare).
/// Character ids 104_0xx and quest 900 are this class's.
/// </summary>
public class DoodadReplicationShould
{
    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);

    private const uint Quest = 900, Before = 9000, Objective = 9001;

    // Visible to, or usable by, a viewer on quest 900's objective 9001, the quest's second stage, and only until it
    // reaches its count of 2.
    private static readonly DoodadGate s_onObjective = new(Quest, DoodadGateCondition.OnObjective, Objective);

    [Theory]
    [InlineData("template gate")]
    [InlineData("spawn gate")]
    public async Task Send_a_doodad_only_to_the_viewers_its_rules_allow(string rule)
    {
        StaticData data = await Data();
        TransferMaps maps = await TransferMaps.CreateAsync(NewWorld(data));
        MapInstance town = maps.Town;
        Doodad doodad = rule == "template gate"
            ? Place(town, 1, Template(visible: s_onObjective))
            : Place(town, 1, Template(), visibleGate: s_onObjective);
        MapInstanceClient a = JoinAt(town, 104_001, Vector3.zero);
        MapInstanceClient b = JoinAt(town, 104_002, new Vector3(20f, 0f, 0f));
        ActiveQuest held = OnTheObjective(a.Character);

        Ticks(town, 30);

        ObjectState added = Assert.Single(a.Added(), s => s.Guid == doodad.Guid.RawValue);
        Assert.Equal(doodad.Template.Id.Value, added.DoodadTemplateId);
        Assert.Equal(WireDoodadState.Ready, added.DoodadState);
        Assert.DoesNotContain(b.Added(), s => s.Guid == doodad.Guid.RawValue);

        // The objective's count reached: the gate closes for A at its next decision, and B was never told.
        a.Character.Quests.SetProgress(held, Objective, 2);
        Ticks(town, 6);

        Assert.Contains(doodad.Guid.RawValue, a.Removed());
        Assert.DoesNotContain(b.Added(), s => s.Guid == doodad.Guid.RawValue);

        // A move into the same instance starts A's view over: its gate still closed, it is not told again.
        a.Sent.Clear();
        maps.World.TransferPlayer(a.Connection, town);
        Ticks(town, 30);

        Assert.Single(a.Added(), s => s.Guid == a.Character.Guid.RawValue);
        Assert.DoesNotContain(a.Added(), s => s.Guid == doodad.Guid.RawValue);
        Assert.DoesNotContain(b.Added(), s => s.Guid == doodad.Guid.RawValue);
    }

    [Fact]
    public async Task Tell_each_viewer_whether_it_can_use_a_doodad()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld(await Data()));
        Doodad doodad = Place(instance, 2, Template(usable: s_onObjective));
        MapInstanceClient a = JoinAt(instance, 104_011, Vector3.zero);
        MapInstanceClient b = JoinAt(instance, 104_012, new Vector3(20f, 0f, 0f));
        OnTheObjective(a.Character);

        Ticks(instance, 30);

        Assert.True(Assert.Single(a.Added(), s => s.Guid == doodad.Guid.RawValue).CanInteract);
        // An add tells a viewer only that it can use the doodad: one that cannot is told nothing of it.
        Assert.Null(Assert.Single(b.Added(), s => s.Guid == doodad.Guid.RawValue).CanInteract);

        // B accepts the quest (its first stage, not the objective's yet), then reaches the objective's stage.
        ActiveQuest accepted = b.Character.Quests.Start(Quest, DateTime.UnixEpoch);
        Ticks(instance, 6);
        b.Character.Quests.SetStage(accepted, 1);
        Ticks(instance, 6);

        ObjectState update = Assert.Single(b.StateUpdates(), s => s.Guid == doodad.Guid.RawValue);
        Assert.True(update.CanInteract);
        Assert.Null(update.DoodadState);
        Assert.DoesNotContain(a.StateUpdates(), s => s.Guid == doodad.Guid.RawValue);
    }

    [Fact]
    public async Task Send_a_spent_doodad_as_used_and_a_despawned_one_as_removed()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld(await Data()));
        Doodad doodad = Place(instance, 3, Template());
        MapInstanceClient[] viewers =
            [JoinAt(instance, 104_021, Vector3.zero), JoinAt(instance, 104_022, new Vector3(20f, 0f, 0f))];
        Ticks(instance, 30);
        Assert.All(viewers, v => Assert.True(Assert.Single(v.Added(), s => s.Guid == doodad.Guid.RawValue).CanInteract));

        Change(instance, doodad, WorldDoodadState.Used);
        Ticks(instance, 6);

        foreach (MapInstanceClient viewer in viewers)
        {
            ObjectState used = Assert.Single(viewer.StateUpdates(), s => s.Guid == doodad.Guid.RawValue);
            Assert.Equal(WireDoodadState.Used, used.DoodadState);
            // An update tells a viewer the doodad can no longer be used, false on the wire rather than left out.
            Assert.False(Assert.IsType<bool>(used.CanInteract));
        }

        Change(instance, doodad, WorldDoodadState.Despawned);
        Ticks(instance, 6);

        Assert.All(viewers, v => Assert.Single(v.Removed(), guid => guid == doodad.Guid.RawValue));
        Assert.All(viewers, v => Assert.Single(v.StateUpdates(), s => s.Guid == doodad.Guid.RawValue));
    }

    [Fact]
    public async Task Decide_views_over_unchanged_gated_doodads_without_allocating()
    {
        (MapInstance instance, QuietConnection[] viewers) = QuietInstance(await Data());
        var doodads = new List<Doodad>();
        for (uint i = 0; i < 20; i++)
            doodads.Add(Place(instance, 100 + i, Template(visible: s_onObjective), new Vector3(i % 5 * 4f, 0f, i / 5 * 4f)));
        OnTheObjective((CharacterEntity)viewers[0].Character!);

        Ticks(instance, 60);
        long before = ThreadAllocations.BeforeQuietWindow();
        Ticks(instance, 60);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        CharacterCharacterGameState seeing = ((CharacterEntity)viewers[0].Character!).CharacterGameState;
        CharacterCharacterGameState refused = ((CharacterEntity)viewers[1].Character!).CharacterGameState;
        Assert.All(doodads, d => Assert.True(seeing.Knows(d.Guid)));
        Assert.All(doodads, d => Assert.False(refused.Knows(d.Guid)));
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    /// <summary>The quest fixtures and quest 900: a first stage, then the stage of objective 9001, two of it.</summary>
    private static async Task<StaticData> Data() =>
        (await QuestTestWorld.CreateAsync(quests:
            [QuestTestData.Quest(Quest).WithStage(0, Kill(Before, Boar, 1)).WithStage(1, Kill(Objective, Boar, 2))])).Data;

    /// <summary>Accepts quest 900 and moves it to the stage of objective 9001, with none of it done.</summary>
    private static ActiveQuest OnTheObjective(CharacterEntity character)
    {
        ActiveQuest quest = character.Quests.Start(Quest, DateTime.UnixEpoch);
        character.Quests.SetStage(quest, 1);
        return quest;
    }

    private static DoodadTemplateView Template(DoodadGate? visible = null, DoodadGate? usable = null) => new(
        new DoodadTemplateId(104), "Glimmer", "props-kit", "Crate", null, null, null, null,
        DoodadInteraction.Click, 0, 5f, 0f, DoodadUseLimit.OncePerInstance, DoodadSpentLook.ShowUsed, null, null,
        null, 0, 0, 0, DoodadCollision.None, 0f, 0f, 0f, visible, usable, [], 0, ScriptCanSee: false);

    /// <summary>A doodad 10 m from the origin, unless placed elsewhere, as an instance build places it.</summary>
    private static Doodad Place(MapInstance instance, uint id, DoodadTemplateView template, Vector3? position = null,
        DoodadGate? visibleGate = null)
    {
        var doodad = new Doodad(new ObjectGuid(ObjectType.Doodad, id), template, position ?? new Vector3(10f, 0f, 0f),
            0f, visibleGate);
        instance.AddDoodad(doodad);
        return doodad;
    }

    private static MapInstanceClient JoinAt(MapInstance instance, uint id, Vector3 position)
    {
        CharacterEntity character = TestCharacters.New(id);
        character.Position = position;
        return Join(instance, character);
    }

    /// <summary>The doodad's state changed on the tick, as a use spends it: the state written, the instance told.</summary>
    private static void Change(MapInstance instance, Doodad doodad, WorldDoodadState state)
    {
        typeof(Doodad).GetProperty(nameof(Doodad.State))!.SetValue(doodad, state);
        typeof(MapInstance).GetMethod("DoodadChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(instance, [doodad]);
    }

    /// <summary>An instance over a world and connections that are no substitutes, which allocate on every call.</summary>
    private static (MapInstance Instance, QuietConnection[] Viewers) QuietInstance(StaticData data)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new CombatConfig());
        services.AddSingleton<IChunkLibrary>(new AnyChunkLibrary());
        services.AddSingleton(Substitute.For<IScriptManager>());
        services.AddSingleton(new DoodadVisibility());

        var entryChunk = new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero);
        var layout = new ChunkLayout(Seed: 0, Chunks: [entryChunk], EntryChunk: entryChunk, BossChunk: null,
            Portals: [], EntrySpawnWorldPos: Vector3.zero, CellSize: 30f, Config: null);
        var instance = new MapInstance(NullLoggerFactory.Instance, services.BuildServiceProvider(), new QuietWorld(data),
            new MapTemplateId(1), ownerCharacterId: null, layout, Substitute.For<IMapNavigator>(), seed: 0);

        var viewers = new QuietConnection[2];
        for (int i = 0; i < viewers.Length; i++)
        {
            CharacterEntity character = TestCharacters.New((uint)(104_031 + i));
            character.Spells.Load(Array.Empty<IAbility>());
            character.Position = new Vector3(i * 2f, 0f, 0f);
            viewers[i] = new QuietConnection(character);
            instance.AddCharacter(viewers[i]);
        }

        return (instance, viewers);
    }

    private static void Ticks(MapInstance instance, int count)
    {
        for (int i = 0; i < count; i++)
            instance.Update(s_tick);
    }
}
