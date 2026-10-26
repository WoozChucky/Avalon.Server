using System.Reflection;
using System.Runtime.CompilerServices;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Combat;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Inventory;
using Avalon.World.Parties;
using Avalon.World.Public.Characters;

namespace Avalon.World.Testing.Scenarios;

/// <summary>
/// Parties fighting through forests: four forest instances on four fixed seeds, each built as its party's forest
/// is in production (<see cref="ScenarioWorld.NewForestInstance" />: the World server's generator, placement, spawn
/// table and real AI over the World server's reference data), with a party of three level 1 characters in each, joined
/// at the entry; their classes cycle over the four (warrior, wizard, hunter, cleric) across the whole roster, so three
/// parties in four carry a cleric. Every player is a <see cref="LoopFighter" />: it walks to the nearest live creature
/// through the real input handler and casts its class's basic ability at it through the real cast handler, and a cleric
/// heals its party first when one of them is hurt. What is measured is a fight: the forests' creature AI (231 to 325
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
/// <para>
/// Built themed (<c>forest-resonance</c>, #1180 part A2), the same fight is fought in the Resonance kit
/// (<see cref="ScenarioWorld.WearResonanceKit" />): every fighter wears four pieces, each with one T9 affix of each of its
/// class's two themes (<see cref="s_kitAffixes" />), so each of the eight themes is at 4 on some fighter and its effects
/// fire on the fight's hits, crits, kills and strikes. Its check adds floors of its own, over the measured ticks: across
/// the roster, the effects fired at least about half as often as they do on these seeds, and each of those four triggers
/// fired at least once (<see cref="Fires" />); the report lists every trigger's firings (<see cref="Notes" />).
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

    // Chosen (#1109) from seeds 1 to 64 for a fight of the size the gate has always held, after the forest pool's fifteen
    // new pieces gave seeds 1 to 4 a fight half as large: each seed decides its forest's layout, and its place in the list
    // its party's classes. The first party, the one with no cleric, fights on seed 33 since the tree walls (45 before),
    // whose forest kills some of its fighters, so the revive path runs in the fight (4 deaths over the run). The third
    // party fights on seed 56 since #1113 (15 before), which gave the seeds that retry their layout, 15 and 46 among them,
    // forests of their own: 15's new one halved the fight's quest completions, to the floor below.
    private static readonly int[] s_forestSeeds = [33, 46, 56, 36];

    // The least the measured ticks must do, about half of what the fight on these seeds does in them (112 kills, 24
    // completions, 202 pickups, 18 heals landed): a pool or harness change that shrinks the fight by half or more fails
    // here, rather than leaving the allocation gate holding fewer of each event.
    private const int MinKills = 60, MinQuestsCompleted = 15, MinPickups = 110, MinHealsLanded = 8;
    // Cycled over the whole roster, not per party slot: parties of three over four classes, so three parties in four
    // carry a cleric (the second, third and fourth here).
    private static readonly CharacterClass[] s_classes =
        [CharacterClass.Warrior, CharacterClass.Wizard, CharacterClass.Hunter, CharacterClass.Cleric];

    // A themed fighter's affix families, by class: one of each of its class's two themes, on each piece of the kit.
    private static readonly Dictionary<CharacterClass, (string Family, DamageType? Element)[]> s_kitAffixes = new()
    {
        [CharacterClass.Warrior] = [("P12", null), ("S9", null)],                  // Iron, Thorn
        [CharacterClass.Wizard] = [("P8", DamageType.Fire), ("P16", null)],        // Ember, Veil
        [CharacterClass.Hunter] = [("P8", DamageType.Lightning), ("S7", null)],    // Storm, Gale
        [CharacterClass.Cleric] = [("P11", null), ("P8", DamageType.Cold)],        // Wild, Rime
    };

    // The triggers a themed fight must fire during the measured ticks, across the roster; Evaded and WardBroken are
    // reported (Notes), not floored.
    private static readonly ResonanceTrigger[] s_flooredTriggers =
        [ResonanceTrigger.Hit, ResonanceTrigger.Crit, ResonanceTrigger.Kill, ResonanceTrigger.Struck];

    // The least Resonance effects a themed fight must fire during the measured ticks, about half of what it fires on these
    // seeds (152: 44 Hit, 21 Crit, 27 Kill, 51 Struck, 3 Evaded, 6 WardBroken): a change that cut the firings by half or
    // more fails here, rather than passing the allocation gate as a saving.
    private const int MinResonanceFires = 75;

    private static readonly PropertyInfo s_resonanceProcs =
        typeof(CharacterEntity).GetProperty("ResonanceProcs", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"{nameof(CharacterEntity)}.ResonanceProcs is not there");

    private readonly bool _themed;

    // Each world's fight, for Verify and Kills: the scenario is shared, and builds a world for every run.
    private readonly ConditionalWeakTable<ScenarioWorld, Fight> _fights = new();

    /// <summary>The fight as <c>forest-combat</c> measures it: unthemed.</summary>
    public ForestCombatScenario()
        : this("forest-combat", themed: false)
    {
    }

    /// <param name="name">The name the baseline reports it under.</param>
    /// <param name="themed">Whether every fighter wears the Resonance kit (see the remarks).</param>
    public ForestCombatScenario(string name, bool themed)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
        _themed = themed;
    }

    public string Name { get; }

    public int Players => s_forestSeeds.Length * PartySize;

    public FixedLength Length { get; } = new(WarmupTicks, MeasuredTicks);

    public ScenarioWorld Build()
    {
        var world = ScenarioWorld.CreateWithReferenceData(seed: WorldSeed);
        try
        {
            FighterHandlers handlers = LoopFighter.Handlers(world);
            var fighters = new List<LoopFighter>(Players);
            var roster = new List<CharacterEntity>(Players);
            var forests = new List<FightForest>(s_forestSeeds.Length);
            ResonanceWatch? watch = _themed ? new ResonanceWatch() : null;

            foreach (int seed in s_forestSeeds)
            {
                var members = new ScenarioConnection[PartySize];
                var party = new LoopFighter[PartySize];
                var characters = new ICharacter[PartySize];
                for (int m = 0; m < PartySize; m++)
                {
                    CharacterEntity character = world.NewCharacter(s_classes[fighters.Count % s_classes.Length]);
                    if (_themed)
                    {
                        ScenarioWorld.WearResonanceKit(character, ScenarioReferenceData.Shared.Data,
                            s_kitAffixes[character.Class]);
                    }

                    FillBag(world, character);
                    characters[m] = character;
                    roster.Add(character);
                    var fighter = new LoopFighter(handlers, character, seed: fighters.Count + 1);
                    members[m] = world.Connect(character,
                        watch is null ? fighter.Step : watch.Watching(character, fighter.Step));
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

            var fight = new Fight(fighters, forests, roster, watch);
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
    /// measured ticks, and the fight as a whole killing, landing heals, completing kill quests and picking up drops at
    /// least about half as often as it does on its seeds (<see cref="MinKills" /> and the others), giving kill credit
    /// and destroying items: a fighter that stopped fighting (stuck on a wall, or its casts refused), or a fight that
    /// stopped or shrank, would measure cheaper, and the scenario would pass the allocation gate as an improvement.
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
        RequireAtLeast(measured.Kills, MinKills, "killed");
        RequireAtLeast(measured.HealsLanded, MinHealsLanded, "landed heals");
        RequireAtLeast(measured.QuestCredits, 1, "gave kill credit");
        RequireAtLeast(measured.QuestsCompleted, MinQuestsCompleted, "completed kill quests (each completion is abandoned and accepted again)");
        RequireAtLeast(measured.Pickups, MinPickups, "picked up drops");
        RequireAtLeast(measured.ItemsDestroyed, 1, "destroyed items to keep room in the bags");

        if (_themed)
        {
            IReadOnlyDictionary<ResonanceTrigger, int> fired = Fires(world);
            RequireAtLeast(fired.Values.Sum(), MinResonanceFires, "fired Resonance effects");
            foreach (ResonanceTrigger trigger in s_flooredTriggers)
                RequireAtLeast(fired[trigger], 1, $"fired {trigger} Resonance effects");
        }
    }

    /// <summary>
    /// The Resonance effects the roster fired during the measured ticks, by trigger, every trigger listed; all 0 for an
    /// unthemed fight. A firing spends its effect's cooldown, which is how it is counted (<see cref="ResonanceWatch" />).
    /// </summary>
    public IReadOnlyDictionary<ResonanceTrigger, int> Fires(ScenarioWorld world)
    {
        Fight fight = FightOf(world);
        Dictionary<ResonanceTrigger, int> fired = Enum.GetValues<ResonanceTrigger>().ToDictionary(t => t, _ => 0);
        if (fight.Watch is not { } watch)
            return fired;

        if (fight.MarkedFires is not { } marked)
            throw new InvalidOperationException($"{Name}: the fight's progress was not marked before its measured ticks");

        watch.LookAtEveryone();
        foreach (ResonanceTrigger trigger in fired.Keys)
            fired[trigger] = watch.Fired[trigger] - marked[trigger];
        return fired;
    }

    /// <summary>
    /// For the report: what the themed fight fired during the measured ticks, by trigger, Evaded and WardBroken included,
    /// which the check does not floor; null for an unthemed fight.
    /// </summary>
    public string? Notes(ScenarioWorld world)
    {
        if (!_themed)
            return null;

        IReadOnlyDictionary<ResonanceTrigger, int> fired = Fires(world);
        return $"Resonance effects fired in the measured ticks: {fired.Values.Sum()} " +
               $"({string.Join(", ", fired.Select(pair => $"{pair.Key} {pair.Value}"))})";
    }

    private void RequireAtLeast(int count, int least, string what)
    {
        if (count < least)
        {
            throw new InvalidOperationException(
                $"{Name}: the fight {what} {count} times during the measured ticks, fewer than {least}. A fight that " +
                "stopped doing it, or does it far less, measures cheaper than it is and would pass the allocation gate " +
                "as an improvement.");
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

    /// <summary>
    /// One world's fighters, their characters and forests, the themed fight's Resonance watch, and their casts, kills,
    /// outcome and Resonance firings when its progress was marked.
    /// </summary>
    private sealed class Fight(IReadOnlyList<LoopFighter> fighters, IReadOnlyList<FightForest> forests,
        IReadOnlyList<CharacterEntity> characters, ResonanceWatch? watch)
    {
        public IReadOnlyList<LoopFighter> Fighters { get; } = fighters;

        public IReadOnlyList<CharacterEntity> Characters { get; } = characters;

        public IReadOnlyList<FightForest> Forests { get; } = forests;

        public ResonanceWatch? Watch { get; } = watch;

        public int[] MarkedCasts { get; } = new int[fighters.Count];

        public int[] MarkedKills { get; } = new int[forests.Count];

        public FightOutcome? MarkedOutcome { get; private set; }

        public IReadOnlyDictionary<ResonanceTrigger, int>? MarkedFires { get; private set; }

        public bool Marked => MarkedOutcome is not null;

        public void Mark()
        {
            for (int i = 0; i < Fighters.Count; i++)
                MarkedCasts[i] = Fighters[i].Casts;
            for (int f = 0; f < Forests.Count; f++)
                MarkedKills[f] = Forests[f].Kills;
            MarkedOutcome = Outcome();
            if (Watch is { } resonance)
            {
                resonance.LookAtEveryone();
                MarkedFires = new Dictionary<ResonanceTrigger, int>(resonance.Fired);
            }
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

    /// <summary>
    /// Counts the themed roster's Resonance firings, by trigger. A firing spends its effect's cooldown, so a slot whose
    /// ready time moved since the last look fired once: each fighter's procs are looked at in its own step, every tick,
    /// and every cooldown is at least 2 s, so no firing is missed or counted twice (at count 4 no two active slots share a
    /// cooldown). Looking reads the procs' public members into arrays sized at the build, so it allocates nothing on a
    /// measured tick; the procs are World-side and internal, so they are found by reflection, once, at the build.
    /// </summary>
    private sealed class ResonanceWatch
    {
        private readonly List<(ResonanceProcs Procs, DateTime[] Seen)> _watched = [];

        /// <summary>Every firing since the build, by trigger, every trigger listed.</summary>
        public Dictionary<ResonanceTrigger, int> Fired { get; } =
            Enum.GetValues<ResonanceTrigger>().ToDictionary(trigger => trigger, _ => 0);

        /// <summary>
        /// <paramref name="step" />, which also looks at <paramref name="character" />'s procs once it has stepped. The
        /// character wears its kit already: its procs keep these slots for the fight (no refresh recounts it).
        /// </summary>
        public Action<ScenarioConnection> Watching(CharacterEntity character, Action<ScenarioConnection> step)
        {
            var procs = (ResonanceProcs)s_resonanceProcs.GetValue(character)!;
            int index = _watched.Count;
            _watched.Add((procs, new DateTime[procs.Count]));
            return connection =>
            {
                step(connection);
                Look(index);
            };
        }

        /// <summary>Looks at every fighter: at the mark and at the check, so a firing after a fighter's last step counts.</summary>
        public void LookAtEveryone()
        {
            for (int i = 0; i < _watched.Count; i++)
                Look(i);
        }

        private void Look(int index)
        {
            (ResonanceProcs procs, DateTime[] seen) = _watched[index];
            if (procs.Count != seen.Length)
                throw new InvalidOperationException("A fighter's Resonance effects were rebuilt during the fight");

            for (int slot = 0; slot < seen.Length; slot++)
            {
                DateTime readyAt = procs.ReadyAt(slot);
                if (readyAt == seen[slot])
                    continue;

                seen[slot] = readyAt;
                Fired[procs.EffectAt(slot).Trigger]++;
            }
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
