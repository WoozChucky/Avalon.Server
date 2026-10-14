using System.Reflection;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.World.Parties;
using Avalon.Domain.World;
using Avalon.Network.Packets.State;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Avalon.World.Scripts.Creatures;
using Avalon.World.Testing.Scenarios;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// Scheduling creatures (#979) changes which creatures an instance visits on a tick, never what they do: the same world
/// is run twice side by side, once with every creature updated on every tick (the loop as it was before, kept as
/// <c>MapInstance.UpdateEveryCreatureEveryTick</c>) and once scheduled, and after every tick each creature must stand,
/// move, fight, target, detect, patrol and die exactly alike. The forests are built as production builds them (real
/// layouts, spawn tables, AI and kits) with parties fighting through them, a character that leaves mid-fight and one
/// that arrives later; the town holds town NPCs, a patrol that pauses and fights, aggro creatures attacked, dodging (an
/// attack that dealt nothing), killing a character, killed, hot reloaded, one placed by a command, and a script that does
/// not opt in, which must still be called on every tick.
/// </summary>
public sealed class CreatureSchedulingShould
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData("forest", 1800)]
    [InlineData("town", 1500)]
    public void Leave_every_creature_as_updating_every_creature_every_tick_does(string place, int ticks)
    {
        using Side reference = place == "forest" ? Forest(scheduled: false) : Town(scheduled: false);
        using Side scheduled = place == "forest" ? Forest(scheduled: true) : Town(scheduled: true);

        long visits = 0, possible = 0;
        for (int tick = 0; tick < ticks; tick++)
        {
            reference.Act(tick);
            scheduled.Act(tick);
            TimeSpan delta = Jittered(tick);
            reference.World.Tick(delta);
            scheduled.World.Tick(delta);

            for (int m = 0; m < reference.Maps.Count; m++)
            {
                CreatureView[] expected = Views(reference.Maps[m]);
                CreatureView[] actual = Views(scheduled.Maps[m]);
                Assert.True(expected.Length == actual.Length,
                    $"{place} map {m}, tick {tick}: {actual.Length} creatures scheduled, {expected.Length} every tick");
                for (int i = 0; i < expected.Length; i++)
                {
                    if (!expected[i].Equals(actual[i]))
                        Assert.Fail($"{place} map {m}, tick {tick}, creature {i}:\n every tick {expected[i]}\n scheduled  {actual[i]}");
                }

                visits += EveryStep(scheduled.Maps[m]);
                possible += actual.Length;
            }
        }

        // The equivalence would hold trivially if nothing waited: most creatures must have been off the every-step list.
        Assert.True(visits < possible / 2, $"{place}: {visits} of {possible} creature places ran on every step");

        if (place == "forest")
            Assert.True(reference.Swapped && scheduled.Swapped, "No forest creature was fighting with a cooldown running to reload");

        // A script that does not opt in is called on every tick, scheduled or not.
        if (place == "town")
        {
            Assert.Equal(ticks, reference.Counting!.Updates);
            Assert.Equal(ticks, scheduled.Counting!.Updates);
        }
    }

    /// <summary>
    /// The opt-in's contract for a script of any author (#979, for #622): a script that waits for an event alone is called
    /// once after joining and then only on the tick after each event, having heard the event first; one that waits for a
    /// time is called on the first tick at which that much creature time has passed.
    /// </summary>
    [Theory]
    [InlineData("hit")]
    [InlineData("hit, after waiting for TimeSpan.MaxValue")]
    [InlineData("attacked for nothing")]
    [InlineData("a character left")]
    [InlineData("a new patrol path")]
    [InlineData("its due time")]
    public void Call_a_waiting_script_only_after_its_events_and_at_its_due_time(string wake)
    {
        using var world = new ScenarioWorld();
        MapInstance town = world.AddInstance();
        CharacterEntity watcher = world.NewCharacter(1);
        world.Join(town, watcher, TownNavmesh.LoopCentre);
        ScenarioConnection leaver = world.Join(town, world.NewCharacter(2), TownNavmesh.LoopCentre);
        var template = new CreatureTemplate { Id = new CreatureTemplateId(1) };
        TimeSpan wait = wake switch
        {
            // Exactly thirty ticks: the call falls on the tick the clock reaches its due time, not the one after.
            "its due time" => TimeSpan.FromTicks(ScenarioWorld.Dt.Ticks * 30),
            "hit, after waiting for TimeSpan.MaxValue" => TimeSpan.MaxValue,
            _ => Timeout.InfiniteTimeSpan,
        };
        WaitingScript script = null!;
        Creature creature = Place(town, 1, template, TownNavmesh.EntrySpawn, c => script = new WaitingScript(c, town, wait));

        world.Tick(); // It joined: called once, then it waits.
        for (int i = 0; i < 20; i++)
            world.Tick();
        Assert.Single(script.Updates);

        if (wake == "its due time")
        {
            for (int i = 0; i < 60; i++)
                world.Tick();

            // Called at the first tick at which its wait had passed since the last call, each time.
            Assert.Equal(3, script.Updates.Count);
            for (int i = 1; i < script.Updates.Count; i++)
            {
                TimeSpan between = script.Updates[i] - script.Updates[i - 1];
                Assert.InRange(between, wait, wait + ScenarioWorld.Dt - TimeSpan.FromTicks(1));
            }

            return;
        }

        var combat = (CombatService)town.CombatService;
        switch (wake)
        {
            case "hit" or "hit, after waiting for TimeSpan.MaxValue": combat.ApplyDamage(watcher, creature, 5); break;
            case "attacked for nothing": combat.EnterAuraCombat(watcher, creature); break;
            case "a character left": town.RemoveCharacter(leaver); break;
            case "a new patrol path": creature.PatrolPath = [new PatrolPoint(TownNavmesh.LoopCentre, TimeSpan.Zero)]; break;
        }

        Assert.Equal(wake == "a new patrol path" ? 0 : 1, script.Heard);
        world.Tick();
        Assert.Equal(2, script.Updates.Count);
        for (int i = 0; i < 20; i++)
            world.Tick();
        Assert.Equal(2, script.Updates.Count);
    }

    // ---- The forest ---------------------------------------------------------------------------------------------

    private static Side Forest(bool scheduled)
    {
        var world = ScenarioWorld.CreateWithReferenceData(seed: 1);
        var side = new Side(world);
        FighterHandlers handlers = LoopFighter.Handlers(world);
        CharacterClass[] classes = [CharacterClass.Warrior, CharacterClass.Wizard, CharacterClass.Hunter, CharacterClass.Healer];
        int fighters = 0;
        foreach (int seed in new[] { 1, 2 })
        {
            var members = new ScenarioConnection[3];
            var party = new LoopFighter[3];
            var characters = new ICharacter[3];
            for (int m = 0; m < 3; m++)
            {
                CharacterEntity character = world.NewCharacter(classes[fighters % classes.Length]);
                characters[m] = character;
                var fighter = new LoopFighter(handlers, character, seed: ++fighters);
                members[m] = world.Connect(character, fighter.Step);
                party[m] = fighter;
            }

            PartyId partyId = world.FormParty(members);
            MapInstance forest = world.NewForestInstance(seed, partyId);
            side.Add(forest, scheduled);
            var fight = new FightForest(forest);
            for (int m = 0; m < 3; m++)
            {
                party[m].Enter(fight, characters);
                world.Join(forest, members[m], fight.Entry);
            }
        }

        // A character out of any party, standing beside a creature of the first forest so that creature finds it and
        // fights it, then leaving the forest mid-fight; and another arriving later beside a creature of the second.
        MapInstance first = side.Maps[0], second = side.Maps[1];
        ScenarioConnection? leaver = null;
        side.Actions = tick =>
        {
            if (tick == 300)
                leaver = world.Join(first, world.NewCharacter(9001), Beside(first, 40));
            else if (tick == 700)
                first.RemoveCharacter(leaver!);
            else if (tick == 900)
                world.Join(second, world.NewCharacter(9002), Beside(second, 60));
            else if (tick >= 1000 && !side.Swapped)
                side.Swapped = ReloadAsPlainCombat(world, first);
        };

        return side;
    }

    /// <summary>
    /// A hot reload that names the plain <see cref="CreatureCombatScript" /> for the first creature fighting with a
    /// cooldown running: the creature keeps its abilities and their cooldowns (they are the creature's), and its new
    /// script, idle from the start, must still count them down on every tick. False while none is fighting so.
    /// </summary>
    private static bool ReloadAsPlainCombat(ScenarioWorld world, MapInstance map)
    {
        foreach (ICreature creature in map.Creatures.Values)
        {
            if (creature is Creature { Script: AggroDefendScript script, Abilities.AnyCoolingDown: true } fighter
                && ((AiScript)Field(script, "_combat")!).State is CreatureCombatScript.CombatState.Combat)
            {
                HotReload(map, fighter, c => new CreatureCombatScript(NullLoggerFactory.Instance, c, map, world.Clock));
                return true;
            }
        }

        return false;
    }

    /// <summary>Four metres from the <paramref name="nth" /> creature: inside every forest creature's detection range.</summary>
    private static Vector3 Beside(MapInstance map, int nth) =>
        map.Creatures.Values.ElementAt(nth).Position + new Vector3(4f, 0f, 0f);

    // ---- The town -----------------------------------------------------------------------------------------------

    private static Side Town(bool scheduled)
    {
        var world = new ScenarioWorld(seed: 3);
        var side = new Side(world);
        MapInstance town = world.AddInstance();
        side.Add(town, scheduled);

        var template = new CreatureTemplate
        {
            Id = new CreatureTemplateId(1),
            DetectionRange = 8f,
            SpeedWalk = 2f,
            SpeedRun = 4f,
            BodyRemoveTimerSecs = 3,
        };
        Vector3 entry = TownNavmesh.EntrySpawn, square = TownNavmesh.LoopCentre;

        for (uint i = 0; i < 3; i++)
            Place(town, 1 + i, template, entry + new Vector3(-3f + 3f * i, 0f, 6f), c => new TownNpcScript(c, town));

        Creature patrol = Place(town, 10, template, square, c => new CreaturePatrolScript(NullLoggerFactory.Instance, c, town, world.Clock),
            path:
            [
                new PatrolPoint(square + new Vector3(4f, 0f, 0f), TimeSpan.FromSeconds(0.5)),
                new PatrolPoint(square + new Vector3(0f, 0f, 4f), TimeSpan.Zero),
                new PatrolPoint(square + new Vector3(-4f, 0f, 0f), TimeSpan.FromSeconds(1.2)),
            ]);
        Creature pathless = Place(town, 11, template, square + new Vector3(6f, 0f, 6f),
            c => new CreaturePatrolScript(NullLoggerFactory.Instance, c, town, world.Clock));
        Creature aggroA = Place(town, 20, template, entry + new Vector3(8f, 0f, -4f), Aggro(town, world));
        Creature aggroB = Place(town, 21, template, square + new Vector3(0f, 0f, -6f), Aggro(town, world), dodge: 100f);
        side.Counting = new CountingScript(Place(town, 30, template, entry + new Vector3(0f, 0f, -6f), _ => null), town);

        // Patrols pausing for different times, so several timed waits are due in turn.
        for (uint i = 0; i < 3; i++)
        {
            Vector3 start = square + new Vector3(-8f + 4f * i, 0f, 8f);
            var pause = TimeSpan.FromSeconds(0.1 + 0.35 * i);
            Place(town, 12 + i, template, start, c => new CreaturePatrolScript(NullLoggerFactory.Instance, c, town, world.Clock),
                path: [new PatrolPoint(start + new Vector3(2f, 0f, 0f), pause), new PatrolPoint(start, pause * 2)]);
        }

        // What a mod can do from a script, mid-step (#979): a script running between waiting creatures hits one before
        // it and one after it in the instance's order, gives a later creature a new script, and hits a later creature
        // that waits for an event alone. Each must act on the tick it would have when every creature ran every tick.
        Vector3 corner = entry + new Vector3(5f, 0f, 13f);
        Creature earlier = Place(town, 40, template, corner, Aggro(town, world));
        Creature meddling = Place(town, 41, template, corner + new Vector3(2f, 0f, 0f), _ => null);
        Creature later = Place(town, 42, template, corner + new Vector3(4f, 0f, 0f), Aggro(town, world));
        Creature swapped = Place(town, 43, template, corner + new Vector3(6f, 0f, 0f), c => new TownNpcScript(c, town));
        Creature waiting = Place(town, 44, template, corner + new Vector3(8f, 0f, 0f),
            c => new CreatureCombatScript(NullLoggerFactory.Instance, c, town, world.Clock));
        meddling.Script = new MeddlingScript(meddling, town, updates =>
        {
            switch (updates)
            {
                case 100:
                    town.CombatService.ApplyDamage(meddling, later, 1);
                    town.CombatService.ApplyDamage(meddling, earlier, 1);
                    break;
                case 300: swapped.Script = Aggro(town, world)(swapped); break;
                case 400: town.CombatService.ApplyDamage(meddling, waiting, 1); break;
            }
        });

        // Three characters, one always far from everything, so the town ticks throughout.
        CharacterEntity walker = world.NewCharacter(1, health: 1000);
        CharacterEntity attacker = world.NewCharacter(2);
        CharacterEntity bystander = world.NewCharacter(3);
        world.Join(town, walker, entry + new Vector3(-12f, 0f, -12f));
        ScenarioConnection attackerConnection = world.Join(town, attacker, square + new Vector3(10f, 0f, 10f));
        world.Join(town, bystander, square + new Vector3(12f, 0f, -12f));
        var combat = (CombatService)town.CombatService;

        side.Actions = tick =>
        {
            switch (tick)
            {
                case 30: walker.Position = aggroA.Position + new Vector3(3f, 0f, 0f); break;                   // found by A's search
                case 200: Place(town, 22, template, entry + new Vector3(-6f, 0f, -2f), Aggro(town, world)); break; // a command places one
                case 240: combat.ApplyDamage(attacker, patrol, 5); break;                                     // the patrol fights
                case 330: combat.EnterAuraCombat(bystander, aggroB); break;                                    // an attack for nothing
                case 420: combat.ApplyDamage(aggroA, walker, 5000); break;                                     // A kills its target
                case 520: town.RemoveCharacter(attackerConnection); break;                                     // the patrol's target leaves
                case 600: combat.RevivePlayer(walker, entry + new Vector3(-12f, 0f, -12f)); break;
                case 700: combat.ApplyDamage(bystander, aggroB, 5); break;                                     // dodged, mostly
                case 800: combat.ApplyDamage(patrol, aggroB, 1_000_000); break;                                // B dies (no character credited, as this world has no levels), its corpse goes
                case 900: HotReload(town, aggroA, Aggro(town, world)); break;
                case 1000: walker.Position = aggroA.Position + new Vector3(2f, 0f, 2f); break;
                case 1100:
                    patrol.PatrolPath = [new PatrolPoint(square, TimeSpan.FromSeconds(0.3)), new PatrolPoint(square + new Vector3(5f, 0f, 5f), TimeSpan.Zero)];
                    break;
                case 1150: pathless.PatrolPath = [new PatrolPoint(square + new Vector3(2f, 0f, 2f), TimeSpan.FromSeconds(0.2))]; break; // stood, now walks
                case 1200: walker.Position = entry + new Vector3(-12f, 0f, -12f); break;
                case 1300: world.Join(town, world.NewCharacter(4), square + new Vector3(1f, 0f, -1f)); break;  // arrives in B's old place
            }
        };

        return side;
    }

    private static Func<Creature, AiScript?> Aggro(MapInstance town, ScenarioWorld world) =>
        c => new AggroDefendScript(NullLoggerFactory.Instance, c, town, world.Clock);

    private static Creature Place(MapInstance map, uint id, CreatureTemplate template, Vector3 position,
        Func<Creature, AiScript?> script, PatrolPoint[]? path = null, float dodge = 0f)
    {
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            TemplateId = template.Id,
            Metadata = template,
            Name = $"Creature{id}",
            Position = position,
            Health = 100,
            CurrentHealth = 100,
            Speed = template.SpeedWalk,
            DodgePct = dodge,
        };
        if (path is not null)
            creature.PatrolPath = path;
        creature.Script = script(creature);
        map.AddCreature(creature);
        return creature;
    }

    /// <summary>As <c>World.ReattachScripts</c> does: out of the instance, a fresh script, back in.</summary>
    private static void HotReload(MapInstance map, Creature creature, Func<Creature, AiScript?> script)
    {
        map.RemoveCreature(creature);
        creature.Script = script(creature);
        map.AddCreature(creature);
    }

    // ---- Reading the creatures -----------------------------------------------------------------------------------

    private static CreatureView[] Views(MapInstance map)
    {
        var views = new CreatureView[map.Creatures.Count];
        int i = 0;
        foreach (ICreature creature in map.Creatures.Values)
            views[i++] = View(map, creature);
        return views;
    }

    /// <summary>
    /// A tick of about 1/60 s, a little longer or shorter each time and the same for both sides, as a server's ticks
    /// are: a float timer summed over varying deltas drifts if the scheduler sums them differently.
    /// </summary>
    private static TimeSpan Jittered(int tick) =>
        TimeSpan.FromTicks(ScenarioWorld.Dt.Ticks + ((tick * 7919) % 41 - 20) * 500);

    private static CreatureView View(MapInstance map, ICreature creature)
    {
        AiScript? script = creature.Script;
        AiScript? combat = script switch
        {
            AggroDefendScript or CreaturePatrolScript => (AiScript?)Field(script, "_combat"),
            CreatureCombatScript => script,
            _ => null,
        };
        object? target = combat is null ? null : typeof(CreatureCombatScript).GetField("_target", Private)!.GetValue(combat);
        AiScript? detector = script is AggroDefendScript ? (AiScript?)Field(script, "_detector") : null;
        var patrol = script as CreaturePatrolScript;

        return new CreatureView(
            creature.Position.x, creature.Position.y, creature.Position.z,
            creature.Velocity.x, creature.Velocity.z, creature.Orientation.y,
            creature.MoveState, creature.CurrentHealth,
            script?.GetType().Name, combat?.State, Describe(target), detector?.State,
            detector is null ? -1f : SearchTimer(map, creature, detector),
            Cooldowns(creature),
            patrol?.State, patrol is null ? -1 : (int)Field(patrol, "_currentWaypointIndex")!,
            patrol is not null && (bool)Field(patrol, "_pausing")!);
    }

    /// <summary>
    /// The seconds toward the detector's next search: its own timer, or, while the creature waits for its search, the
    /// timer its cohort keeps for it (the scheduler's, internal, read by name), which is what it is handed back.
    /// </summary>
    private static float SearchTimer(MapInstance map, ICreature creature, AiScript detector)
    {
        if (creature is Creature scheduled && Field(scheduled, "_schedule") is { } schedule
            && Field(schedule, "Wait")!.ToString() == "Search")
        {
            object scheduler = typeof(MapInstance).GetField("_scheduler", Private)!.GetValue(map)!;
            var cohorts = (Array)Field(scheduler, "_cohorts")!;
            object cohort = cohorts.GetValue((int)Field(schedule, "Cohort")!)!;
            return (float)cohort.GetType().GetField("Elapsed")!.GetValue(cohort)!;
        }

        return (float)Field(detector, "_searchTimer")!;
    }

    /// <summary>Each of a World-side creature's ability cooldowns, in its order.</summary>
    private static string Cooldowns(ICreature creature) =>
        creature is Creature { Abilities.All: { Count: > 0 } abilities }
            ? string.Join(",", abilities.Select(a => a.CooldownTimer.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))
            : string.Empty;

    private static string? Describe(object? unit) => unit switch
    {
        null => null,
        ICharacter character => $"character {character.Guid.Id}",
        ICreature creature => $"creature {creature.Name}",
        _ => unit.ToString(),
    };

    private static object? Field(object owner, string name)
    {
        for (Type? type = owner.GetType(); type is not null; type = type.BaseType)
        {
            if (type.GetField(name, Private | BindingFlags.Public) is { } field)
                return field.GetValue(owner);
        }

        throw new MissingFieldException(owner.GetType().Name, name);
    }

    /// <summary>How many of the instance's creatures run on every step now (the scheduler's, internal, read by name).</summary>
    private static int EveryStep(MapInstance map)
    {
        object scheduler = typeof(MapInstance).GetField("_scheduler", Private)!.GetValue(map)!;
        return (int)scheduler.GetType().GetProperty("EveryStep")!.GetValue(scheduler)!;
    }

    private readonly record struct CreatureView(
        float X, float Y, float Z, float VelocityX, float VelocityZ, float Yaw, MoveState Move, uint Health,
        string? Script, object? Combat, string? Target, object? Detector, float SearchTimer, string Cooldowns,
        object? Patrol, int Waypoint, bool Pausing);

    /// <summary>One run of a world: its instances, what is done to them on which tick, and the counting script.</summary>
    private sealed class Side(ScenarioWorld world) : IDisposable
    {
        public ScenarioWorld World { get; } = world;

        public List<MapInstance> Maps { get; } = [];

        public Action<int>? Actions { get; set; }

        public CountingScript? Counting { get; set; }

        /// <summary>Whether a fighting forest creature was hot reloaded as a plain combat script (<see cref="ReloadAsPlainCombat" />).</summary>
        public bool Swapped { get; set; }

        public void Add(MapInstance map, bool scheduled)
        {
            if (!scheduled)
                typeof(MapInstance).GetMethod("UpdateEveryCreatureEveryTick", Private)!.Invoke(map, null);
            Maps.Add(map);
        }

        public void Act(int tick) => Actions?.Invoke(tick);

        public void Dispose() => World.Dispose();
    }

    /// <summary>
    /// A custom script that opts in and stays active, doing what <paramref name="act" /> does with the count of its own
    /// Updates: the same count on both sides, since it runs on every tick on both.
    /// </summary>
    private sealed class MeddlingScript(ICreature creature, ISimulationContext context, Action<int> act)
        : AiScript(creature, context)
    {
        private int _updates;

        public override object State { get; set; } = 0;

        public override bool UpdatesWhenDue => true;

        public override void Update(TimeSpan deltaTime) => act(++_updates);

        protected override bool ShouldRun() => false;
    }

    /// <summary>A custom script that opts in, waits as it is told, and records when it is called and what it heard.</summary>
    private sealed class WaitingScript(ICreature creature, ISimulationContext context, TimeSpan wait) : AiScript(creature, context)
    {
        public List<TimeSpan> Updates { get; } = [];

        public int Heard { get; private set; }

        public override object State { get; set; } = 0;

        public override bool UpdatesWhenDue => true;

        public override TimeSpan NextUpdateIn => wait;

        public override void Update(TimeSpan deltaTime) => Updates.Add(Context.CreatureClock);

        public override void OnHit(IUnit attacker, uint damage) => Heard++;

        public override void OnAttacked(IUnit attacker) => Heard++;

        public override void OnCharacterLeft(ICharacter character) => Heard++;

        protected override bool ShouldRun() => false;
    }

    /// <summary>A custom script written against the per-tick contract: it does not opt in, and counts its Updates.</summary>
    private sealed class CountingScript : AiScript
    {
        public CountingScript(Creature creature, ISimulationContext context) : base(creature, context)
        {
            creature.Script = this;
        }

        public int Updates { get; private set; }

        public override object State { get; set; } = 0;

        public override void Update(TimeSpan deltaTime) => Updates++;

        protected override bool ShouldRun() => false;
    }
}
