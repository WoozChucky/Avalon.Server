using System.Reflection;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Creatures.Locomotion;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Maps.Navigation;
using Avalon.Network.Packets.State;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Creatures;
using Avalon.World.Public.Units;
using Avalon.Server.World.UnitTests.Creatures;
using DotRecast.Detour;
using DotRecast.Detour.Crowd;
using DotRecast.Recast.Geom;
using DotRecast.Recast.Toolset.Builder;
using Microsoft.Extensions.Logging;
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

    // --- Task 10: configuration-driven selection ------------------------------------------------
    //
    // CreateLocomotion (MapInstance.cs) is the only place that ever constructs a CrowdLocomotion in
    // production; everything above this point had to reach a crowd via SetLocomotion by reflection
    // because nothing chose one on its own yet.

    /// <summary>Production change that breaks this: CreateLocomotion returning CrowdLocomotion for the default config, or the default config's own CreatureLocomotion value changing.</summary>
    [Fact]
    public void Use_Waypoint_Locomotion_By_Default()
    {
        MapInstance instance = BuildInstance(new GameConfiguration { WorldId = new WorldId(1) });

        Assert.IsType<WaypointLocomotion>(instance.Locomotion);
    }

    /// <summary>Production change that breaks this: CreateLocomotion not branching on CreatureLocomotionMode.Crowd, or the `MapNavigator { NavMesh: { } }` pattern rejecting a real baked navmesh.</summary>
    [Fact]
    public void Use_Crowd_Locomotion_When_Configured()
    {
        MapInstance instance = BuildInstance(new GameConfiguration
        {
            WorldId = new WorldId(1),
            CreatureLocomotion = CreatureLocomotionMode.Crowd,
        }, withBakedNavMesh: true);

        Assert.IsType<CrowdLocomotion>(instance.Locomotion);
    }

    /// <summary>
    /// Review Focus 5. A crowd cannot be built without a mesh, and creatures that cannot move are worse
    /// than creatures that move badly.
    /// Production change that breaks this: CreateLocomotion dereferencing a null NavMesh instead of
    /// falling back (would throw a NullReferenceException out of the constructor instead of returning
    /// WaypointLocomotion).
    /// </summary>
    [Fact]
    public void Fall_Back_To_Waypoint_When_The_Navmesh_Is_Missing()
    {
        MapInstance instance = BuildInstance(new GameConfiguration
        {
            WorldId = new WorldId(1),
            CreatureLocomotion = CreatureLocomotionMode.Crowd,
        }, withBakedNavMesh: false);

        Assert.IsType<WaypointLocomotion>(instance.Locomotion);
    }

    /// <summary>
    /// The other half of the fallback: MapInstance is handed an <see cref="IMapNavigator" />, not a
    /// <see cref="MapNavigator" /> — every test substitute, and possibly a future non-DotRecast
    /// implementation, is not a MapNavigator at all. The `is MapNavigator { NavMesh: { } }` pattern
    /// in CreateLocomotion has to fail closed on this shape too, not just on a MapNavigator with a
    /// null NavMesh.
    /// Production change that breaks this: CreateLocomotion casting `_navigator` to MapNavigator
    /// unconditionally (e.g. `((MapNavigator)_navigator).NavMesh`) instead of pattern-matching, which
    /// would throw an InvalidCastException here instead of falling back.
    /// </summary>
    [Fact]
    public void Fall_Back_To_Waypoint_When_The_Navigator_Is_Not_A_MapNavigator()
    {
        MapInstance instance = BuildInstance(new GameConfiguration
        {
            WorldId = new WorldId(1),
            CreatureLocomotion = CreatureLocomotionMode.Crowd,
        }, navigator: Substitute.For<IMapNavigator>());

        Assert.IsType<WaypointLocomotion>(instance.Locomotion);
    }

    /// <summary>
    /// A silent downgrade to Waypoint would look exactly like "the flag does nothing" from the
    /// operator's side — this pins that the fallback actually says which map it affected and why.
    /// Uses a hand-written <see cref="ILogger" /> rather than an NSubstitute one: the interesting
    /// assertion is the formatted message text, and NSubstitute cannot intercept the generic
    /// `Log&lt;TState&gt;` call in a way that recovers it without reimplementing the same formatter.
    /// Production change that breaks this: dropping the LogWarning call, lowering it below Warning,
    /// or a message that no longer names the map or the reason.
    /// </summary>
    [Fact]
    public void Log_A_Warning_When_Crowd_Locomotion_Falls_Back()
    {
        var loggerFactory = new RecordingLoggerFactory();

        BuildInstance(new GameConfiguration
        {
            WorldId = new WorldId(1),
            CreatureLocomotion = CreatureLocomotionMode.Crowd,
        }, withBakedNavMesh: false, loggerFactory: loggerFactory);

        Assert.Contains(loggerFactory.Logger.Entries, entry =>
            entry.Level == LogLevel.Warning &&
            entry.Message.Contains("navmesh", StringComparison.OrdinalIgnoreCase) &&
            entry.Message.Contains("1", StringComparison.Ordinal)); // TemplateId = new MapTemplateId(1)
    }

    // --- MeleeSlotRadius reachability -----------------------------------------------------------
    //
    // MeleeSlotCount/MeleeSlotRadius are read from configuration right next to where the fallback
    // warning above is emitted (MapInstance's constructor). CreatureCombatScript.AttackRange is
    // 1.5f and GameConfiguration.MeleeSlotRadius defaults to 1.5f too — the two are deliberately
    // configured independently, so nothing stops an operator setting MeleeSlotRadius past
    // AttackRange, at which point every creature walks to its slot, arrives, and can never reach
    // its target: total, silent failure with no symptom besides mobs standing still in a ring.

    /// <summary>
    /// Production change that breaks this: WarnIfMeleeSlotRadiusUnreachable comparing with
    /// <c>&gt;=</c> instead of <c>&gt;</c> (or any other change that makes the default
    /// MeleeSlotRadius, which equals AttackRange exactly, trip the warning).
    /// </summary>
    [Fact]
    public void Not_Warn_When_MeleeSlotRadius_Is_The_Default()
    {
        var loggerFactory = new RecordingLoggerFactory();

        BuildInstance(new GameConfiguration { WorldId = new WorldId(1) }, loggerFactory: loggerFactory);

        Assert.DoesNotContain(loggerFactory.Logger.Entries, entry =>
            entry.Level == LogLevel.Warning &&
            entry.Message.Contains("MeleeSlotRadius", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Production change that breaks this: dropping the MeleeSlotRadius/AttackRange comparison (or
    /// the LogWarning call) from MapInstance's constructor, or a message that stops naming the
    /// configured radius, the attack range it was checked against, or the map.
    /// </summary>
    [Fact]
    public void Warn_When_MeleeSlotRadius_Exceeds_The_Attack_Range()
    {
        var loggerFactory = new RecordingLoggerFactory();

        BuildInstance(new GameConfiguration
        {
            WorldId = new WorldId(1),
            MeleeSlotRadius = 4.2f,
        }, loggerFactory: loggerFactory);

        Assert.Contains(loggerFactory.Logger.Entries, entry =>
            entry.Level == LogLevel.Warning &&
            entry.Message.Contains("4.2", StringComparison.Ordinal) && // configured MeleeSlotRadius
            entry.Message.Contains("1.5", StringComparison.Ordinal) && // AttackRange it exceeds
            entry.Message.Contains("attack", StringComparison.OrdinalIgnoreCase)); // consequence
    }

    // --- Creature death, and the tick order the whole seam rests on -----------------------------
    //
    // Everything above this line drives the locomotion by hand or inspects which one was chosen.
    // These drive MapInstance's own Update and its own death handler with a REAL Creature entity, a
    // real locomotion and (for the ordering test) a real CreatureCombatScript, because the two
    // defects below live in the wiring between those, not inside any one of them: death never
    // reached the locomotion at all, and nothing anywhere drove the production script loop and the
    // production locomotion together, so their order was free.

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(0.25);

    /// <summary>
    /// F1, waypoint side. A creature killed mid-approach used to keep walking its remaining path:
    /// <c>WaypointLocomotion.Update</c> advances every registered agent with a non-empty queue and
    /// knows nothing about whether the creature is alive, and the script that used to own the walking
    /// returned early on death. MapNavigator emits a waypoint every 0.5 units, so a creature killed
    /// 10 units out had ~20 queued waypoints and walked the whole way to the player as a corpse, with
    /// MoveState left at Running until the queue drained. The release path that would have prevented
    /// it — <c>MapInstance.RemoveCreature</c> — is unreachable on death, because the only production
    /// caller of it is <c>CreatureRespawner.Update</c> and MapInstance installs
    /// <c>NoOpCreatureRespawner</c>.
    /// Production change that breaks this: dropping <c>_locomotion.Stop</c> from
    /// <c>MapInstance.OnCreatureKilled</c> (the corpse walks on, and MoveState stays Running).
    /// </summary>
    [Fact]
    public void Stop_A_Creature_Where_It_Fell_When_It_Is_Killed()
    {
        (MapInstance instance, _) = BuildKillableInstance();
        Creature creature = RealCreatureAt(Vector3.zero, id: 700_101);
        instance.AddCreature(creature);

        // What a chasing script would have left behind: a long queued path and a moving MoveState.
        instance.Locomotion.MoveTo(creature, new Vector3(20f, 0f, 0f));
        creature.MoveState = MoveState.Running;
        instance.Update(TickInterval);

        Assert.NotEqual(Vector3.zero, creature.Position); // fixture check: it really was walking

        creature.Died(Substitute.For<IUnit>());
        Vector3 whereItFell = creature.Position;

        // 30 simulated seconds — far more than the ~5s the remaining 20-unit path would have taken.
        for (int tick = 0; tick < 120; tick++)
            instance.Update(TickInterval);

        Assert.Equal(whereItFell, creature.Position);
        Assert.Equal(MoveState.Idle, creature.MoveState);
    }

    /// <summary>
    /// The same defect as <see cref="Stop_A_Creature_Where_It_Fell_When_It_Is_Killed" />, but on
    /// <c>RemoveCreature</c> rather than <c>OnCreatureKilled</c> — reachable today, unlike the F1
    /// respawn gap, because <c>World.ApplyScriptsHotReload</c> (World.cs:320) calls
    /// <c>RemoveCreature(entity); … entity.Script = script; AddCreature(entity);</c> every time a
    /// developer edits a creature script while that creature is mid-chase. <c>RemoveCreature</c>
    /// unregisters and releases the melee slot but did not <c>Stop</c>, so the creature kept its
    /// last <c>MoveState</c> and <c>Velocity</c> (both locomotions no-op on an unregistered
    /// creature) — a hot-reloaded creature left at <c>MoveState.Running</c> that the client
    /// extrapolates forever, since the reloaded script's fresh state never re-triggers a MoveTo on
    /// its own.
    /// Production change that breaks this: dropping <c>_locomotion.Stop</c> from
    /// <c>MapInstance.RemoveCreature</c>, or reordering it after <c>Unregister</c> (a no-op on an
    /// already-unregistered creature in both implementations, per the comment in
    /// <c>OnCreatureKilled</c>).
    /// </summary>
    [Fact]
    public void Stop_A_Creature_Where_It_Stood_When_It_Is_Removed()
    {
        (MapInstance instance, _) = BuildInstanceWithCreature();
        Creature creature = RealCreatureAt(Vector3.zero, id: 700_120);
        instance.AddCreature(creature);

        instance.Locomotion.MoveTo(creature, new Vector3(20f, 0f, 0f));
        creature.MoveState = MoveState.Running;
        instance.Update(TickInterval);

        Assert.NotEqual(Vector3.zero, creature.Position); // fixture check: it really was walking
        Vector3 whereItStood = creature.Position;

        instance.RemoveCreature(creature);

        Assert.Equal(MoveState.Idle, creature.MoveState);
        Assert.Equal(Vector3.zero, creature.Velocity);

        // And it actually stays put: with the agent gone, nothing should advance it further either.
        for (int tick = 0; tick < 60; tick++)
            instance.Update(TickInterval);

        Assert.Equal(whereItStood, creature.Position);
    }

    /// <summary>
    /// F1, crowd side, and the half <see cref="Stop_A_Creature_Where_It_Fell_When_It_Is_Killed" />
    /// cannot see: Stop alone would freeze the corpse but leave its <c>DtCrowdAgent</c> in the crowd
    /// forever — a permanent obstacle living creatures steer around, which
    /// <c>DtCrowd.HandleCollisions</c> also shoves about regardless of whether it has a target, and
    /// one leaked agent per kill for the life of a persistent town instance.
    /// Production change that breaks this: dropping <c>_locomotion.Unregister</c> from
    /// <c>MapInstance.OnCreatureKilled</c>.
    /// </summary>
    [Fact]
    public void Remove_A_Killed_Creatures_Crowd_Agent()
    {
        (MapInstance instance, _) = BuildKillableInstance();
        var crowd = new CrowdLocomotion(CrowdLocomotionShould.FlatNavMesh.Value,
            NavmeshBuildSettings.AgentRadius, NullLoggerFactory.Instance.CreateLogger("test"));
        SetLocomotion(instance, crowd);

        Creature creature = RealCreatureAt(Vector3.zero, id: 700_102);
        instance.AddCreature(creature);
        Assert.Single(CrowdOf(crowd).GetActiveAgents());

        creature.Died(Substitute.For<IUnit>());

        Assert.Empty(CrowdOf(crowd).GetActiveAgents());
    }

    /// <summary>
    /// F1, slot side, outward direction: the slot the dying creature held on whatever it was
    /// attacking. <c>CreatureCombatScript</c>'s death branch releases it too, but only for a creature
    /// that had that script to run; <c>OnCreatureKilled</c> is the chokepoint every death funnels
    /// through, so it is where the invariant can actually be enforced. MeleeSlotCount is 1 here so
    /// "the slot came back" is observable as a rival claim that could not succeed a moment earlier.
    /// Production change that breaks this: dropping <c>_meleeSlots.ReleaseClaimant</c> from
    /// <c>MapInstance.OnCreatureKilled</c>. (<c>ReleaseTarget</c> cannot substitute for it — the dead
    /// creature is the claimant here, not the key.)
    /// </summary>
    [Fact]
    public void Release_The_Melee_Slot_A_Killed_Creature_Held()
    {
        (MapInstance instance, _) = BuildKillableInstance(
            new GameConfiguration { WorldId = new WorldId(1), MeleeSlotCount = 1 });

        Creature creature = RealCreatureAt(new Vector3(3f, 0f, 0f), id: 700_103);
        instance.AddCreature(creature);

        var attackedGuid = new ObjectGuid(ObjectType.Character, 700_901);
        var rival = new ObjectGuid(ObjectType.Creature, 700_104);
        var rivalPosition = new Vector3(-3f, 0f, 0f);

        Assert.True(instance.MeleeSlots.TryClaim(attackedGuid, creature.Guid, Vector3.zero, creature.Position, out _));
        Assert.False(instance.MeleeSlots.TryClaim(attackedGuid, rival, Vector3.zero, rivalPosition, out _));

        creature.Died(Substitute.For<IUnit>());

        Assert.True(instance.MeleeSlots.TryClaim(attackedGuid, rival, Vector3.zero, rivalPosition, out _),
            "the slot the dead creature held on its target was never given back");
    }

    /// <summary>
    /// F1, slot side, inward direction, and the F5 gap it closes for free: a dying creature is also a
    /// <em>target</em> others hold slots on. The script-side target-death release is gated on
    /// <c>_target is ICharacter</c>, so a creature target dying leaves its ring claimed and its
    /// claimants pointing at a corpse. Not reachable today (nothing puts a creature in another
    /// creature's threat list), which is exactly why it needs pinning rather than arguing about.
    /// Production change that breaks this: dropping <c>_meleeSlots.ReleaseTarget</c> from
    /// <c>MapInstance.OnCreatureKilled</c>. (<c>ReleaseClaimant</c> cannot substitute for it — the
    /// dead creature is the key here, not a claimant.)
    /// </summary>
    [Fact]
    public void Release_Every_Melee_Slot_Held_On_A_Killed_Creature()
    {
        (MapInstance instance, _) = BuildKillableInstance(
            new GameConfiguration { WorldId = new WorldId(1), MeleeSlotCount = 1 });

        Creature victim = RealCreatureAt(Vector3.zero, id: 700_105);
        instance.AddCreature(victim);

        var chaser = new ObjectGuid(ObjectType.Creature, 700_106);
        var secondChaser = new ObjectGuid(ObjectType.Creature, 700_107);
        var secondChaserPosition = new Vector3(-3f, 0f, 0f);

        Assert.True(instance.MeleeSlots.TryClaim(victim.Guid, chaser, victim.Position, new Vector3(3f, 0f, 0f), out _));
        Assert.False(instance.MeleeSlots.TryClaim(victim.Guid, secondChaser, victim.Position, secondChaserPosition, out _));

        victim.Died(Substitute.For<IUnit>());

        Assert.True(instance.MeleeSlots.TryClaim(victim.Guid, secondChaser, victim.Position, secondChaserPosition, out _),
            "the ring other creatures had claimed on the dead creature was never freed");
    }

    /// <summary>
    /// The other end of F1's teardown. Death now unregisters the creature from the locomotion, so
    /// respawn has to put it back or a creature that dies once can never move again. Nothing calls
    /// <c>RespawnCreature</c> today (NoOpCreatureRespawner), which is precisely why the obligation
    /// needs to be recorded in a test rather than in a comment: the day respawn is wired up, this is
    /// what says the creature comes back in a clean state.
    /// Production change that breaks this: removing <c>_locomotion.Register</c> from
    /// <c>MapInstance.RespawnCreature</c> while <c>OnCreatureKilled</c> still unregisters — the
    /// half-fix that leaves a respawned creature permanently immobile.
    /// </summary>
    [Fact]
    public void Register_A_Respawned_Creature_With_The_Locomotion_Again()
    {
        (MapInstance instance, _) = BuildKillableInstance();
        Creature creature = RealCreatureAt(Vector3.zero, id: 700_108);
        instance.AddCreature(creature);
        creature.Died(Substitute.For<IUnit>());

        instance.RespawnCreature(creature);

        instance.Locomotion.MoveTo(creature, new Vector3(20f, 0f, 0f));
        instance.Update(TickInterval);

        Assert.NotEqual(Vector3.zero, creature.Position);
    }

    /// <summary>
    /// The production ordering the test above does not model: <c>CreatureRespawner.ScheduleRespawn</c>
    /// starts both the body-remove timer (default 120s) and the respawn timer (default 180s), so
    /// <c>RemoveCreature</c> always fires before <c>RespawnCreature</c> for a creature that respawns.
    /// <c>RemoveCreature</c> drops the creature from <c>_creatures</c>; a <c>RespawnCreature</c> that
    /// only re-registers with the locomotion (rather than calling <c>AddCreature</c>) then creates a
    /// live locomotion registration — under <c>CrowdLocomotion</c>, a live <c>DtCrowdAgent</c> — for a
    /// creature that is not in <c>_creatures</c>: never ticked by the Step 4 script loop, never
    /// broadcast, and never reachable by a future <c>OnCreatureKilled</c> (its first line guards on
    /// <c>_creatures.ContainsKey</c>). That is the exact "permanent invisible obstacle" leak F1 exists
    /// to close, relocated from the death path to the respawn path. The full round-trip through a real
    /// script proves the creature is not just present in the dictionary but actually alive again —
    /// driven by Step 4 the same way <c>Tick_The_Locomotion_After_The_Creature_Scripts</c> proves it
    /// for a fresh creature.
    /// Production change that breaks this: <c>RespawnCreature</c> calling
    /// <c>_locomotion.Register(creature, _creatureAgentRadius)</c> directly instead of
    /// <c>AddCreature(creature)</c>.
    /// </summary>
    [Fact]
    public void Return_A_Respawned_Creature_To_The_Creature_Dictionary_After_Removal()
    {
        (MapInstance instance, ICharacter target) = BuildKillableInstance();
        Creature creature = RealCreatureAt(Vector3.zero, id: 700_110);
        instance.AddCreature(creature);
        creature.Died(Substitute.For<IUnit>());

        // Mirrors CreatureRespawner.Update: the body-remove timer fires before the respawn timer,
        // so production always removes the creature from _creatures before respawning it.
        instance.RemoveCreature(creature);
        instance.RespawnCreature(creature);

        Assert.True(instance.Creatures.ContainsKey(creature.Guid),
            "a respawned creature must return to _creatures, or it is never ticked, broadcast, or reachable by a future death again");

        // And it is actually alive again, not just present: the script loop (Step 4) only reaches
        // creatures in _creatures, so a real script issuing MoveTo through Update proves the wiring,
        // not just the dictionary entry.
        var script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, instance);
        script.OnEnteredRange(target);
        creature.Script = script;

        instance.Update(TickInterval);

        Assert.NotEqual(Vector3.zero, creature.Position);
    }

    /// <summary>
    /// The tick order the whole seam rests on: the locomotion must be ticked <em>after</em> the
    /// creature scripts, because the scripts choose destinations and the locomotion consumes them.
    /// Reversed, every creature acts on last tick's decision. Nothing else in the suite notices:
    /// every other test in this class drives <c>MoveTo</c> by hand, and the integration tests in
    /// <c>CreatureCombatScriptShould</c> hand-roll <c>script.Update(); locomotion.Update();</c>
    /// themselves rather than going through MapInstance — so moving <c>_locomotion.Update</c> above
    /// the script loop leaves the rest of the suite entirely green.
    ///
    /// One tick is the whole test. In the right order the script's very first <c>MoveTo</c> is walked
    /// inside the same Update, so Position changes; with the locomotion ticked first it runs against
    /// an empty path and Position is untouched, because the MoveTo has not happened yet.
    /// Production change that breaks this: moving <c>_locomotion.Update(deltaTime)</c> above
    /// "Step 4: Update creature scripts" in <c>MapInstance.Update</c>.
    /// </summary>
    [Fact]
    public void Tick_The_Locomotion_After_The_Creature_Scripts()
    {
        (MapInstance instance, ICharacter target) = BuildKillableInstance();
        Creature creature = RealCreatureAt(Vector3.zero, id: 700_109);
        instance.AddCreature(creature);

        // A real CreatureCombatScript against the real MapInstance as its ISimulationContext: the
        // claim is about the production script loop feeding the production locomotion, so neither
        // end may be a stand-in. The seated character sits 10 units away, well outside AttackRange,
        // so the script's first Update has no choice but to issue a MoveTo.
        var script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, instance);
        script.OnEnteredRange(target);
        creature.Script = script;

        instance.Update(TickInterval);

        Assert.NotEqual(Vector3.zero, creature.Position);
    }

    /// <summary>
    /// A real <see cref="Creature" /> rather than a substitute: these tests kill it through
    /// <see cref="Creature.Died" />, which raises the static <c>Creature.OnCreatureKilled</c> that
    /// MapInstance subscribes to — the chain F1 is about, and one no ICreature substitute can raise.
    /// </summary>
    private static Creature RealCreatureAt(Vector3 position, uint id, float speed = 4f)
    {
        var metadata = Substitute.For<ICreatureMetadata>();
        metadata.SpeedWalk.Returns(speed / 2f);
        metadata.SpeedRun.Returns(speed);

        return new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Metadata = metadata,
            Name = "corpse-under-test",
            Position = position,
            Speed = speed,
            Health = 100,
            CurrentHealth = 100,
        };
    }

    /// <summary>
    /// Like <see cref="BuildInstanceWithCreature" />, but with MapInstance's <c>OnCreatureKilled</c>
    /// subscription deliberately LEFT ATTACHED, since these tests go through the real
    /// <c>Creature.Died</c> → static event → <c>MapInstance.OnCreatureKilled</c> chain. Every other
    /// static subscription is detached exactly as elsewhere in this file, and the surviving one is
    /// harmless to other tests: <c>OnCreatureKilled</c> returns immediately for any creature that is
    /// not in <em>this</em> instance's <c>_creatures</c>, and the guids above are unique to this file.
    /// The navigator returns a long 0.5-step path (the shape MapNavigator actually produces) so a
    /// creature killed mid-walk has plenty of path left to keep walking if nothing stops it.
    /// The seated character is returned because the ordering test needs something for the script to
    /// chase; it stands 10 units out, well beyond AttackRange.
    /// </summary>
    private static (MapInstance Instance, ICharacter Target) BuildKillableInstance(GameConfiguration? config = null)
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IScriptManager)).Returns(Substitute.For<IScriptManager>());
        serviceProvider.GetService(typeof(CombatConfig)).Returns(new CombatConfig());

        var world = Substitute.For<IWorld>();
        world.Configuration.Returns(config ?? new GameConfiguration());

        var navigator = Substitute.For<IMapNavigator>();
        navigator.FindPath(Arg.Any<Vector3>(), Arg.Any<Vector3>())
            .Returns(call => StepwisePath(call.ArgAt<Vector3>(0), call.ArgAt<Vector3>(1)));

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

        DetachAll(typeof(Creature), instance, except: nameof(Creature.OnCreatureKilled));
        DetachAll(typeof(CharacterEntity), instance);

        var character = Substitute.For<ICharacter>();
        character.Guid.Returns(new ObjectGuid(ObjectType.Character, 700_800));
        character.Position.Returns(new Vector3(10f, 0f, 0f));
        character.IsDead.Returns(false);
        var connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        instance.AddCharacter(connection);

        return (instance, character);
    }

    /// <summary>
    /// A path in the shape <see cref="MapNavigator" /> returns: the start, a waypoint every 0.5
    /// units (its <c>StepSize</c>), then the exact destination. A one-element path would let a
    /// corpse "arrive" almost immediately and hide the walking-corpse defect entirely.
    /// </summary>
    private static List<Vector3> StepwisePath(Vector3 from, Vector3 to)
    {
        const float stepSize = 0.5f;
        var path = new List<Vector3> { from };

        float total = Vector3.Distance(from, to);
        for (float walked = stepSize; walked < total; walked += stepSize)
            path.Add(Vector3.MoveTowards(from, to, walked));

        path.Add(to);
        return path;
    }

    /// <summary>
    /// Builds a bare MapInstance (no creature, no character seated) purely to inspect which
    /// <see cref="ICreatureLocomotion" /> its constructor chose. Reuses the same construction shape
    /// as <see cref="BuildInstanceWithCreature" /> but parameterizes the two things Task 10's
    /// selection actually reads: the configuration, and the navigator.
    /// </summary>
    /// <param name="withBakedNavMesh">
    /// True loads <see cref="CrowdLocomotionShould.FlatNavMesh" /> into a real
    /// <see cref="MapNavigator" /> before construction (the success path for Crowd). False, the
    /// default, builds a real <see cref="MapNavigator" /> that is never loaded, so its
    /// <see cref="MapNavigator.NavMesh" /> stays null — one of the two fallback conditions. Ignored
    /// when <paramref name="navigator" /> is supplied directly.
    /// </param>
    /// <param name="navigator">
    /// Overrides the navigator entirely, for the other fallback condition: a navigator that is not a
    /// <see cref="MapNavigator" /> at all (every test substitute, including the one
    /// <see cref="BuildInstanceWithCreature" /> uses).
    /// </param>
    /// <param name="loggerFactory">
    /// Overrides the logger factory so <see cref="Log_A_Warning_When_Crowd_Locomotion_Falls_Back" />
    /// can inspect what MapInstance logged.
    /// </param>
    private static MapInstance BuildInstance(
        GameConfiguration config,
        bool withBakedNavMesh = false,
        IMapNavigator? navigator = null,
        ILoggerFactory? loggerFactory = null)
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IScriptManager)).Returns(Substitute.For<IScriptManager>());
        serviceProvider.GetService(typeof(CombatConfig)).Returns(new CombatConfig());

        var world = Substitute.For<IWorld>();
        world.Configuration.Returns(config);

        if (navigator is null)
        {
            var mapNavigator = new MapNavigator(NullLoggerFactory.Instance);
            if (withBakedNavMesh)
                mapNavigator.LoadFromNavMesh(CrowdLocomotionShould.FlatNavMesh.Value);
            navigator = mapNavigator;
        }

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
            loggerFactory ?? NullLoggerFactory.Instance,
            serviceProvider,
            world,
            new MapTemplateId(1),
            ownerCharacterId: null,
            layout,
            navigator,
            seed: 0);

        // See DetachStaticEventHandlers' remarks on BuildInstanceWithCreature: same leak, same fix,
        // needed here even though this instance never gets a populated _connections/_creatures.
        DetachStaticEventHandlers(instance);

        return instance;
    }

    /// <summary>
    /// Hand-written rather than NSubstitute: captures the formatted message text from
    /// <see cref="ILogger.Log{TState}" /> so a test can assert on what an operator would actually
    /// read, not just that some call happened.
    /// </summary>
    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        public RecordingLogger Logger { get; } = new();

        public ILogger CreateLogger(string categoryName) => Logger;

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }
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

    /// <param name="except">
    /// An event to leave subscribed. Only <see cref="BuildKillableInstance" /> uses it, to keep
    /// <c>Creature.OnCreatureKilled</c> attached so a real <c>Creature.Died</c> reaches the instance.
    /// </param>
    private static void DetachAll(Type declaringType, object target, string? except = null)
    {
        foreach (EventInfo eventInfo in declaringType.GetEvents(BindingFlags.Public | BindingFlags.Static))
        {
            if (eventInfo.Name == except)
                continue;

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
