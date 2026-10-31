using System.Text.Json;
using System.Text.Json.Nodes;
using Avalon.Balance.Core;
using Avalon.Common;
using Xunit;

namespace Avalon.Balance.UnitTests;

/// <summary>A config that parses, or is built in code, can still be wrong: Run says so as an Issue and never throws.</summary>
public class SimulationBadConfigShould
{
    private static readonly RunFilter s_warriorNormal3 =
        new(new HashSet<CharacterClass> { CharacterClass.Warrior }, null, null, null, new HashSet<string> { "normal-3" });

    private static RunRequest With(BalanceConfig config, int? runs = 5) => new(null, config, s_warriorNormal3, runs, 1);

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

    private const string Axes = "warrior-axes";

    private static JsonObject AxesBuild(JsonObject builds) => (JsonObject)builds[Axes]!;

    private static JsonObject AxesEntry(JsonObject builds) => (JsonObject)AxesBuild(builds)["rotation"]!.AsArray()[0]!;

    public static TheoryData<string> BadJson =>
    [
        "scenarios.builds-null", "scenarios.gear-null", "scenarios.scenarios-null", "scenarios.gearProfiles-null",
        "scenarios.levels-null", "scenarios.scenario-null", "scenarios.pack-null", "scenarios.pack-element-null",
        "scenarios.levelOffset-fraction", "scenarios.gearProfile-class-null",
        "targets.scenarios-null", "targets.global-null", "targets.scenario-null",
        "builds.build-null", "builds.nodes-null", "builds.rotation-null", "builds.entry-null", "builds.when-null",
        "builds.when-element-null", "builds.when-value-null",
    ];

    [Theory]
    [MemberData(nameof(BadJson))]
    public void Refuse_a_parsed_config_with_a_hole_in_it(string name)
    {
        (string s, string t, string b) = ConfigFiles.Save(TestData.Config());
        JsonObject scenarios = Json(s), targets = Json(t), builds = Json(b);
        switch (name)
        {
            case "scenarios.builds-null": scenarios["builds"] = null; break;
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
            case "builds.build-null": builds[Axes] = null; break;
            case "builds.nodes-null": AxesBuild(builds)["nodes"] = null; break;
            case "builds.rotation-null": AxesBuild(builds)["rotation"] = null; break;
            case "builds.entry-null": AxesBuild(builds)["rotation"] = new JsonArray((JsonNode?)null); break;
            case "builds.when-null": AxesEntry(builds)["when"] = null; break;
            case "builds.when-element-null": AxesEntry(builds)["when"] = new JsonArray((JsonNode?)null); break;
            case "builds.when-value-null": AxesEntry(builds)["when"] = new JsonArray(new JsonObject { ["targetsAlive"] = null }); break;
            default: throw new ArgumentException(name);
        }

        BalanceConfig config;
        try
        {
            config = new BalanceConfig(ConfigFiles.ParseScenarios(scenarios.ToJsonString()),
                ConfigFiles.ParseTargets(targets.ToJsonString()), ConfigFiles.ParseBuilds(builds.ToJsonString()));
        }
        catch (InvalidDataException)
        {
            return; // Refused at parse time, which the files' loaders report as a bad file.
        }

        AssertRefused(Run(config));
    }

    [Theory]
    [InlineData("builds")]
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
    [InlineData("build")]
    [InlineData("nodes")]
    [InlineData("rotation")]
    [InlineData("rotation-entry")]
    [InlineData("when")]
    [InlineData("when-element")]
    [InlineData("when-value")]
    [InlineData("scenarios-file")]
    [InlineData("targets-file")]
    [InlineData("builds-file")]
    public void Refuse_a_config_built_with_nulls(string name)
    {
        BalanceConfig c = TestData.Config();
        BalanceConfig config = c;
        switch (name)
        {
            case "builds": c.Scenarios.Builds = null!; break;
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
            case "build": c.Builds[Axes] = null!; break;
            case "nodes": c.Builds[Axes].Nodes = null!; break;
            case "rotation": c.Builds[Axes].Rotation = null!; break;
            case "rotation-entry": c.Builds[Axes].Rotation = [null!]; break;
            case "when": c.Builds[Axes].Rotation[0].When = null!; break;
            case "when-element": c.Builds[Axes].Rotation[0].When = [null!]; break;
            case "when-value": c.Builds[Axes].Rotation[0].When = [new Dictionary<string, string> { ["targetsAlive"] = null! }]; break;
            case "scenarios-file": config = c with { Scenarios = null! }; break;
            case "targets-file": config = c with { Targets = null! }; break;
            case "builds-file": config = c with { Builds = null! }; break;
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
