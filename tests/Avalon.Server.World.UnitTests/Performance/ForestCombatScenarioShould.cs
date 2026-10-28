using Avalon.Domain.World;
using Avalon.World.Testing.Scenarios;
using Xunit.Abstractions;

namespace Avalon.Server.World.UnitTests.Performance;

/// <summary>
/// The <c>forest-combat</c>, <c>forest-resonance</c>, <c>forest-awakening</c> and <c>forest-capstones</c> scenarios are
/// gated by <see cref="ScenarioAllocationsShould" /> like the others, which holds only if the same build fights the
/// same fight on every run: this checks that each does, in one process, where anything process-wide (the object id
/// counter, caches warmed by the first run) would show. The scenario's own check
/// (<see cref="ForestCombatScenario.Verify" />, run by each measurement) requires heals landed, kill quests credited
/// and completed, drops picked up, items destroyed and, themed, Resonance effects fired during the measured ticks and,
/// sealed, progress on every fighter's sealed affix; this adds that the whole run kills and dies, and that both runs
/// did all of it alike, the themed fight firing the same effects and the sealed one filling the same bars, and that the
/// shaped fight is not the plain one.
/// </summary>
[Collection(nameof(ScenarioAllocations))]
public sealed class ForestCombatScenarioShould(ITestOutputHelper output)
{
    [Theory]
    [InlineData("forest-combat", false, false, false)]
    [InlineData("forest-resonance", true, false, false)]
    [InlineData("forest-awakening", false, true, false)]
    [InlineData("forest-capstones", false, false, true)]
    public void Fight_the_same_fight_and_allocate_as_much_on_a_second_run(string name, bool themed, bool sealedKit,
        bool capstones)
    {
        var scenario = new Counted(new ForestCombatScenario(name, themed, sealedKit, capstones));

        ScenarioReport first = ScenarioMeasurement.Run(scenario, TimeSpan.FromSeconds(5), measureTicks: 0);
        Run firstRun = scenario.Last!;
        ScenarioReport second = ScenarioMeasurement.Run(scenario, TimeSpan.FromSeconds(5), measureTicks: 0);
        Run secondRun = scenario.Last!;

        output.WriteLine($"{firstRun.Outcome} and {secondRun.Outcome}; {first.BytesPerWindow:N0} and " +
                         $"{second.BytesPerWindow:N0} B per window. {first.Notes}");
        Assert.True(firstRun.Outcome.Kills > 0, "The fight killed nothing");
        Assert.True(firstRun.Outcome.Deaths > 0, "No fighter died: the creatures no longer fight back");
        Assert.Equal(firstRun.Outcome, secondRun.Outcome);
        Assert.Equal(firstRun.Fires, secondRun.Fires);
        Assert.Equal(themed, firstRun.Fires.Values.Sum() > 0);
        Assert.Equal(firstRun.Progress, secondRun.Progress);
        Assert.Equal(sealedKit, firstRun.Progress.Count > 0);
        Assert.Equal(first.BytesPerWindow, second.BytesPerWindow);

        // A sealed affix adds nothing to stats, so the sealed fight is forest-combat's, kill for kill: its figure over that
        // one's is the meter's cost alone.
        // A capstone fight is not: one that fights the plain fight kill for kill has lost its shapes.
        if (sealedKit || capstones)
        {
            var plain = new Counted(new ForestCombatScenario("forest-combat", themed: false));
            ScenarioMeasurement.Run(plain, TimeSpan.FromSeconds(5), measureTicks: 0);
            if (sealedKit)
                Assert.Equal(plain.Last!.Outcome, firstRun.Outcome);
            else
                Assert.NotEqual(plain.Last!.Outcome, firstRun.Outcome);
        }
    }

    /// <summary>
    /// The scenario, keeping the outcome, the Resonance firings and the sealed progress of the world it last verified,
    /// which the measurement then disposes.
    /// </summary>
    private sealed class Counted(ForestCombatScenario scenario) : IScenario
    {
        public Run? Last { get; private set; }

        public string Name => scenario.Name;

        public int Players => scenario.Players;

        public FixedLength Length => scenario.Length;

        public ScenarioWorld Build() => scenario.Build();

        public void Verify(ScenarioWorld world)
        {
            scenario.Verify(world);
            Last = new Run(scenario.Outcome(world), scenario.Fires(world), scenario.AwakeningProgress(world));
        }

        public string? Notes(ScenarioWorld world) => scenario.Notes(world);
    }

    /// <summary>What one run's fight did, as its check read it.</summary>
    private sealed record Run(FightOutcome Outcome, IReadOnlyDictionary<ResonanceTrigger, int> Fires, IReadOnlyList<int> Progress);
}
