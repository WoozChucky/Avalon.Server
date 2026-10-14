using System.Runtime.CompilerServices;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Inventory;
using Avalon.World.Parties;
using Avalon.World.Public.Characters;

namespace Avalon.World.Testing.Scenarios;

/// <summary>
/// Parties fighting through forests: four forest instances on the fixed seeds 1 to 4, each built as its party's forest
/// is in production (<see cref="ScenarioWorld.NewForestInstance" />: the World server's generator, placement, spawn
/// table and real AI over the World server's reference data), with a party of three level 1 characters in each, joined
/// at the entry; their classes cycle over the four (warrior, wizard, hunter, healer) across the whole roster, so three
/// parties in four carry a healer. Every player is a <see cref="LoopFighter" />: it walks to the nearest live creature
/// through the real input handler and casts its class's basic ability at it through the real cast handler, and a healer
/// heals its party first when one of them is hurt. What is measured is a fight: the forests' creature AI (about 200
/// creatures each, their AI run when active or due as in production, #979), casts, projectiles, hits, heals, threat
/// and encounters, deaths and revives, kills with their real loot and experience shared by the party, and the state
/// broadcast of all of it.
/// </summary>
/// <remarks>
/// <para>
/// A fight never settles, so the scenario has a fixed length (<see cref="Length" />): <see cref="WarmupTicks" /> ticks
/// from the build (the walk to the first creatures, and every character's first ability-amount update), then
/// <see cref="MeasuredTicks" /> measured ticks, one minute at 60 Hz. Everything it reads is seeded or on the world's
/// clock: the layouts, placement, combat and loot rolls, and each fighter's turns away from walls; so the same build
/// fights the same fight, kill for kill, on every run and every machine.
/// </para>
/// <para>
/// Level 1 characters die to a pack. A dead fighter lies dead for <see cref="LoopFighter.ReviveAfterTicks" /> ticks
/// and is then revived at the forest's entry by the instance's combat service, standing for a respawn in town and the
/// walk back, which a scenario cannot do.
/// </para>
/// </remarks>
public sealed class ForestCombatScenario : IScenario
{
    /// <summary>Ticks from the build to the first measured one: 10 s at 60 Hz.</summary>
    public const int WarmupTicks = 600;

    /// <summary>Ticks measured: one minute at 60 Hz.</summary>
    public const int MeasuredTicks = 3600;

    private const int PartySize = 3;

    /// <summary>The bag slots each fighter starts with filled (<see cref="FillBag" />), of 30.</summary>
    public const int BagFill = 28;

    // Forest armour that does not stack: one bag slot each.
    private const ulong FillerItem = 12;

    /// <summary>Seeds the world's combat and loot rolls.</summary>
    private const int WorldSeed = 1;

    private static readonly int[] s_forestSeeds = [1, 2, 3, 4];
    // Cycled over the whole roster, not per party slot: parties of three over four classes, so three parties in four
    // carry a healer (the second, third and fourth here).
    private static readonly CharacterClass[] s_classes =
        [CharacterClass.Warrior, CharacterClass.Wizard, CharacterClass.Hunter, CharacterClass.Healer];

    // Each world's fight, for Verify and Kills: the scenario is shared, and builds a world for every run.
    private readonly ConditionalWeakTable<ScenarioWorld, Fight> _fights = new();

    public string Name => "forest-combat";

    public int Players => s_forestSeeds.Length * PartySize;

    public FixedLength Length { get; } = new(WarmupTicks, MeasuredTicks);

    public ScenarioWorld Build()
    {
        var world = ScenarioWorld.CreateWithReferenceData(seed: WorldSeed);
        try
        {
            FighterHandlers handlers = LoopFighter.Handlers(world);
            var fighters = new List<LoopFighter>(Players);
            var forests = new List<FightForest>(s_forestSeeds.Length);

            foreach (int seed in s_forestSeeds)
            {
                var members = new ScenarioConnection[PartySize];
                var party = new LoopFighter[PartySize];
                var characters = new ICharacter[PartySize];
                for (int m = 0; m < PartySize; m++)
                {
                    CharacterEntity character = world.NewCharacter(s_classes[fighters.Count % s_classes.Length]);
                    FillBag(world, character);
                    characters[m] = character;
                    var fighter = new LoopFighter(handlers, character, seed: fighters.Count + 1);
                    members[m] = world.Connect(character, fighter.Step);
                    party[m] = fighter;
                    fighters.Add(fighter);
                }

                PartyId partyId = world.FormParty(members);
                MapInstance instance = world.NewForestInstance(seed, partyId);
                var forest = new FightForest(instance);
                forests.Add(forest);
                for (int m = 0; m < PartySize; m++)
                {
                    party[m].Enter(forest, characters);
                    world.Join(instance, members[m], forest.Entry);
                }
            }

            var fight = new Fight(fighters, forests);
            world.Marked += fight.Mark;
            _fights.AddOrUpdate(world, fight);
            return world;
        }
        catch
        {
            world.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Every player still present and sent to, every fighter casting and every forest losing creatures over the
    /// measured ticks, and the fight as a whole landing heals, giving and completing kill quests, picking up drops and
    /// destroying items: a fighter that stopped fighting (stuck on a wall, or its casts refused), or a fight that
    /// stopped any of the rest, would measure cheaper, and the scenario would pass the allocation gate as an improvement.
    /// </summary>
    public void Verify(ScenarioWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        world.RequireEveryPresent(Name);
        world.RequireEverySent(Name);

        Fight fight = FightOf(world);
        if (!fight.Marked)
            throw new InvalidOperationException($"{Name}: the fight's progress was not marked before its measured ticks");

        for (int i = 0; i < fight.Fighters.Count; i++)
        {
            int casts = fight.Fighters[i].Casts - fight.MarkedCasts[i];
            if (casts <= 0)
            {
                throw new InvalidOperationException(
                    $"{Name}: player {i} cast nothing during the measured ticks. A fighter that stopped fighting " +
                    "measures cheaper than the fight and would pass the allocation gate as an improvement.");
            }
        }

        for (int f = 0; f < fight.Forests.Count; f++)
        {
            int kills = fight.Forests[f].Kills - fight.MarkedKills[f];
            if (kills <= 0)
            {
                throw new InvalidOperationException(
                    $"{Name}: the forest on seed {s_forestSeeds[f]} lost no creature during the measured ticks. A party " +
                    "that kills nothing measures cheaper than the fight and would pass the allocation gate as an improvement.");
            }
        }

        // What the fight does besides fighting, over the measured ticks: each path the gate holds since #972.
        FightOutcome measured = fight.Outcome().Since(fight.MarkedOutcome!);
        RequireSome(measured.HealsLanded, "landed no heal");
        RequireSome(measured.QuestCredits, "gave the kill quest no kill credit");
        RequireSome(measured.QuestsCompleted, "completed no kill quest (each completion is abandoned and accepted again)");
        RequireSome(measured.Pickups, "picked up no drop");
        RequireSome(measured.ItemsDestroyed, "destroyed no item to keep room in the bags");
    }

    private void RequireSome(int count, string what)
    {
        if (count <= 0)
        {
            throw new InvalidOperationException(
                $"{Name}: the fight {what} during the measured ticks. A fight that stopped doing it measures cheaper " +
                "than it is and would pass the allocation gate as an improvement.");
        }
    }

    /// <summary>
    /// A character that has played a while: <see cref="BagFill" /> slots of its 30-slot bag hold a piece of forest
    /// armour each (item 12, which does not stack), put there through its inventory service before it enters the
    /// forest. A level 1 bag fills far slower than a minute's fight, so without it the fighters would never need room,
    /// and the item destroy path would not be measured; with it each fighter keeps its bag at 27 or 28 slots
    /// (<see cref="LoopFighter.BagRoomSlots" />), destroying a slot for each pickup that takes one. Setup, never ticked.
    /// </summary>
    private static void FillBag(ScenarioWorld world, CharacterEntity character)
    {
        IInventoryService inventory = world.Economy!.InventoryOf(character);
        for (int i = 0; i < BagFill; i++)
        {
            InventoryAddResult added = inventory.TryAdd(new ItemTemplateId(FillerItem), 1);
            if (added != InventoryAddResult.Ok)
                throw new InvalidOperationException($"Filling {character.Name}'s bag failed: {added}");
        }
    }

    /// <summary>What the world's fight has done since it was built, summed over its fighters and forests.</summary>
    public FightOutcome Outcome(ScenarioWorld world) => FightOf(world).Outcome();

    private Fight FightOf(ScenarioWorld world) =>
        _fights.TryGetValue(world, out Fight? fight)
            ? fight
            : throw new ArgumentException($"The world was not built by {Name}", nameof(world));

    /// <summary>One world's fighters and forests, and their casts, kills and outcome when its progress was marked.</summary>
    private sealed class Fight(IReadOnlyList<LoopFighter> fighters, IReadOnlyList<FightForest> forests)
    {
        public IReadOnlyList<LoopFighter> Fighters { get; } = fighters;

        public IReadOnlyList<FightForest> Forests { get; } = forests;

        public int[] MarkedCasts { get; } = new int[fighters.Count];

        public int[] MarkedKills { get; } = new int[forests.Count];

        public FightOutcome? MarkedOutcome { get; private set; }

        public bool Marked => MarkedOutcome is not null;

        public void Mark()
        {
            for (int i = 0; i < Fighters.Count; i++)
                MarkedCasts[i] = Fighters[i].Casts;
            for (int f = 0; f < Forests.Count; f++)
                MarkedKills[f] = Forests[f].Kills;
            MarkedOutcome = Outcome();
        }

        public FightOutcome Outcome()
        {
            int kills = 0;
            foreach (FightForest forest in Forests)
                kills += forest.Kills;

            int deaths = 0, heals = 0, landed = 0, accepted = 0, credits = 0, completed = 0, pickups = 0, destroyed = 0,
                givenUp = 0;
            foreach (LoopFighter fighter in Fighters)
            {
                deaths += fighter.Deaths;
                heals += fighter.Heals;
                landed += fighter.HealsLanded;
                accepted += fighter.QuestsAccepted;
                credits += fighter.QuestCredits;
                completed += fighter.QuestsCompleted;
                pickups += fighter.Pickups;
                destroyed += fighter.ItemsDestroyed;
                givenUp += fighter.GaveUp;
            }

            return new FightOutcome(kills, deaths, heals, landed, accepted, credits, completed, pickups, destroyed, givenUp);
        }
    }
}

/// <summary>What a <c>forest-combat</c> fight did since its world was built (<see cref="ForestCombatScenario.Outcome" />).</summary>
/// <param name="Kills">Creatures killed.</param>
/// <param name="Deaths">Fighters' deaths.</param>
/// <param name="Heals">Heals the cast handler accepted.</param>
/// <param name="HealsLanded">Of those, the heals that raised the health of the member they were aimed at.</param>
/// <param name="QuestsAccepted">Kill quests accepted, the first time and again after each completion.</param>
/// <param name="QuestCredits">Kills the fighters' kill quests counted.</param>
/// <param name="QuestsCompleted">Kill quests completed, each then abandoned and accepted again.</param>
/// <param name="Pickups">Drops picked up.</param>
/// <param name="ItemsDestroyed">Bag slots destroyed to keep room for the next pickup.</param>
/// <param name="DropsGivenUp">Drops a fighter stopped walking at (out of reach) or was refused.</param>
public sealed record FightOutcome(int Kills, int Deaths, int Heals, int HealsLanded, int QuestsAccepted,
    int QuestCredits, int QuestsCompleted, int Pickups, int ItemsDestroyed, int DropsGivenUp)
{
    /// <summary>What happened between <paramref name="earlier" /> and this outcome.</summary>
    public FightOutcome Since(FightOutcome earlier)
    {
        ArgumentNullException.ThrowIfNull(earlier);
        return new FightOutcome(Kills - earlier.Kills, Deaths - earlier.Deaths, Heals - earlier.Heals,
            HealsLanded - earlier.HealsLanded, QuestsAccepted - earlier.QuestsAccepted,
            QuestCredits - earlier.QuestCredits, QuestsCompleted - earlier.QuestsCompleted, Pickups - earlier.Pickups,
            ItemsDestroyed - earlier.ItemsDestroyed, DropsGivenUp - earlier.DropsGivenUp);
    }
}
