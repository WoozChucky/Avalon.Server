using Avalon.World.Testing.Scenarios;
using Xunit.Abstractions;

namespace Avalon.Server.World.UnitTests.Performance;

/// <summary>
/// The <c>forest-combat</c> scenario is gated by <see cref="ScenarioAllocationsShould" /> like the others, which holds only
/// if the same build fights the same fight on every run: this checks that it does, in one process, where anything
/// process-wide (the object id counter, caches warmed by the first run) would show. The scenario's own check
/// (<see cref="ForestCombatScenario.Verify" />, run by each measurement) requires heals landed, kill quests credited and
/// completed, drops picked up and items destroyed during the measured ticks; this adds that the whole run kills and
/// dies, and that both runs did all of it alike.
/// </summary>
[Collection(nameof(ScenarioAllocations))]
public sealed class ForestCombatScenarioShould(ITestOutputHelper output)
{
    [Fact]
    public void Fight_the_same_fight_and_allocate_as_much_on_a_second_run()
    {
        var scenario = new Counted(new ForestCombatScenario());

        ScenarioReport first = ScenarioMeasurement.Run(scenario, TimeSpan.FromSeconds(5), measureTicks: 0);
        FightOutcome firstOutcome = scenario.Outcome!;
        ScenarioReport second = ScenarioMeasurement.Run(scenario, TimeSpan.FromSeconds(5), measureTicks: 0);
        FightOutcome secondOutcome = scenario.Outcome!;

        output.WriteLine($"{firstOutcome} and {secondOutcome}; {first.BytesPerWindow:N0} and {second.BytesPerWindow:N0} B per window.");
        Assert.True(firstOutcome.Kills > 0, "The fight killed nothing");
        Assert.True(firstOutcome.Deaths > 0, "No fighter died: the creatures no longer fight back");
        Assert.Equal(firstOutcome, secondOutcome);
        Assert.Equal(first.BytesPerWindow, second.BytesPerWindow);
    }

    /// <summary>The scenario, keeping the outcome of the world it last verified, which the measurement then disposes.</summary>
    private sealed class Counted(ForestCombatScenario scenario) : IScenario
    {
        public FightOutcome? Outcome { get; private set; }

        public string Name => scenario.Name;

        public int Players => scenario.Players;

        public FixedLength Length => scenario.Length;

        public ScenarioWorld Build() => scenario.Build();

        public void Verify(ScenarioWorld world)
        {
            scenario.Verify(world);
            Outcome = scenario.Outcome(world);
        }
    }
}
