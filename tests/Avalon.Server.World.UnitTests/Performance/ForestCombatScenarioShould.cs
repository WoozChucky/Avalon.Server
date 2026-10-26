using Avalon.Domain.World;
using Avalon.World.Testing.Scenarios;
using Xunit.Abstractions;

namespace Avalon.Server.World.UnitTests.Performance;

/// <summary>
/// The <c>forest-combat</c>, <c>forest-resonance</c> and <c>forest-awakening</c> scenarios are gated by
/// <see cref="ScenarioAllocationsShould" />
/// like the others, which holds only if the same build fights the same fight on every run: this checks that each does,
/// in one process, where anything process-wide (the object id counter, caches warmed by the first run) would show. The
/// scenario's own check (<see cref="ForestCombatScenario.Verify" />, run by each measurement) requires heals landed, kill
/// quests credited and completed, drops picked up, items destroyed and, themed, Resonance effects fired during the
/// measured ticks and, sealed, progress on every fighter's sealed affix; this adds that the whole run kills and dies, and
/// that both runs did all of it alike, the themed fight firing the same effects and the sealed one filling the same bars.
/// </summary>
[Collection(nameof(ScenarioAllocations))]
public sealed class ForestCombatScenarioShould(ITestOutputHelper output)
{
    [Theory]
    [InlineData("forest-combat", false, false)]
    [InlineData("forest-resonance", true, false)]
    [InlineData("forest-awakening", false, true)]
    public void Fight_the_same_fight_and_allocate_as_much_on_a_second_run(string name, bool themed, bool sealedKit)
    {
        var scenario = new Counted(new ForestCombatScenario(name, themed, sealedKit));

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
