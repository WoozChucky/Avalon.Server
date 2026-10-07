using Avalon.Balance.Core;
using Avalon.Balance.Data;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;
using Xunit;

namespace Avalon.Balance.UnitTests;

public class ConfigFilesShould
{
    private static readonly string s_balanceDir = Path.Combine(RepositoryRoot.Find(), "balance");

    private static ScenarioFile CheckedInScenarios() =>
        ConfigFileStore.Load(Path.Combine(s_balanceDir, "scenarios.json"), ConfigFiles.ParseScenarios);

    [Fact]
    public void Load_the_checked_in_files_against_the_seed()
    {
        BalanceData data = TestData.Seeded;
        ScenarioFile scenarios = CheckedInScenarios();
        scenarios.Validate(data);
        TargetFile targets = ConfigFileStore.Load(Path.Combine(s_balanceDir, "targets.json"), ConfigFiles.ParseTargets);
        RotationFile rotations = ConfigFileStore.Load(Path.Combine(s_balanceDir, "rotations.json"), ConfigFiles.ParseRotations);

        Assert.Equal(Enumerable.Range(1, 10).Select(l => (ushort)l), scenarios.LevelRange());
        Assert.Equal("forest", targets.GradedGear);
        foreach (CharacterClass c in scenarios.Classes)
            Assert.NotEmpty(rotations.Compile(c, data));
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

        foreach (CharacterClass c in Enum.GetValues<CharacterClass>())
        {
            ItemTemplate[] items = scenarios.GearFor(profile, c).Select(data.Item).ToArray();
            Assert.All(items, i => Assert.Contains(c, i.AllowedClasses));
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

        foreach (CharacterClass c in Enum.GetValues<CharacterClass>())
            Assert.All(CheckedInScenarios().GearFor("starter", c), id => Assert.Contains(id, sold));
    }

    [Fact]
    public void Take_the_forest_profile_from_uncommon_drops()
    {
        BalanceData data = TestData.Seeded;
        foreach (CharacterClass c in Enum.GetValues<CharacterClass>())
            Assert.All(CheckedInScenarios().GearFor("forest", c), id => Assert.Equal(ItemRarity.Uncommon, data.Item(id).Rarity));
    }

    [Fact]
    public void Refuse_a_pack_rarity_with_no_hostile_template()
    {
        ScenarioFile scenarios = ConfigFiles.ParseScenarios("""
            { "runs": 1, "seed": 1, "levels": [1, 1], "classes": ["Warrior"], "gear": ["none"],
              "scenarios": [ { "id": "town", "pack": [ { "template": 1 } ] } ], "gearProfiles": {} }
            """);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => scenarios.Validate(TestData.Seeded));
        Assert.Contains("scenario 'town'", error.Message, StringComparison.Ordinal);

        SeedTables tables = SeedSource.Load();
        tables.CreatureTemplates.Single(t => t.Id.Value == 10).Rarity = CreatureRarity.Rare;   // no Boss left
        ScenarioFile boss = ConfigFiles.ParseScenarios("""
            { "runs": 1, "seed": 1, "levels": [1, 1], "classes": ["Warrior"], "gear": ["none"],
              "scenarios": [ { "id": "boss-1", "pack": [ { "rarity": "Boss" } ] } ], "gearProfiles": {} }
            """);
        Assert.Contains("no hostile Boss template",
            Assert.Throws<InvalidDataException>(() => boss.Validate(BalanceData.From(tables))).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "Warrior": [ { "ability": 210 } ] }""", "Warrior rotation entry 1: ability 210 is not in the class's kit")]
    [InlineData("""{ "Warrior": [ { "ability": 200, "when": [ { "mood": ">1" } ] } ] }""", "unknown condition 'mood'")]
    [InlineData("""{ "Warrior": [ { "ability": 200, "when": [ { "power": "about 3" } ] } ] }""", "'about 3' is not")]
    public void Refuse_a_bad_rotation(string json, string message)
    {
        RotationFile rotations = ConfigFiles.ParseRotations(json);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => rotations.Compile(CharacterClass.Warrior, TestData.Seeded));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuse_a_class_with_no_rotation() =>
        Assert.Throws<InvalidDataException>(() =>
            ConfigFiles.ParseRotations("{}").Compile(CharacterClass.Wizard, TestData.Seeded));

    private static TargetFile CheckedInTargets() =>
        ConfigFileStore.Load(Path.Combine(s_balanceDir, "targets.json"), ConfigFiles.ParseTargets);

    [Fact]
    public void Refuse_a_misspelt_scenario_field()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ConfigFiles.ParseScenarios("""
            { "runs": 1, "seed": 1, "levels": [1, 1], "classes": ["Warrior"], "gear": ["none"],
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
