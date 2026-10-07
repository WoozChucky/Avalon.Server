using System.Globalization;
using System.Text.Json;
using Avalon.Balance.Core;
using Avalon.Balance.Data;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Xunit;

namespace Avalon.Balance.UnitTests;

public class BalanceRunnerShould
{
    private static readonly string BalanceDir = Path.Combine(RepositoryRoot.Find(), "balance");

    private static BalanceRunner Runner()
    {
        ScenarioFile scenarios = ConfigFileStore.Load(Path.Combine(BalanceDir, "scenarios.json"), ConfigFiles.ParseScenarios);
        RotationFile rotations = ConfigFileStore.Load(Path.Combine(BalanceDir, "rotations.json"), ConfigFiles.ParseRotations);
        return new BalanceRunner(TestData.Seeded, scenarios, rotations);
    }

    [Fact]
    public void Give_the_same_rows_whatever_the_scheduling()
    {
        BalanceRunner runner = Runner();
        RunPlan plan = runner.Plan(CharacterClass.Warrior, "normal-3", runs: 20, seed: 1) with { Levels = [1, 2, 3] };

        Assert.Equal(JsonSerializer.Serialize(runner.Run(plan)), JsonSerializer.Serialize(runner.Run(plan)));
    }

    [Fact]
    public void Derive_a_stable_seed_from_the_run_coordinates()
    {
        Assert.Equal(RunSeed.For(672, CharacterClass.Wizard, 3, "forest", "elite-1", 17),
            RunSeed.For(672, CharacterClass.Wizard, 3, "forest", "elite-1", 17));
        Assert.NotEqual(RunSeed.For(672, CharacterClass.Wizard, 3, "forest", "elite-1", 17),
            RunSeed.For(672, CharacterClass.Wizard, 3, "forest", "elite-1", 18));
    }

    [Fact]
    public void Derive_the_same_seed_for_a_negative_seed_whatever_the_culture()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            int invariant = RunSeed.For(-672, CharacterClass.Wizard, 3, "forest", "elite-1", 17);

            // Swedish writes a negative number with U+2212, not an ASCII hyphen.
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            int swedish = RunSeed.For(-672, CharacterClass.Wizard, 3, "forest", "elite-1", 17);

            Assert.Equal(invariant, swedish);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void Plan_only_the_filtered_class_and_scenario()
    {
        RunPlan plan = Runner().Plan(CharacterClass.Healer, "boss-1", runs: 5, seed: null);

        Assert.Equal([CharacterClass.Healer], plan.Classes);
        Assert.Equal(["boss-1"], plan.Scenarios.Select(s => s.Id));
        Assert.Equal(5, plan.Runs);
        Assert.Equal(672, plan.Seed);
    }

    [Fact]
    public void Refuse_an_unknown_scenario_filter() =>
        Assert.Contains("Unknown scenario 'normal3'",
            Assert.Throws<ArgumentException>(() => Runner().Plan(null, "normal3", null, null)).Message, StringComparison.Ordinal);

    [Fact]
    public void Clamp_a_creature_level_to_its_template_range_and_roll_it_for_template_offsets()
    {
        ScenarioFile scenarios = ConfigFileStore.Load(Path.Combine(BalanceDir, "scenarios.json"), ConfigFiles.ParseScenarios);
        CreatureTemplate wolf = TestData.Seeded.Creature(5);   // levels 2-4
        var random = new Random(1);

        Assert.Equal(2, FightFactory.CreatureLevel(wolf, 1, scenarios.Find("normal-1"), random));        // clamped up
        Assert.Equal(4, FightFactory.CreatureLevel(wolf, 10, scenarios.Find("normal-1"), random));       // clamped down
        Assert.Equal(3, FightFactory.CreatureLevel(wolf, 1, scenarios.Find("normal-3-plus2"), random));  // 1 + 2
        Assert.All(Enumerable.Range(0, 50), _ =>
            Assert.InRange(FightFactory.CreatureLevel(wolf, 1, scenarios.Find("forest-real"), random), 2, 4));
    }

    [Fact]
    public void Record_win_rate_percentiles_and_a_stat_snapshot()
    {
        BalanceRunner runner = Runner();
        RowResult row = runner.Run(runner.Plan(CharacterClass.Warrior, "normal-1", runs: 30, seed: 3) with { Levels = [3], Gear = ["forest"] }).Single();

        Assert.Equal(new RowKey(CharacterClass.Warrior, 3, "forest", "normal-1"), row.Key);
        Assert.InRange(row.WinRatePct, 0, 100);
        Assert.True(row.FightSeconds.P10 <= row.FightSeconds.Median && row.FightSeconds.Median <= row.FightSeconds.P90);
        Assert.Equal(4, row.Snapshot.Abilities.Count);
        Assert.True(row.DamageDealtPerRun.ContainsKey("Cleave"));
    }
}
