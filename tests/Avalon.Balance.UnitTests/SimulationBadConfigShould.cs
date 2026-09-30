using System.Text.Json;
using System.Text.Json.Nodes;
using Avalon.Balance.Core;
using Avalon.World.Public.Enums;
using Xunit;

namespace Avalon.Balance.UnitTests;

/// <summary>A config that parses, or is built in code, can still be wrong: Run says so as an Issue and never throws.</summary>
public class SimulationBadConfigShould
{
    private static readonly RunFilter WarriorNormal3 =
        new(new HashSet<CharacterClass> { CharacterClass.Warrior }, null, null, new HashSet<string> { "normal-3" });

    private static RunRequest With(BalanceConfig config, int? runs = 5) => new(null, config, WarriorNormal3, runs, 1);

    private static RunResult Run(BalanceConfig config) =>
        Simulation.Run(TestData.Seed(), TestData.Config(), With(config), null, CancellationToken.None);

    private static void AssertRefused(RunResult result)
    {
        Assert.Equal(RunStatus.Invalid, result.Status);
        Assert.NotEmpty(result.Issues);
        Assert.Empty(result.Rows);
    }

    private static JsonObject Json(string text) => (JsonObject)JsonNode.Parse(text)!;

    private static JsonObject FirstScenario(JsonObject scenarios) => (JsonObject)scenarios["scenarios"]!.AsArray()[0]!;

    private static JsonObject WarriorEntry(JsonObject rotations) => (JsonObject)rotations["Warrior"]!.AsArray()[0]!;

    public static TheoryData<string> BadJson =>
    [
        "scenarios.classes-null", "scenarios.gear-null", "scenarios.scenarios-null", "scenarios.gearProfiles-null",
        "scenarios.levels-null", "scenarios.scenario-null", "scenarios.pack-null", "scenarios.pack-element-null",
        "scenarios.levelOffset-fraction", "scenarios.gearProfile-class-null",
        "targets.scenarios-null", "targets.global-null", "targets.scenario-null",
        "rotations.class-null", "rotations.entry-null", "rotations.when-null", "rotations.when-element-null",
        "rotations.when-value-null",
    ];

    [Theory]
    [MemberData(nameof(BadJson))]
    public void Refuse_a_parsed_config_with_a_hole_in_it(string name)
    {
        (string s, string t, string r) = ConfigFiles.Save(TestData.Config());
        JsonObject scenarios = Json(s), targets = Json(t), rotations = Json(r);
        switch (name)
        {
            case "scenarios.classes-null": scenarios["classes"] = null; break;
            case "scenarios.gear-null": scenarios["gear"] = null; break;
            case "scenarios.scenarios-null": scenarios["scenarios"] = null; break;
            case "scenarios.gearProfiles-null": scenarios["gearProfiles"] = null; break;
            case "scenarios.levels-null": scenarios["levels"] = null; break;
            case "scenarios.scenario-null": scenarios["scenarios"]!.AsArray()[0] = null; break;
            case "scenarios.pack-null": FirstScenario(scenarios)["pack"] = null; break;
            case "scenarios.pack-element-null": FirstScenario(scenarios)["pack"] = new JsonArray((JsonNode?)null); break;
            case "scenarios.levelOffset-fraction": FirstScenario(scenarios)["levelOffset"] = 1.5; break;
            case "scenarios.gearProfile-class-null": ((JsonObject)((JsonObject)scenarios["gearProfiles"]!).First().Value!)["Warrior"] = null; break;
            case "targets.scenarios-null": targets["scenarios"] = null; break;
            case "targets.global-null": targets["global"] = null; break;
            case "targets.scenario-null": ((JsonObject)targets["scenarios"]!)["normal-3"] = null; break;
            case "rotations.class-null": rotations["Warrior"] = null; break;
            case "rotations.entry-null": rotations["Warrior"] = new JsonArray((JsonNode?)null); break;
            case "rotations.when-null": WarriorEntry(rotations)["when"] = null; break;
            case "rotations.when-element-null": WarriorEntry(rotations)["when"] = new JsonArray((JsonNode?)null); break;
            case "rotations.when-value-null": WarriorEntry(rotations)["when"] = new JsonArray(new JsonObject { ["targetsAlive"] = null }); break;
            default: throw new ArgumentException(name);
        }

        BalanceConfig config;
        try
        {
            config = new BalanceConfig(ConfigFiles.ParseScenarios(scenarios.ToJsonString()),
                ConfigFiles.ParseTargets(targets.ToJsonString()), ConfigFiles.ParseRotations(rotations.ToJsonString()));
        }
        catch (InvalidDataException)
        {
            return; // Refused at parse time, which the files' loaders report as a bad file.
        }

        AssertRefused(Run(config));
    }

    [Theory]
    [InlineData("classes")]
    [InlineData("gear")]
    [InlineData("scenarios")]
    [InlineData("gearProfiles")]
    [InlineData("levels")]
    [InlineData("pack")]
    [InlineData("pack-element")]
    [InlineData("scenario-element")]
    [InlineData("scenario-id")]
    [InlineData("gradedGear")]
    [InlineData("targets.scenarios")]
    [InlineData("targets.global")]
    [InlineData("targets.scenario-value")]
    [InlineData("rotation-class")]
    [InlineData("rotation-entry")]
    [InlineData("when")]
    [InlineData("when-element")]
    [InlineData("when-value")]
    [InlineData("scenarios-file")]
    [InlineData("targets-file")]
    [InlineData("rotations-file")]
    public void Refuse_a_config_built_with_nulls(string name)
    {
        BalanceConfig c = TestData.Config();
        BalanceConfig config = c;
        switch (name)
        {
            case "classes": c.Scenarios.Classes = null!; break;
            case "gear": c.Scenarios.Gear = null!; break;
            case "scenarios": c.Scenarios.Scenarios = null!; break;
            case "gearProfiles": c.Scenarios.GearProfiles = null!; break;
            case "levels": c.Scenarios.Levels = null!; break;
            case "pack": c.Scenarios.Scenarios[0].Pack = null!; break;
            case "pack-element": c.Scenarios.Scenarios[0].Pack = [null!]; break;
            case "scenario-element": c.Scenarios.Scenarios = [null!]; break;
            case "scenario-id": c.Scenarios.Scenarios[0].Id = null!; break;
            case "gradedGear": c.Targets.GradedGear = null!; break;
            case "targets.scenarios": c.Targets.Scenarios = null!; break;
            case "targets.global": c.Targets.Global = null!; break;
            case "targets.scenario-value": c.Targets.Scenarios["normal-3"] = null!; break;
            case "rotation-class": c.Rotations[CharacterClass.Warrior] = null!; break;
            case "rotation-entry": c.Rotations[CharacterClass.Warrior] = [null!]; break;
            case "when": c.Rotations[CharacterClass.Warrior][0].When = null!; break;
            case "when-element": c.Rotations[CharacterClass.Warrior][0].When = [null!]; break;
            case "when-value": c.Rotations[CharacterClass.Warrior][0].When = [new Dictionary<string, string> { ["targetsAlive"] = null! }]; break;
            case "scenarios-file": config = c with { Scenarios = null! }; break;
            case "targets-file": config = c with { Targets = null! }; break;
            case "rotations-file": config = c with { Rotations = null! }; break;
            default: throw new ArgumentException(name);
        }

        AssertRefused(Run(config));
    }

    [Fact]
    public void Keep_the_message_for_a_fractional_level_offset()
    {
        BalanceConfig c = TestData.Config();
        c.Scenarios.Scenarios[0].LevelOffset = JsonDocument.Parse("1.5").RootElement;

        RunResult result = Run(c);

        Assert.Equal(RunStatus.Invalid, result.Status);
        Assert.Contains("levelOffset must be a number or \"template\"", result.Issues[0].Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(100_001)]
    [InlineData(int.MaxValue)]
    public void Refuse_a_run_count_past_the_library_ceiling(int runs)
    {
        RunResult result = Simulation.Run(TestData.Seed(), TestData.Config(), With(TestData.Config(), runs), null, CancellationToken.None);

        Assert.Equal(RunStatus.Invalid, result.Status);
        Assert.Equal("runsPerRow", Assert.Single(result.Issues).Path);
        Assert.Empty(result.Rows);
    }
}
