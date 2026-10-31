using Avalon.Balance.Core;
using Avalon.Balance.Data;
using Avalon.Common;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Xunit;

namespace Avalon.Balance.UnitTests;

public class ConfigFilesShould
{
    private static readonly string s_balanceDir = Path.Combine(RepositoryRoot.Find(), "balance");

    private static ScenarioFile CheckedInScenarios() =>
        ConfigFileStore.Load(Path.Combine(s_balanceDir, "scenarios.json"), ConfigFiles.ParseScenarios);

    private static BuildFile CheckedInBuilds() =>
        ConfigFileStore.Load(Path.Combine(s_balanceDir, "builds.json"), ConfigFiles.ParseBuilds);

    [Fact]
    public void Load_the_checked_in_files_against_the_seed()
    {
        BalanceData data = TestData.Seeded;
        ScenarioFile scenarios = CheckedInScenarios();
        BuildFile builds = CheckedInBuilds();
        scenarios.Validate(data, builds);
        TargetFile targets = ConfigFileStore.Load(Path.Combine(s_balanceDir, "targets.json"), ConfigFiles.ParseTargets);

        Assert.Equal(Enumerable.Range(1, 10).Select(l => (ushort)l), scenarios.LevelRange());
        Assert.Equal("forest", targets.GradedGear);
        foreach (string id in scenarios.Builds)
            Assert.NotEmpty(builds.Compile(id, data).Rotation);
    }

    [Fact]
    public void Read_a_template_level_offset_and_a_numeric_one()
    {
        ScenarioFile scenarios = CheckedInScenarios();

        Assert.True(scenarios.Find("forest-real").OffsetFromTemplate);
        Assert.Equal(2, scenarios.Find("normal-3-plus2").Offset);
        Assert.Equal(0, scenarios.Find("elite-1").Offset);          // absent means 0
        Assert.True(scenarios.Find("normal-3").SameLevel);
        Assert.False(scenarios.Find("normal-3-plus2").SameLevel);
    }

    [Theory]
    [InlineData("starter")]
    [InlineData("forest")]
    public void Name_one_wearable_piece_per_slot_for_each_class(string profile)
    {
        BalanceData data = TestData.Seeded;
        ScenarioFile scenarios = CheckedInScenarios();

        foreach (CharacterClass c in CharacterClasses.Playable)
        {
            ItemTemplate[] items = scenarios.GearFor(profile, c).Select(data.Item).ToArray();
            Assert.All(items, i => Assert.True(i.AllowsClass(c)));
            Assert.All(items, i => Assert.NotNull(i.Slot));
            Assert.Equal(items.Length, items.Select(i => i.Slot).Distinct().Count());
            Assert.Contains(items, i => i.Slot == ItemSlotType.MainHand);
        }
    }

    [Fact]
    public void Take_the_starter_profile_from_the_weapon_and_armour_vendors()
    {
        BalanceData data = TestData.Seeded;
        var sold = data.Tables.VendorStocks
            .Where(v => v.CreatureTemplateId.Value is 12 or 13)
            .Select(v => v.ItemTemplateId.Value)
            .ToHashSet();

        foreach (CharacterClass c in CharacterClasses.Playable)
            Assert.All(CheckedInScenarios().GearFor("starter", c), id => Assert.Contains(id, sold));
    }

    /// <summary>
    /// #1179 retired the Uncommon rarity: the forest profile is the tier-2 bases, each a Common base the simulator wears as
    /// its fixed copy (#1183), and none of them a starter piece.
    /// </summary>
    [Fact]
    public void Take_the_forest_profile_from_common_bases()
    {
        BalanceData data = TestData.Seeded;
        ScenarioFile scenarios = CheckedInScenarios();
        foreach (CharacterClass c in CharacterClasses.Playable)
        {
            ulong[] starter = scenarios.GearFor("starter", c);
            Assert.All(scenarios.GearFor("forest", c), id =>
            {
                ItemTemplate item = data.Item(id);
                Assert.Equal(ItemRarity.Common, item.Rarity);
                Assert.NotNull(item.BaseKind);
                Assert.DoesNotContain(id, starter);
            });
        }
    }

    [Fact]
    public void Refuse_a_pack_rarity_with_no_hostile_template()
    {
        ScenarioFile scenarios = ConfigFiles.ParseScenarios("""
            { "runs": 1, "seed": 1, "levels": [1, 1], "builds": ["warrior-axes"], "gear": ["none"],
              "scenarios": [ { "id": "town", "pack": [ { "template": 1 } ] } ], "gearProfiles": {} }
            """);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => scenarios.Validate(TestData.Seeded, CheckedInBuilds()));
        Assert.Contains("scenario 'town'", error.Message, StringComparison.Ordinal);

        SeedTables tables = SeedSource.Load();
        tables.CreatureTemplates.Single(t => t.Id.Value == 10).Rarity = CreatureRarity.Rare;   // no Boss left
        ScenarioFile boss = ConfigFiles.ParseScenarios("""
            { "runs": 1, "seed": 1, "levels": [1, 1], "builds": ["warrior-axes"], "gear": ["none"],
              "scenarios": [ { "id": "boss-1", "pack": [ { "rarity": "Boss" } ] } ], "gearProfiles": {} }
            """);
        Assert.Contains("no hostile Boss template",
            Assert.Throws<InvalidDataException>(() => boss.Validate(BalanceData.From(tables), CheckedInBuilds())).Message,
            StringComparison.Ordinal);
    }

    /// <summary>B-R16: a build is refused when compiled for an ability its whole path never holds, a node the tree refuses, or a bad condition; an unknown class when parsed.</summary>
    [Theory]
    [InlineData("""{ "b": { "class": "Warrior", "nodes": [57], "rotation": [ { "ability": 210 } ] } }""",
        "build 'b' rotation entry 1: ability 210 is never held by the build")]
    [InlineData("""{ "b": { "class": "Warrior", "nodes": [57], "rotation": [ { "ability": 319, "when": [ { "mood": ">1" } ] } ] } }""",
        "unknown condition 'mood'")]
    [InlineData("""{ "b": { "class": "Warrior", "nodes": [57], "rotation": [ { "ability": 319, "when": [ { "power": "about 3" } ] } ] } }""",
        "'about 3' is not")]
    [InlineData("""{ "b": { "class": "Warrior", "nodes": [3], "rotation": [ { "ability": 319 } ] } }""",
        "build 'b': node 3: NotConnected")]
    [InlineData("""{ "b": { "class": "Nope", "nodes": [57], "rotation": [ { "ability": 319 } ] } }""", "builds: ")]
    public void Refuse_a_bad_build(string json, string message)
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            ConfigFiles.ParseBuilds(json).Compile("b", TestData.Seeded));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuse_a_build_with_no_rotation() =>
        Assert.Throws<InvalidDataException>(() =>
            ConfigFiles.ParseBuilds("""{ "b": { "class": "Wizard", "nodes": [] } }""").Compile("b", TestData.Seeded));

    private static TargetFile CheckedInTargets() =>
        ConfigFileStore.Load(Path.Combine(s_balanceDir, "targets.json"), ConfigFiles.ParseTargets);

    [Fact]
    public void Refuse_a_misspelt_scenario_field()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ConfigFiles.ParseScenarios("""
            { "runs": 1, "seed": 1, "levels": [1, 1], "builds": ["warrior-axes"], "gear": ["none"],
              "scenarios": [ { "id": "normal-1", "pack": [ { "rarity": "Normal" } ], "levelOfset": 2 } ], "gearProfiles": {} }
            """));

        Assert.StartsWith("scenarios:", error.Message, StringComparison.Ordinal);
        Assert.Contains("levelOfset", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuse_a_misspelt_target_band()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ConfigFiles.ParseTargets("""
            { "scenarios": { "normal-1": { "fightSecs": { "min": 4 } } } }
            """));

        Assert.StartsWith("targets:", error.Message, StringComparison.Ordinal);
        Assert.Contains("fightSecs", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_the_checked_in_targets_against_the_checked_in_scenarios() =>
        CheckedInTargets().Validate(CheckedInScenarios());

    [Theory]
    [InlineData("""{ "scenarios": { "normal-9": {} } }""", "scenario 'normal-9' is not in scenarios.json")]
    [InlineData("""{ "global": { "parityScenario": "nope" } }""", "parityScenario 'nope'")]
    [InlineData("""{ "global": { "resourceScenario": "nope" } }""", "resourceScenario 'nope'")]
    [InlineData("""{ "gradedGear": "epic" }""", "gradedGear 'epic'")]
    [InlineData("""{ "scenarios": { "normal-1": { "winRate": { "min": 90, "max": 80 } } } }""",
        "scenario 'normal-1' winRate has min 90 above max 80")]
    [InlineData("""{ "global": { "killsPerLevel": { "min": 30, "max": 20 } } }""",
        "global killsPerLevel has min 30 above max 20")]
    public void Refuse_targets_that_do_not_fit_the_scenarios(string json, string message)
    {
        TargetFile targets = ConfigFiles.ParseTargets(json);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => targets.Validate(CheckedInScenarios()));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }
}
