using System.Runtime.CompilerServices;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Parties;
using Avalon.World.Public.Enums;

namespace Avalon.World.Testing.Scenarios;

/// <summary>
/// Parties fighting through forests: four forest instances on the fixed seeds 1 to 4, each built as its party's forest
/// is in production (<see cref="ScenarioWorld.NewForestInstance" />: the World server's generator, placement, spawn
/// table and real AI over the World server's reference data), with a party of three level 1 characters in each, a
/// warrior, a wizard and a hunter, joined at the entry. Every player is a <see cref="LoopFighter" />: it walks to the
/// nearest live creature through the real input handler and casts its class's basic ability at it through the real
/// cast handler. What is measured is a fight: the forests' creature AI (about 200 creatures each, all ticking), casts,
/// projectiles, hits, threat and encounters, deaths and revives, kills with their real loot and experience shared by
/// the party, and the state broadcast of all of it.
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

    /// <summary>Seeds the world's combat and loot rolls.</summary>
    private const int WorldSeed = 1;

    private static readonly int[] s_forestSeeds = [1, 2, 3, 4];
    private static readonly CharacterClass[] s_classes = [CharacterClass.Warrior, CharacterClass.Wizard, CharacterClass.Hunter];

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
                for (int m = 0; m < PartySize; m++)
                {
                    CharacterEntity character = world.NewCharacter(s_classes[m % s_classes.Length]);
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
                    party[m].Enter(forest);
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
    /// measured ticks: a fighter that stopped fighting (stuck on a wall, or its casts refused) would leave the forest
    /// cheaper, and the scenario would pass the allocation gate as an improvement.
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
    }

    /// <summary>The creatures killed in the world's forests since they were built.</summary>
    public int Kills(ScenarioWorld world)
    {
        int kills = 0;
        foreach (FightForest forest in FightOf(world).Forests)
            kills += forest.Kills;
        return kills;
    }

    private Fight FightOf(ScenarioWorld world) =>
        _fights.TryGetValue(world, out Fight? fight)
            ? fight
            : throw new ArgumentException($"The world was not built by {Name}", nameof(world));

    /// <summary>One world's fighters and forests, and their casts and kills when its progress was marked.</summary>
    private sealed class Fight(IReadOnlyList<LoopFighter> fighters, IReadOnlyList<FightForest> forests)
    {
        public IReadOnlyList<LoopFighter> Fighters { get; } = fighters;

        public IReadOnlyList<FightForest> Forests { get; } = forests;

        public int[] MarkedCasts { get; } = new int[fighters.Count];

        public int[] MarkedKills { get; } = new int[forests.Count];

        public bool Marked { get; private set; }

        public void Mark()
        {
            for (int i = 0; i < Fighters.Count; i++)
                MarkedCasts[i] = Fighters[i].Casts;
            for (int f = 0; f < Forests.Count; f++)
                MarkedKills[f] = Forests[f].Kills;
            Marked = true;
        }
    }
}
