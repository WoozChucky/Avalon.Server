using Avalon.World.Testing.Scenarios;
using Xunit.Abstractions;

namespace Avalon.Server.World.UnitTests.Performance;

/// <summary>
/// The <c>forest-combat</c> scenario is gated by <see cref="ScenarioAllocationsShould" /> like the others, which holds only
/// if the same build fights the same fight on every run: this checks that it does, in one process, where anything
/// process-wide (the object id counter, caches warmed by the first run) would show.
/// </summary>
[Collection(nameof(ScenarioAllocations))]
public sealed class ForestCombatScenarioShould(ITestOutputHelper output)
{
    [Fact]
    public void Kill_as_many_creatures_and_allocate_as_much_on_a_second_run()
    {
        var scenario = new Counted(new ForestCombatScenario());

        ScenarioReport first = ScenarioMeasurement.Run(scenario, TimeSpan.FromSeconds(5), measureTicks: 0);
        int firstKills = scenario.Kills;
        ScenarioReport second = ScenarioMeasurement.Run(scenario, TimeSpan.FromSeconds(5), measureTicks: 0);
        int secondKills = scenario.Kills;

        output.WriteLine($"Kills {firstKills} and {secondKills}; {first.BytesPerWindow:N0} and {second.BytesPerWindow:N0} B per window.");
        Assert.True(firstKills > 0, "The fight killed nothing");
        Assert.Equal(firstKills, secondKills);
        Assert.Equal(first.BytesPerWindow, second.BytesPerWindow);
    }

    /// <summary>The scenario, keeping the kills of the world it last verified, which the measurement then disposes.</summary>
    private sealed class Counted(ForestCombatScenario scenario) : IScenario
    {
        public int Kills { get; private set; }

        public string Name => scenario.Name;

        public int Players => scenario.Players;

        public FixedLength Length => scenario.Length;

        public ScenarioWorld Build() => scenario.Build();

        public void Verify(ScenarioWorld world)
        {
            scenario.Verify(world);
            Kills = scenario.Kills(world);
        }
    }
}
