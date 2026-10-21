using System.Reflection;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.World.Parties;
using Avalon.Domain.World;
using Avalon.Network.Packets.State;
using Avalon.World.ChunkLayouts;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
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
/// attack that dealt nothing), killing a character, killed, hot reloaded, one placed by a command, and a script that
/// answers that it needs every tick, which must get every tick.
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

        // A script that answers that it needs every tick is called on every tick, scheduled or not.
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

    /// <summary>
    /// The scheduler against a plain model of the contract (#979), over many creatures answering at random: the next tick
    /// (zero, or a negative time), a time (short or long, some due on the same tick), an event alone (Infinite, or a time
    /// too long for the clock); woken before the step, or mid-step by a script running between them (those after it in
    /// the instance's order run that tick, those before it the next); removed while waiting for a time, and added again;
    /// under ticks of varying length. After every tick, each creature has run exactly when the model says it must: so the
    /// timed waits' heap hands back every due creature on its tick and none early, whatever was added and removed.
    /// </summary>
    [Fact]
    public void Run_each_creature_exactly_when_its_answer_and_its_wakes_say()
    {
        const int Count = 300, Ticks = 1500;
        var random = new Random(979);
        using var world = new ScenarioWorld();
        MapInstance town = world.AddInstance();
        world.Join(town, world.NewCharacter(1), TownNavmesh.LoopCentre);
        var template = new CreatureTemplate { Id = new CreatureTemplateId(1) };
        var model = new Expected[Count];
        var creatures = new Creature[Count];
        bool[] ran = new bool[Count];
        int meddler = Count / 2;

        TimeSpan RandomAnswer() => random.Next(100) switch
        {
            < 25 => TimeSpan.Zero,
            < 30 => TimeSpan.FromTicks(-random.Next(1, 200_000)),
            < 38 => Timeout.InfiniteTimeSpan,
            < 40 => TimeSpan.MaxValue,
            < 45 => TimeSpan.FromTicks(ScenarioWorld.Dt.Ticks * random.Next(1, 4)),
            _ => TimeSpan.FromTicks(random.Next(1, 120 * 166_667)),
        };

        void Meddle()
        {
            List<ICreature> order = [.. town.Creatures.Values];
            int me = order.IndexOf(creatures[meddler]);
            for (int j = random.Next(4); j > 0; j--)
            {
                int other = random.Next(Count);
                if (other == meddler || !model[other].In)
                    continue;

                creatures[other].PatrolPath = []; // A wake, as a mod can give from its script.
                if (order.IndexOf(creatures[other]) > me)
                    model[other].WokenThisTick = true;
                else
                    model[other].WokenNextTick = true;
            }
        }

        for (int i = 0; i < Count; i++)
        {
            int id = i;
            creatures[i] = Place(town, (uint)(100 + i), template, TownNavmesh.EntrySpawn, c => new AnsweringScript(c, town, () =>
            {
                ran[id] = true;
                model[id].Ran = town.CreatureClock;
                model[id].Answer = id == meddler ? TimeSpan.Zero : RandomAnswer();
                if (id == meddler)
                    Meddle();
            }, () => model[id].Answer));
            model[i] = new Expected { In = true, MustRun = true };
        }

        for (int tick = 0; tick < Ticks; tick++)
        {
            // Before the tick: wakes, removals (some waiting for a time) and creatures added again.
            for (int j = 0; j < 6; j++)
            {
                int other = random.Next(Count);
                if (other == meddler)
                    continue;

                int action = random.Next(10);
                if (action < 6 && model[other].In)
                {
                    creatures[other].PatrolPath = [];
                    model[other].WokenThisTick = true;
                }
                else if (action < 8 && model[other].In)
                {
                    town.RemoveCreature(creatures[other]);
                    model[other].In = false;
                }
                else if (!model[other].In)
                {
                    town.AddCreature(creatures[other]);
                    model[other] = model[other] with { In = true, MustRun = true };
                }
            }

            Array.Clear(ran);
            world.Tick(TimeSpan.FromTicks(ScenarioWorld.Dt.Ticks + random.Next(-20_000, 20_000)));
            TimeSpan now = town.CreatureClock;
            for (int i = 0; i < Count; i++)
            {
                ref Expected expected = ref model[i];
                bool due = expected.In && (expected.MustRun || expected.WokenThisTick || i == meddler
                                           || DueByAnswer(expected.Last, expected.LastRan, now));
                Assert.True(ran[i] == due,
                    $"Tick {tick}, creature {i}: ran {ran[i]}, should have {due} (last answer {expected.Last} at {expected.LastRan}, now {now}, woken {expected.WokenThisTick}, joined {expected.MustRun})");

                expected.MustRun = expected.In && expected.WokenNextTick;
                expected.WokenNextTick = false;
                expected.WokenThisTick = false;
                if (ran[i])
                {
                    expected.Last = expected.Answer;
                    expected.LastRan = expected.Ran;
                }
            }
        }
    }

    /// <summary>
    /// The contract on <see cref="AiScript.NextUpdateIn" />: zero or less (but Infinite) is the next tick; Infinite, or a
    /// time too long for the clock, an event alone; any other time, the first tick at which that much has passed.
    /// </summary>
    private static bool DueByAnswer(TimeSpan answer, TimeSpan answeredAt, TimeSpan now)
    {
        if (answer == Timeout.InfiniteTimeSpan)
            return false;
        if (answer <= TimeSpan.Zero)
            return true;
        if (answer >= TimeSpan.MaxValue - answeredAt)
            return false;
        return now >= answeredAt + answer;
    }

    /// <summary>What the model knows of one creature: whether it is in the instance, why it must run, and its last answer.</summary>
    private record struct Expected
    {
        public bool In;
        public bool MustRun;
        public bool WokenThisTick;
        public bool WokenNextTick;
        public TimeSpan Ran;
        public TimeSpan Answer;
        public TimeSpan LastRan;
        public TimeSpan Last;
    }

    /// <summary>A script whose Update and answer are the test's.</summary>
    private sealed class AnsweringScript(ICreature creature, ISimulationContext context, Action update, Func<TimeSpan> answer)
        : AiScript(creature, context)
    {
        public override object State { get; set; } = 0;

        public override TimeSpan NextUpdateIn => answer();

        public override void Update(TimeSpan deltaTime) => update();

        protected override bool ShouldRun() => false;
    }

    /// <summary>
    /// A subclass of a built-in that does not answer <see cref="AiScript.NextUpdateIn" /> itself is called on every tick
    /// (#979): the built-ins wait only as their own type, so no subclass inherits a wait its own Update may not keep.
    /// Each stands idle, where the built-in itself would wait.
    /// </summary>
    [Theory]
    [InlineData(nameof(AggroDefendScript))]
    [InlineData(nameof(CreatureCombatScript))]
    [InlineData(nameof(CreaturePatrolScript))]
    [InlineData(nameof(TownNpcScript))]
    public void Call_a_subclass_of_a_built_in_on_every_tick_unless_it_answers_itself(string builtIn)
    {
        using var world = new ScenarioWorld();
        MapInstance town = world.AddInstance();
        world.Join(town, world.NewCharacter(1), TownNavmesh.LoopCentre);
        var template = new CreatureTemplate { Id = new CreatureTemplateId(1), DetectionRange = 5f };
        ICountsUpdates counted = null!;
        Place(town, 1, template, TownNavmesh.EntrySpawn, c =>
        {
            AiScript script = builtIn switch
            {
                nameof(AggroDefendScript) => new AggroSubclass(c, town),
                nameof(CreatureCombatScript) => new CombatSubclass(c, town),
                nameof(CreaturePatrolScript) => new PatrolSubclass(c, town),
                _ => new TownNpcSubclass(c, town),
            };
            counted = (ICountsUpdates)script;
            return script;
        });

        for (int i = 0; i < 120; i++)
            world.Tick();

        Assert.Equal(120, counted.Updates);
    }

    private interface ICountsUpdates
    {
        int Updates { get; }
    }

    private sealed class AggroSubclass(Creature creature, ISimulationContext context)
        : AggroDefendScript(creature, context, new CreatureCombatScript(NullLoggerFactory.Instance, creature, context)),
            ICountsUpdates
    {
        public int Updates { get; private set; }

        public override void Update(TimeSpan deltaTime)
        {
            Updates++;
            base.Update(deltaTime);
        }
    }

    private sealed class CombatSubclass(Creature creature, ISimulationContext context)
        : CreatureCombatScript(NullLoggerFactory.Instance, creature, context), ICountsUpdates
    {
        public int Updates { get; private set; }

        public override void Update(TimeSpan deltaTime)
        {
            Updates++;
            base.Update(deltaTime);
        }
    }

    private sealed class PatrolSubclass(Creature creature, ISimulationContext context)
        : CreaturePatrolScript(NullLoggerFactory.Instance, creature, context), ICountsUpdates
    {
        public int Updates { get; private set; }

        public override void Update(TimeSpan deltaTime)
        {
            Updates++;
            base.Update(deltaTime);
        }
    }

    private sealed class TownNpcSubclass(Creature creature, ISimulationContext context)
        : TownNpcScript(creature, context), ICountsUpdates
    {
        public int Updates { get; private set; }

        public override void Update(TimeSpan deltaTime)
        {
            Updates++;
            base.Update(deltaTime);
        }
    }

    // ---- The forest ---------------------------------------------------------------------------------------------

    private static Side Forest(bool scheduled)
    {
        var world = ScenarioWorld.CreateWithReferenceData(seed: 1);
        var side = new Side(world);
        FighterHandlers handlers = LoopFighter.Handlers(world);
        CharacterClass[] classes = [CharacterClass.Warrior, CharacterClass.Wizard, CharacterClass.Hunter, CharacterClass.Cleric];
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

    // ---- The search filter (#982) -------------------------------------------------------------------------------

    /// <summary>
    /// A due search that no character can satisfy is skipped (#982), and that changes nothing: two instances are driven
    /// side by side through the same seeded changes, one skipping and one running every due search
    /// (<c>MapInstance.SearchEveryDueCreature</c>), and after every tick each creature must stand, detect, target and wait
    /// alike, and each client must have been sent the same packets. The changes are the ones the filter can get wrong:
    /// characters placed at a creature's range on X/Z to the float (on it, a float inside, a float beyond) and above or
    /// below it, positions with a component not finite (which the search's NaN distance lets through to its line of
    /// sight, here always clear for those), off the layout and on cell edges, characters dying, reviving, leaving, coming
    /// back and entering a second instance without leaving this one (then not indexed here), and a script in the middle
    /// of the instance's order that, mid-step, moves characters beside creatures after it (which must be found on that
    /// tick when their search is due on it) and hits creatures (woken before or after their turn). Once per seed each, two
    /// cases are staged rather than left to chance (<see cref="Stage" />): a character alone exactly at a due creature's
    /// range on one axis, moved there within its cell after the grid bound its characters' bounds, and a character at a
    /// due creature's range that another instance indexes. Each weakening of the filter tried fails it, on every seed: a
    /// strict comparison at the range, the characters' bounds not widened by the range, the bounds left stale by a move
    /// within a cell (Debug and Release), the characters in no cell or not indexed left out, a cell's character count not
    /// kept, and the timer of a skipped search not reset; on some seeds, a reach too short for the cells, and the
    /// question asked when a cohort comes due rather than at each creature's turn.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Detect_alike_whether_or_not_searches_no_character_can_satisfy_are_skipped(int seed)
    {
        var random = new Random(seed);
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        ChunkLayout layout = FilterLayout();
        using MapInstance filtered = TestMapInstances.Build(MapInstanceClients.NewWorld(), navigator: new ClearSight(), time: clock, layout: layout);
        using MapInstance reference = TestMapInstances.Build(MapInstanceClients.NewWorld(), navigator: new ClearSight(), time: clock, layout: layout);
        typeof(MapInstance).GetMethod("SearchEveryDueCreature", Private)!.Invoke(reference, null);
        using MapInstance filteredElsewhere = TestMapInstances.Build(MapInstanceClients.NewWorld(), time: clock, layout: layout);
        using MapInstance referenceElsewhere = TestMapInstances.Build(MapInstanceClients.NewWorld(), time: clock, layout: layout);

        // Creatures on whole and half metres, so a character placed at a range along an axis is at it exactly; the
        // meddling script in the middle of the instance's order.
        float[] ranges = [8f, 0f, 12f, 20f]; // 0: the default, 10 m
        var episodes = new Episodes();
        var creatures = new List<Pair<Creature>>();
        var characters = new List<Pair<MapInstanceClient>>();
        for (uint id = 1; id <= 41; id++)
        {
            if (id == 21)
            {
                filtered.AddCreature(Meddler(filtered, id, new Random(seed), episodes, characters, creatures, f => f.Filtered));
                reference.AddCreature(Meddler(reference, id, new Random(seed), episodes, characters, creatures, f => f.Reference));
                continue;
            }

            var template = new CreatureTemplate
            {
                Id = new CreatureTemplateId(id),
                DetectionRange = ranges[id % ranges.Length],
                // Fast, so a creature that gave up a chase is home and searching again within a few seconds.
                SpeedWalk = 10f,
                SpeedRun = 20f,
            };
            var at = new Vector3(random.Next(-60, 480) / 2f, 0.5f * random.Next(0, 3), random.Next(-200, 260) / 2f);
            creatures.Add(new Pair<Creature>(
                Place(filtered, id, template, at, c => new AggroDefendScript(NullLoggerFactory.Instance, c, filtered, clock)),
                Place(reference, id, template, at, c => new AggroDefendScript(NullLoggerFactory.Instance, c, reference, clock))));
        }

        for (uint id = 982_201; id <= 982_206; id++)
        {
            var pair = new Pair<MapInstanceClient>(MapInstanceClients.Join(filtered, id), MapInstanceClients.Join(reference, id)) { In = true };
            characters.Add(pair);
            Move(pair, Somewhere(random));
        }

        int detectedNotFinite = 0;
        for (int tick = 1; tick <= 1200; tick++)
        {
            // Once each per seed, the two cases a filter just too tight would miss: a character alone exactly at a due
            // creature's range on one axis, and one at a due creature's range that another instance indexes.
            if (tick >= 200 && episodes.Alone is null)
                episodes.Alone = Stage(episodes, creatures, characters, filtered, reference, filteredElsewhere, referenceElsewhere, false);
            else if (tick >= 600 && episodes.Elsewhere is null)
                episodes.Elsewhere = Stage(episodes, creatures, characters, filtered, reference, filteredElsewhere, referenceElsewhere, true);

            Change(random, episodes, creatures, characters, filtered, reference, filteredElsewhere, referenceElsewhere);

            int[] from = characters.Select(c => c.Filtered.Sent.Count).ToArray();
            int[] fromReference = characters.Select(c => c.Reference.Sent.Count).ToArray();
            filtered.Update(ScenarioWorld.Dt);
            reference.Update(ScenarioWorld.Dt);
            clock.Advance(ScenarioWorld.Dt);

            CreatureView[] expected = Views(reference), actual = Views(filtered);
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                if (!expected[i].Equals(actual[i]))
                    Assert.Fail($"seed {seed}, tick {tick}, creature {i}:\n every search {expected[i]}\n filtered     {actual[i]}");
                if (expected[i].Target is { } target && target.StartsWith("character", StringComparison.Ordinal)
                    && characters.Find(c => $"character {c.Reference.Character.Guid.Id}" == target) is { } aggroed
                    && !IsFinite(aggroed.Reference.Character.Position))
                {
                    detectedNotFinite++;
                }
            }

            for (int c = 0; c < characters.Count; c++)
            {
                Assert.True(Packets(characters[c].Reference.Sent.Skip(fromReference[c])) == Packets(characters[c].Filtered.Sent.Skip(from[c])),
                    $"seed {seed}, tick {tick}: client {characters[c].Reference.Character.Guid.Id} was sent other packets");
            }

            episodes.Alone?.See(reference);
            episodes.Elsewhere?.See(reference);
        }

        // Each staged case happened and was found by the search the filter must not skip.
        Assert.True(episodes.Alone is { Found: true }, $"seed {seed}: the character alone at a creature's range was {(episodes.Alone is null ? "never staged" : "not found")}");
        Assert.True(episodes.Elsewhere is { Found: true }, $"seed {seed}: the character indexed elsewhere was {(episodes.Elsewhere is null ? "never staged" : "not found")}");

        // The equivalence would hold trivially if nothing was skipped, or if nothing was found.
        long all = Searches(reference), skipped = all - Searches(filtered);
        Assert.True(skipped > all / 10 && skipped < all, $"seed {seed}: {skipped} of {all} searches skipped");
        Assert.True(detectedNotFinite > 0, $"seed {seed}: no creature ever held a character standing nowhere as its target");
    }

    private static void Change(Random random, Episodes episodes, List<Pair<Creature>> creatures, List<Pair<MapInstanceClient>> characters,
        MapInstance filtered, MapInstance reference, MapInstance filteredElsewhere, MapInstance referenceElsewhere)
    {
        // While a staged case plays out, nothing else changes.
        if (episodes.Quiet > 0)
        {
            episodes.Quiet--;
            return;
        }

        // A character placed at a creature's range stays there for a search period, so the creature's search comes due
        // while it does; the others wander.
        foreach (Pair<MapInstanceClient> character in characters)
        {
            if (character.Pinned > 0)
                character.Pinned--;
            else if (random.NextDouble() < 0.2)
                Move(character, Step(character.Reference.Character.Position, random));
        }

        if (random.Next(6) == 0)
        {
            Creature near = creatures[random.Next(creatures.Count)].Reference;
            Pair<MapInstanceClient> character = characters[random.Next(characters.Count)];
            Move(character, AtRange(near, random));
            character.Pinned = 70;
        }

        if (random.Next(20) == 0)
            Move(characters[random.Next(characters.Count)], NotFinite(random));

        // Now and then a character enters a second instance without leaving this one, which then no longer indexes it, at
        // a creature's range, where only its position read live finds it; it comes back about a search period later and
        // through this instance again, which then indexes it again. Until then no search is skipped here.
        if (random.Next(150) == 0 && characters[random.Next(characters.Count)] is { Elsewhere: false } leaving)
        {
            filteredElsewhere.AddCharacter(leaving.Filtered.Connection);
            referenceElsewhere.AddCharacter(leaving.Reference.Connection);
            leaving.Elsewhere = true;
            Move(leaving, AtRange(creatures[random.Next(creatures.Count)].Reference, random));
            leaving.Pinned = 70;
        }

        foreach (Pair<MapInstanceClient> character in characters)
        {
            if (character.Elsewhere && random.Next(60) == 0)
            {
                filteredElsewhere.RemoveCharacter(character.Filtered.Connection);
                referenceElsewhere.RemoveCharacter(character.Reference.Connection);
                character.Elsewhere = false;
                if (character.In)
                {
                    filtered.RemoveCharacter(character.Filtered.Connection);
                    reference.RemoveCharacter(character.Reference.Connection);
                    filtered.AddCharacter(character.Filtered.Connection);
                    reference.AddCharacter(character.Reference.Connection);
                }
            }
        }

        switch (random.Next(80))
        {
            case 0 or 2 or 3 or 4:
            {
                Pair<MapInstanceClient> character = characters[random.Next(characters.Count)];
                bool dead = !character.Reference.Character.IsDead;
                character.Filtered.Character.IsDead = dead;
                character.Reference.Character.IsDead = dead;
                break;
            }
            case 1:
            {
                Pair<MapInstanceClient> character = characters[random.Next(characters.Count)];
                if (character.In)
                {
                    filtered.RemoveCharacter(character.Filtered.Connection);
                    reference.RemoveCharacter(character.Reference.Connection);
                }
                else
                {
                    filtered.AddCharacter(character.Filtered.Connection);
                    reference.AddCharacter(character.Reference.Connection);
                }

                character.In = !character.In;
                break;
            }
        }
    }

    /// <summary>
    /// Stages one of the cases a filter just too tight would miss, at a creature waiting for its search with the search
    /// due within the next 9 ticks (none yet: null, staged on a later tick). The first character is put, alive and in this
    /// instance, at the creature's height and exactly at its range along X (<paramref name="elsewhere" /> false: then
    /// indexed by this instance alone), or halfway to it while also in the second instance (true: not indexed here). Every
    /// other character is put back into this instance's index and lined up far along +X, beyond every creature, so the
    /// creature is just outside the bounds of the rest; and nothing else changes, the meddling script included, until the
    /// search has run.
    /// </summary>
    private static Episode? Stage(Episodes episodes, List<Pair<Creature>> creatures, List<Pair<MapInstanceClient>> characters,
        MapInstance filtered, MapInstance reference, MapInstance filteredElsewhere, MapInstance referenceElsewhere, bool elsewhere)
    {
        Pair<Creature>? due = creatures.Find(c =>
        {
            CreatureView view = View(reference, c.Reference);
            float range = c.Reference.Metadata.DetectionRange > 0f ? c.Reference.Metadata.DetectionRange : 10f;
            return view.Combat is CreatureCombatScript.CombatState.None
                   && view.Detector is CreatureRangeDetectorScript.RangeDetectionState.Searching
                   && view.SearchTimer > 0.85f && !(view.Z > 10f && view.Z < 25f) // in sight (ClearSight)
                   && (view.X + range) % 30f is >= 0f and < 27f; // 3 m further along X in the same cell (the grid's X origin is 0)
        });
        if (due is null)
            return null;

        Vector3 at = due.Reference.Position;
        float range = due.Reference.Metadata.DetectionRange > 0f ? due.Reference.Metadata.DetectionRange : 10f;
        for (int i = 0; i < characters.Count; i++)
        {
            Pair<MapInstanceClient> character = characters[i];
            if (character.Elsewhere)
            {
                filteredElsewhere.RemoveCharacter(character.Filtered.Connection);
                referenceElsewhere.RemoveCharacter(character.Reference.Connection);
                character.Elsewhere = false;
            }

            if (character.In)
            {
                filtered.RemoveCharacter(character.Filtered.Connection);
                reference.RemoveCharacter(character.Reference.Connection);
            }

            filtered.AddCharacter(character.Filtered.Connection);
            reference.AddCharacter(character.Reference.Connection);
            character.In = true;
            if (i > 0)
                Move(character, new Vector3(at.x + range + 400f + 10f * i, at.y, at.z));
        }

        Pair<MapInstanceClient> staged = characters[0];
        staged.Filtered.Character.IsDead = false;
        staged.Reference.Character.IsDead = false;
        if (elsewhere)
        {
            filteredElsewhere.AddCharacter(staged.Filtered.Connection);
            referenceElsewhere.AddCharacter(staged.Reference.Connection);
            staged.Elsewhere = true;
        }

        else
        {
            // First 3 m beyond the range, in the same cell, where the grid binds its characters' bounds; then the move to
            // the range, within the cell, which only the version a move moves tells the bounds of.
            Move(staged, new Vector3(at.x + range + 3f, at.y, at.z));
            BindCharacterBounds(filtered);
            BindCharacterBounds(reference);
        }

        Move(staged, new Vector3(at.x + (elsewhere ? range / 2f : range), at.y, at.z));
        episodes.Quiet = 12;
        return new Episode(due, $"character {staged.Reference.Character.Guid.Id}");
    }

    /// <summary>
    /// Has the instance's spatial grid bind its characters' bounds now, as a creature's search question does (the grid's,
    /// internal, asked by name, of a point that changes nothing).
    /// </summary>
    private static void BindCharacterBounds(MapInstance map)
    {
        object grid = typeof(MapInstance).GetField("_members", Private)!.GetValue(map)!;
        grid.GetType().GetMethod("MayHaveCharacterWithin")!.Invoke(grid, [Vector3.zero, 1f]);
    }

    /// <summary>
    /// A character at a creature's range: along an axis on it, a float inside or beyond it, or at a random angle around it,
    /// level with the creature or above or below it (so within range on X/Z but beyond it in 3D).
    /// </summary>
    private static Vector3 AtRange(Creature creature, Random random)
    {
        float range = creature.Metadata.DetectionRange > 0f ? creature.Metadata.DetectionRange : 10f;
        float distance = random.Next(5) switch
        {
            0 => range,
            1 => MathF.BitDecrement(range),
            2 => MathF.BitIncrement(range),
            _ => range * (float)(0.5 + random.NextDouble() * 0.7),
        };
        float y = random.Next(3) switch { 0 => 0f, 1 => (float)(random.NextDouble() * 6 - 3), _ => range * 0.75f };
        Vector3 at = creature.Position;
        return random.Next(3) switch
        {
            0 => new Vector3(at.x + distance, at.y + y, at.z),
            1 => new Vector3(at.x, at.y + y, at.z - distance),
            _ => new Vector3(at.x + distance * MathF.Cos(random.Next(360)), at.y + y, at.z + distance * MathF.Sin(random.Next(360))),
        };
    }

    private static Vector3 Somewhere(Random random) => random.Next(6) switch
    {
        0 => new Vector3(-400f + random.Next(100), 0f, 50f),                 // off the layout, the clamped edge cells
        1 => new Vector3(30f * random.Next(-1, 9), 1f, 30f * random.Next(-3, 6)), // on a cell edge
        _ => new Vector3((float)(random.NextDouble() * 290 - 40), (float)(random.NextDouble() * 2), (float)(random.NextDouble() * 220 - 100)),
    };

    private static Vector3 Step(Vector3 from, Random random) =>
        IsFinite(from) && Math.Abs(from.x) < 1e4f
            ? new Vector3(from.x + (float)(random.NextDouble() * 6 - 3), from.y, from.z + (float)(random.NextDouble() * 6 - 3))
            : Somewhere(random);

    private static Vector3 NotFinite(Random random)
    {
        float bad = random.Next(4) switch { 0 or 1 => float.NaN, 2 => float.PositiveInfinity, _ => float.NegativeInfinity };
        return random.Next(3) switch
        {
            0 => new Vector3(bad, 0f, 10f),
            1 => new Vector3(10f, bad, 10f),
            _ => new Vector3(10f, 0f, bad),
        };
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

    private static void Move(Pair<MapInstanceClient> character, Vector3 position)
    {
        character.Filtered.Character.Position = position;
        character.Reference.Character.Position = position;
    }

    /// <summary>
    /// A creature whose script needs every tick and, mid-step, moves a character beside a creature after it in the
    /// instance's order, or hits one; the same seeded choices on both sides, as it runs on every tick on both.
    /// </summary>
    private static Creature Meddler(MapInstance map, uint id, Random random, Episodes episodes,
        List<Pair<MapInstanceClient>> characters, List<Pair<Creature>> creatures,
        Func<Pair<MapInstanceClient>, MapInstanceClient> sideOf)
    {
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Metadata = new CreatureTemplate { Id = new CreatureTemplateId(id) },
            Position = new Vector3(-300f, 0f, -300f),
            Health = 100,
            CurrentHealth = 100,
        };
        creature.Script = new MeddlingScript(creature, map, _ =>
        {
            if (characters.Count == 0 || episodes.Quiet > 0)
                return;

            int roll = random.Next(10);
            if (roll < 3)
            {
                // A creature after this one in the order (the creatures list holds the first 20 before it).
                Creature beside = creatures[20 + random.Next(creatures.Count - 20)].Reference;
                Vector3 position = beside.Position + new Vector3(2f, 0f, 1f);
                sideOf(characters[random.Next(characters.Count)]).Character.Position = position;
            }
            else if (roll == 3)
            {
                int target = random.Next(creatures.Count);
                ICreature hit = map.Creatures[creatures[target].Reference.Guid];
                map.CombatService.ApplyDamage(creature, hit, 1);
            }
        });
        return creature;
    }

    private static string Packets(IEnumerable<Avalon.Network.Packets.Abstractions.OutboundPacket> packets) =>
        string.Join("\n", packets.Select(p => $"{p.Header.Type} {Convert.ToBase64String(p.PayloadMemory.Span)}"));

    /// <summary>The searches the instance's creatures' range detectors ran (their counts, internal, read by name).</summary>
    private static long Searches(MapInstance map) => map.Creatures.Values
        .Select(c => c.Script is AggroDefendScript script ? (AiScript)Field(script, "_detector")! : null)
        .Sum(d => d is null ? 0L : (long)typeof(CreatureRangeDetectorScript).GetProperty("Searches", Private)!.GetValue(d)!);

    // A path of 30 m chunks with a branch, so the grid has many cells, most of them off the layout.
    private static ChunkLayout FilterLayout()
    {
        (short X, short Z)[] cells = [(0, 0), (1, 0), (2, 0), (2, 1), (3, 1), (4, 1), (4, 2), (4, 3), (5, 3), (6, 3), (1, -1), (1, -2)];
        var chunks = cells
            .Select(c => new PlacedChunk(new ChunkTemplateId(1), c.X, c.Z, 0, new Vector3(c.X * 30f, 0f, c.Z * 30f)))
            .ToList();
        return new ChunkLayout(0, chunks, chunks[0], null, [], Vector3.zero, 30f);
    }

    /// <summary>The staged cases (<see cref="Stage" />), and how many more ticks nothing else changes for the one playing out.</summary>
    private sealed class Episodes
    {
        public int Quiet { get; set; }
        public Episode? Alone { get; set; }
        public Episode? Elsewhere { get; set; }
    }

    /// <summary>A staged case: its creature, the character it must find, and whether it did.</summary>
    private sealed class Episode(Pair<Creature> creature, string character)
    {
        public bool Found { get; private set; }

        public void See(MapInstance reference) => Found |= View(reference, creature.Reference).Target == character;
    }

    /// <summary>One object on each side: in the instance that skips searches, and in the one that runs them all.</summary>
    private sealed class Pair<T>(T filtered, T reference)
    {
        public T Filtered { get; } = filtered;
        public T Reference { get; } = reference;
        public bool In { get; set; }
        public bool Elsewhere { get; set; }
        public int Pinned { get; set; }
    }

    /// <summary>
    /// A navigator over no navmesh: a path is a straight line to its end, every ray reaches it, the ground is where it was
    /// asked, and the line of sight is clear but across a band of Z (from 10 to 25 m), so a character in range can be out
    /// of sight. A position with a component that is not finite is in sight (no comparison holds for NaN): the search's
    /// own arithmetic then takes such a character, which the filter must never skip.
    /// </summary>
    private sealed class ClearSight : IMapNavigator
    {
        public List<Vector3> FindPath(Vector3 start, Vector3 end) => IsFinite(end) ? [end] : [];

        public bool HasVisibility(Vector3 start, Vector3 end) => !(end.z > 10f && end.z < 25f);

        public Vector3 RaycastWalkable(Vector3 from, Vector3 to) => to;

        public float SampleGroundHeight(float x, float y, float z) => y;

        public object? Mesh => null;
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
    /// A custom script that stays active, doing what <paramref name="act" /> does with the count of its own
    /// Updates: the same count on both sides, since it runs on every tick on both.
    /// </summary>
    private sealed class MeddlingScript(ICreature creature, ISimulationContext context, Action<int> act)
        : AiScript(creature, context)
    {
        private int _updates;

        public override object State { get; set; } = 0;

        public override TimeSpan NextUpdateIn => TimeSpan.Zero;

        public override void Update(TimeSpan deltaTime) => act(++_updates);

        protected override bool ShouldRun() => false;
    }

    /// <summary>A custom script that waits as it is told, and records when it is called and what it heard.</summary>
    private sealed class WaitingScript(ICreature creature, ISimulationContext context, TimeSpan wait) : AiScript(creature, context)
    {
        public List<TimeSpan> Updates { get; } = [];

        public int Heard { get; private set; }

        public override object State { get; set; } = 0;

        public override TimeSpan NextUpdateIn => wait;

        public override void Update(TimeSpan deltaTime) => Updates.Add(Context.CreatureClock);

        public override void OnHit(IUnit attacker, uint damage) => Heard++;

        public override void OnAttacked(IUnit attacker) => Heard++;

        public override void OnCharacterLeft(ICharacter character) => Heard++;

        protected override bool ShouldRun() => false;
    }

    /// <summary>A custom script that needs every tick, as every script had before #979, and counts its Updates.</summary>
    private sealed class CountingScript : AiScript
    {
        public CountingScript(Creature creature, ISimulationContext context) : base(creature, context)
        {
            creature.Script = this;
        }

        public int Updates { get; private set; }

        public override object State { get; set; } = 0;

        public override TimeSpan NextUpdateIn => TimeSpan.Zero;

        public override void Update(TimeSpan deltaTime) => Updates++;

        protected override bool ShouldRun() => false;
    }
}
