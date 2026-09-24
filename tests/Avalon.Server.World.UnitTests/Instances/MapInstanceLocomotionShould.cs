using System.Reflection;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Creatures.Locomotion;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Maps.Navigation;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
using Avalon.World.Scripts;
using Avalon.Server.World.UnitTests.Creatures;
using DotRecast.Detour;
using DotRecast.Detour.Crowd;
using DotRecast.Recast.Geom;
using DotRecast.Recast.Toolset.Builder;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// MapInstance owns a single locomotion object per instance, registers/unregisters creatures as
/// they enter and leave, and ticks it once per Update — after the script loop, since scripts
/// decide destinations and locomotion executes them.
/// </summary>
public class MapInstanceLocomotionShould
{
    [Fact]
    public void Register_A_Creature_With_Locomotion_When_It_Is_Added()
    {
        (MapInstance instance, ICreature creature) = BuildInstanceWithCreature();

        instance.AddCreature(creature);

        // The creature is now driveable: a destination followed by a tick moves it.
        instance.Locomotion.MoveTo(creature, new Vector3(5f, 0f, 0f));
        instance.Update(TimeSpan.FromSeconds(0.5));

        creature.Received().Position = Arg.Any<Vector3>();
    }

    [Fact]
    public void Unregister_A_Creature_When_It_Is_Removed()
    {
        (MapInstance instance, ICreature creature) = BuildInstanceWithCreature();
        instance.AddCreature(creature);
        instance.RemoveCreature(creature);

        creature.ClearReceivedCalls();
        instance.Locomotion.MoveTo(creature, new Vector3(5f, 0f, 0f));
        instance.Update(TimeSpan.FromSeconds(0.5));

        creature.DidNotReceive().Position = Arg.Any<Vector3>();
    }

    // --- Task 9: the CrowdIncludesPlayers flag -------------------------------------------------
    //
    // Task 10 is what makes MapInstance ever construct a CrowdLocomotion on its own (config-driven
    // selection); until then _locomotion is always WaypointLocomotion, so a flag test run against an
    // untouched instance would pass for the wrong reason — the `is CrowdLocomotion` type test in
    // MapInstance.Update would already be false regardless of what CrowdIncludesPlayers says. These
    // two tests instead swap in a real CrowdLocomotion by reflection (see SetLocomotion) so the flag
    // is the only thing distinguishing them, over the same production Update() call path.

    /// <summary>Production change that breaks this: dropping the `_crowdIncludesPlayers` check, or the sync loop itself, from MapInstance.Update.</summary>
    [Fact]
    public void Sync_Every_Characters_Position_Into_The_Crowd_Each_Tick_When_The_Flag_Is_On()
    {
        (MapInstance instance, _) = BuildInstanceWithCreature(crowdIncludesPlayers: true);
        var crowd = new CrowdLocomotion(CrowdLocomotionShould.FlatNavMesh.Value,
            NavmeshBuildSettings.AgentRadius, NullLoggerFactory.Instance.CreateLogger("test"));
        SetLocomotion(instance, crowd);

        instance.Update(TimeSpan.FromSeconds(1d / 60d));

        Assert.Single(CrowdOf(crowd).GetActiveAgents());
    }

    /// <summary>Production change that breaks this: calling SyncPlayer regardless of the flag, i.e. losing the gate entirely.</summary>
    [Fact]
    public void Sync_No_Players_Into_The_Crowd_When_The_Flag_Is_Off()
    {
        (MapInstance instance, _) = BuildInstanceWithCreature(crowdIncludesPlayers: false);
        var crowd = new CrowdLocomotion(CrowdLocomotionShould.FlatNavMesh.Value,
            NavmeshBuildSettings.AgentRadius, NullLoggerFactory.Instance.CreateLogger("test"));
        SetLocomotion(instance, crowd);

        instance.Update(TimeSpan.FromSeconds(1d / 60d));

        Assert.Empty(CrowdOf(crowd).GetActiveAgents());
    }

    /// <summary>
    /// Same reflection technique <see cref="CrowdLocomotionShould.CrowdOf" /> uses to reach
    /// into <see cref="CrowdLocomotion" />'s own private state, aimed at MapInstance's private
    /// <c>_locomotion</c> field instead. Needed only because Task 10 (config-driven selection) has
    /// not landed yet; once it has, these two tests can build the instance with
    /// <c>CreatureLocomotion = CreatureLocomotionMode.Crowd</c> and drop this entirely.
    /// </summary>
    private static void SetLocomotion(MapInstance instance, ICreatureLocomotion locomotion) =>
        typeof(MapInstance)
            .GetField("_locomotion", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(instance, locomotion);

    private static DtCrowd CrowdOf(CrowdLocomotion locomotion) =>
        (DtCrowd)typeof(CrowdLocomotion)
            .GetField("_crowd", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(locomotion)!;

    /// <summary>
    /// Builds a MapInstance directly (constructor is the one at
    /// ChunkLayouts/IChunkLayoutInstanceFactory.cs:59) plus one creature substitute, not yet
    /// added. A character is also seated in the instance: MapInstance.Update returns immediately
    /// when there are no characters, before either the script loop or the locomotion tick run, so
    /// without one the assertions below would fail for a reason unrelated to locomotion wiring.
    /// </summary>
    /// <param name="crowdIncludesPlayers">
    /// <see cref="GameConfiguration.CrowdIncludesPlayers" />. Selecting <see cref="CrowdLocomotion" />
    /// itself is Task 10's job, not this constructor's (see <see cref="SetLocomotion" />) — this
    /// only controls what MapInstance.Update's own flag check reads.
    /// </param>
    private static (MapInstance Instance, ICreature Creature) BuildInstanceWithCreature(
        bool crowdIncludesPlayers = false)
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IScriptManager)).Returns(Substitute.For<IScriptManager>());
        serviceProvider.GetService(typeof(CombatConfig)).Returns(new CombatConfig());

        var world = Substitute.For<IWorld>();
        world.Configuration.Returns(new GameConfiguration { CrowdIncludesPlayers = crowdIncludesPlayers });

        // A NSubstitute IMapNavigator returns an empty path from FindPath by default, which
        // WaypointLocomotion reads as "nowhere to go" and resolves via its come-to-rest path
        // without ever writing Position. Stub a real waypoint, far enough from the origin that
        // the 0.1f arrival epsilon does not immediately consume it on the first tick.
        var navigator = Substitute.For<IMapNavigator>();
        navigator.FindPath(Arg.Any<Vector3>(), Arg.Any<Vector3>())
            .Returns(new List<Vector3> { new Vector3(5f, 0f, 0f) });

        var entryChunk = new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero);
        var layout = new ChunkLayout(
            Seed: 0,
            Chunks: new[] { entryChunk },
            EntryChunk: entryChunk,
            BossChunk: null,
            Portals: Array.Empty<PortalPlacement>(),
            EntrySpawnWorldPos: Vector3.zero,
            CellSize: 30f,
            Config: null);

        var instance = new MapInstance(
            NullLoggerFactory.Instance,
            serviceProvider,
            world,
            new MapTemplateId(1),
            ownerCharacterId: null,
            layout,
            navigator,
            seed: 0);

        // MapInstance's constructor subscribes its instance methods to STATIC events on Creature
        // and CharacterEntity (OnSelfDamaged, OnUnitDamaged, OnCreatureKilled, ...), and there is
        // no matching unsubscribe anywhere. Left attached, this instance would keep reacting to
        // every OTHER test's Creature/CharacterEntity events for the rest of the process —
        // BroadcastUnitHit in particular broadcasts to every connection unconditionally, so it NREs
        // the moment an unrelated test's attacker substitute has no Guid configured. None of this
        // suite's assertions exercise damage/kill/animation events, so detaching immediately, before
        // this instance ever gets a populated _connections/_creatures to broadcast from, is safe and
        // removes the leak at its source rather than merely narrowing the window it is live in.
        DetachStaticEventHandlers(instance);

        var character = Substitute.For<ICharacter>();
        character.Guid.Returns(new ObjectGuid(ObjectType.Character, 424_242));
        var connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        instance.AddCharacter(connection);

        var creature = Substitute.For<ICreature>();
        creature.Guid.Returns(new ObjectGuid(ObjectType.Creature, 424_243));
        var metadata = Substitute.For<ICreatureMetadata>();
        metadata.SpeedWalk.Returns(2f);
        metadata.SpeedRun.Returns(4f);
        creature.Metadata.Returns(metadata);
        creature.Position.Returns(Vector3.zero);
        creature.Speed.Returns(4f);

        return (instance, creature);
    }

    /// <summary>
    /// Removes every handler <paramref name="instance"/> registered on Creature's and
    /// CharacterEntity's static events, by scanning each event's backing delegate for handlers
    /// whose <see cref="Delegate.Target"/> is this instance. Generic over which events exist so it
    /// keeps working if MapInstance's constructor subscribes to more of them later.
    /// </summary>
    private static void DetachStaticEventHandlers(MapInstance instance)
    {
        DetachAll(typeof(Creature), instance);
        DetachAll(typeof(CharacterEntity), instance);
    }

    private static void DetachAll(Type declaringType, object target)
    {
        foreach (EventInfo eventInfo in declaringType.GetEvents(BindingFlags.Public | BindingFlags.Static))
        {
            FieldInfo? backingField =
                declaringType.GetField(eventInfo.Name, BindingFlags.NonPublic | BindingFlags.Static);

            if (backingField?.GetValue(null) is not Delegate current)
                continue;

            foreach (Delegate handler in current.GetInvocationList())
            {
                if (ReferenceEquals(handler.Target, target))
                    eventInfo.RemoveEventHandler(null, handler);
            }
        }
    }
}
