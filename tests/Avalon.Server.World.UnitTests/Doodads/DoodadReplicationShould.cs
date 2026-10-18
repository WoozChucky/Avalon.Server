using System.Collections;
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
using Avalon.World.Loot;
using Avalon.World.Parties;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Maps;
using Avalon.World.Quests;
using Avalon.World.Scripts;
using Avalon.World.Serialization;
using Avalon.World.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    /// <summary>
    /// A's quest log lets the gate through and B's does not, B standing 1 m from A in A's party, which shares nothing of
    /// what A may see; the script rows decide by the viewer's name, or its level. A
    /// script is asked only on first sight and after the viewer's quests, its level or the doodad moved, never on a steady
    /// view, and a throw from it leaves the gate's answer, held like any other.
    /// </summary>
    [Theory]
    [InlineData("template gate")]
    [InlineData("spawn gate")]
    [InlineData("script hides what the gate shows")]
    [InlineData("script shows what the gate hides")]
    [InlineData("script refresh")]
    [InlineData("script shows from a level")]
    [InlineData("script throws")]
    [InlineData("solid ignores the script")]
    public async Task Send_a_doodad_only_to_the_viewers_its_rules_allow(string rule)
    {
        const uint IdA = 104_001, IdB = 104_002;
        var scriptLog = new TestLog();
        var catalogLog = new TestLog();
        StaticData data = await Data();
        var scripts = new DoodadScripts(new ServiceCollection().BuildServiceProvider(), TimeProvider.System,
            new Logger<DoodadScripts>(scriptLog));
        TransferMaps maps = await TransferMaps.CreateAsync(NewWorld(data), new DoodadVisibility(scripts));
        MapInstance town = maps.Town;

        bool shown = true;
        RuledDoodadScript.Asked = 0;
        RuledDoodadScript.Rule = rule switch
        {
            "script shows from a level" => ctx => ctx.ViewerLevel >= 5 ? true : null,
            "script hides what the gate shows" => ctx => ctx.ViewerName == TestCharacters.NameFor(IdA) ? null : false,
            "script shows what the gate hides" => ctx => ctx.ViewerName == TestCharacters.NameFor(IdB) ? true : null,
            "script refresh" => _ => shown ? null : false,
            "script throws" => _ => throw new InvalidOperationException("CanSee"),
            _ => _ => false,
        };
        Doodad doodad = rule switch
        {
            "template gate" => Place(town, 1, Template(visible: s_onObjective)),
            "spawn gate" => Place(town, 1, Template(), visibleGate: s_onObjective),
            "script hides what the gate shows" or "script refresh" => Place(town, 1, Scripted(catalogLog)),
            "script shows what the gate hides" or "script throws" or "script shows from a level" =>
                Place(town, 1, Scripted(catalogLog, gated: true)),
            _ => Place(town, 1, Scripted(catalogLog, solid: true)),
        };
        bool bSees = rule is "script shows what the gate hides" or "script refresh" or "solid ignores the script";
        MapInstanceClient a = JoinAt(town, IdA, Vector3.zero);
        MapInstanceClient b = JoinAt(town, IdB, new Vector3(1f, 0f, 0f));
        var party = new PartyId(104);
        SetParty(a.Character, party);
        SetParty(b.Character, party);
        ActiveQuest held = OnTheObjective(a.Character);

        Ticks(town, 30);

        ObjectState added = Assert.Single(a.Added(), s => s.Guid == doodad.Guid.RawValue);
        Assert.Equal(doodad.Template.Id.Value, added.DoodadTemplateId);
        Assert.Equal(WireDoodadState.Ready, added.DoodadState);
        Assert.Equal(bSees, b.Added().Any(s => s.Guid == doodad.Guid.RawValue));

        if (rule is "template gate" or "spawn gate")
        {
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
            return;
        }

        if (rule == "solid ignores the script")
        {
            // A solid doodad is never hidden: the catalog said so once, and its script is never asked.
            Assert.Single(catalogLog.Entries, e => e.Level == LogLevel.Warning);
            Assert.Equal(0, RuledDoodadScript.Asked);
            return;
        }

        // Asked once per viewer, on first sight; a throw was logged once, the second left out within the interval.
        Assert.Equal(2, RuledDoodadScript.Asked);
        Assert.Equal(rule == "script throws" ? 1 : 0, scriptLog.Errors.Count());

        // A steady view asks nothing, even when what the script would answer changed.
        RuledDoodadScript.Asked = 0;
        shown = false;
        Ticks(town, 30);

        Assert.Equal(0, RuledDoodadScript.Asked);
        Assert.DoesNotContain(doodad.Guid.RawValue, a.Removed());

        if (rule == "script refresh")
        {
            // Told its rule changed, each viewer asks the script again at its next decision, and both lose the doodad.
            Refresh(doodad);
            Ticks(town, 6);

            Assert.Equal(2, RuledDoodadScript.Asked);
            Assert.Contains(doodad.Guid.RawValue, a.Removed());
            Assert.Contains(doodad.Guid.RawValue, b.Removed());
        }

        if (rule == "script shows from a level")
        {
            // B reaches the level: its next decision asks the script again, B's alone, and B is told of the doodad.
            b.Character.Level = 5;
            Ticks(town, 6);

            Assert.Equal(1, RuledDoodadScript.Asked);
            Assert.Single(b.Added(), s => s.Guid == doodad.Guid.RawValue);
        }
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
        for (int i = 0; i < 6 && !viewers[0].StateUpdates().Any(s => s.Guid == doodad.Guid.RawValue); i++)
            instance.Update(s_tick);

        // No rule makes its answer differ between viewers, so its update was encoded once, by the broadcast that sent it,
        // and copied to the second viewer.
        Assert.True(SharedThisBroadcast(instance, doodad), "The ungated doodad's update was not a shared entry");
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

    /// <summary>
    /// A template naming <see cref="RuledDoodadScript" />, read through the catalog, which decides whether its CanSee is
    /// asked; gated by quest 900's objective, or solid, when asked.
    /// </summary>
    private static DoodadTemplateView Scripted(TestLog log, bool gated = false, bool solid = false)
    {
        var row = new DoodadTemplate
        {
            Id = new DoodadTemplateId(105),
            Name = "Ruled",
            ModelKit = "props-kit",
            ModelKey = "Crate",
            Interaction = DoodadInteraction.Click,
            InteractionRange = 5f,
            UseLimit = DoodadUseLimit.OncePerInstance,
            SpentLook = DoodadSpentLook.ShowUsed,
            ScriptName = nameof(RuledDoodadScript),
        };
        if (gated)
        {
            row.VisibleQuestId = Quest;
            row.VisibleCondition = DoodadGateCondition.OnObjective;
            row.VisibleObjectiveId = Objective;
        }

        if (solid)
        {
            row.Collision = DoodadCollision.Box;
            row.CollisionHalfX = row.CollisionHalfZ = 0.5f;
            row.CollisionHeight = 1.2f;
        }

        var quest = new QuestTemplate
        {
            Id = new QuestTemplateId(Quest),
            Objectives = [new QuestObjective { Id = Objective, QuestId = new QuestTemplateId(Quest), Count = 2 }],
        };
        var catalog = new DoodadCatalog([row], new LootCatalog([], NullLoggerFactory.Instance), [], [quest],
            name => name == nameof(RuledDoodadScript) ? typeof(RuledDoodadScript) : null, log);
        Assert.True(catalog.TryGet(row.Id, out DoodadTemplateView? template));
        return template;
    }

    /// <summary>A script's word that its rule changed, as IDoodadContext.RefreshVisibility gives it on the tick.</summary>
    private static void Refresh(Doodad doodad) =>
        (typeof(Doodad).GetMethod("RefreshVisibility", BindingFlags.Instance | BindingFlags.NonPublic)
         ?? throw new MissingMethodException(nameof(Doodad), "RefreshVisibility")).Invoke(doodad, null);

    /// <summary>The party a character is in, written as the party service writes it.</summary>
    private static void SetParty(CharacterEntity character, PartyId party) =>
        (typeof(CharacterEntity).GetProperty(nameof(CharacterEntity.PartyId))?.SetMethod
         ?? throw new MissingMethodException(nameof(CharacterEntity), "set_PartyId")).Invoke(character, [party]);

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
        (typeof(Doodad).GetProperty(nameof(Doodad.State))?.SetMethod
         ?? throw new MissingMethodException(nameof(Doodad), "set_State")).Invoke(doodad, [state]);
        (typeof(MapInstance).GetMethod("DoodadChanged", BindingFlags.Instance | BindingFlags.NonPublic)
         ?? throw new MissingMethodException(nameof(MapInstance), "DoodadChanged")).Invoke(instance, [doodad]);
    }

    /// <summary>Whether the last broadcast encoded the doodad's update as an entry every recipient shares (StatePacketAssembler).</summary>
    private static bool SharedThisBroadcast(MapInstance instance, Doodad doodad)
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        object updates = typeof(MapInstance).GetField("_stateUpdates", Private)?.GetValue(instance)
                         ?? throw new MissingFieldException(nameof(MapInstance), "_stateUpdates");
        IDictionary entries = typeof(StatePacketAssembler).GetField("_index", Private)?.GetValue(updates) as IDictionary
                              ?? throw new MissingFieldException(nameof(StatePacketAssembler), "_index");
        return entries.Contains(doodad.Guid.RawValue);
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
