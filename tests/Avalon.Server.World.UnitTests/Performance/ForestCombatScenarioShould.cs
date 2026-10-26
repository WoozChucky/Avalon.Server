using Avalon.Domain.World;
using Avalon.World.Testing.Scenarios;
using Xunit.Abstractions;

namespace Avalon.Server.World.UnitTests.Performance;

/// <summary>
/// The <c>forest-combat</c> and <c>forest-resonance</c> scenarios are gated by <see cref="ScenarioAllocationsShould" />
/// like the others, which holds only if the same build fights the same fight on every run: this checks that each does,
/// in one process, where anything process-wide (the object id counter, caches warmed by the first run) would show. The
/// scenario's own check (<see cref="ForestCombatScenario.Verify" />, run by each measurement) requires heals landed, kill
/// quests credited and completed, drops picked up, items destroyed and, themed, Resonance effects fired during the
/// measured ticks; this adds that the whole run kills and dies, and that both runs did all of it alike, the themed fight
/// firing the same effects.
/// </summary>
[Collection(nameof(ScenarioAllocations))]
public sealed class ForestCombatScenarioShould(ITestOutputHelper output)
{
    [Theory]
    [InlineData("forest-combat", false)]
    [InlineData("forest-resonance", true)]
    public void Fight_the_same_fight_and_allocate_as_much_on_a_second_run(string name, bool themed)
    {
        var scenario = new Counted(new ForestCombatScenario(name, themed));

        ScenarioReport first = ScenarioMeasurement.Run(scenario, TimeSpan.FromSeconds(5), measureTicks: 0);
        (FightOutcome Outcome, IReadOnlyDictionary<ResonanceTrigger, int> Fires) firstRun = scenario.Last!.Value;
        ScenarioReport second = ScenarioMeasurement.Run(scenario, TimeSpan.FromSeconds(5), measureTicks: 0);
        (FightOutcome Outcome, IReadOnlyDictionary<ResonanceTrigger, int> Fires) secondRun = scenario.Last!.Value;

        output.WriteLine($"{firstRun.Outcome} and {secondRun.Outcome}; {first.BytesPerWindow:N0} and " +
                         $"{second.BytesPerWindow:N0} B per window. {first.Notes}");
        Assert.True(firstRun.Outcome.Kills > 0, "The fight killed nothing");
        Assert.True(firstRun.Outcome.Deaths > 0, "No fighter died: the creatures no longer fight back");
        Assert.Equal(firstRun.Outcome, secondRun.Outcome);
        Assert.Equal(firstRun.Fires, secondRun.Fires);
        Assert.Equal(themed, firstRun.Fires.Values.Sum() > 0);
        Assert.Equal(first.BytesPerWindow, second.BytesPerWindow);
    }

    /// <summary>
    /// The scenario, keeping the outcome and the Resonance firings of the world it last verified, which the measurement
    /// then disposes.
    /// </summary>
    private sealed class Counted(ForestCombatScenario scenario) : IScenario
    {
        public (FightOutcome Outcome, IReadOnlyDictionary<ResonanceTrigger, int> Fires)? Last { get; private set; }

        public string Name => scenario.Name;

        public int Players => scenario.Players;

        public FixedLength Length => scenario.Length;

        public ScenarioWorld Build() => scenario.Build();

        public void Verify(ScenarioWorld world)
        {
            scenario.Verify(world);
            Last = (scenario.Outcome(world), scenario.Fires(world));
        }

        public string? Notes(ScenarioWorld world) => scenario.Notes(world);
    }
}
