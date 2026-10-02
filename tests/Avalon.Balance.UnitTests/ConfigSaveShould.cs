using System.Text.Json;
using System.Text.Json.Serialization;
using Avalon.Balance.Core;
using Xunit;

namespace Avalon.Balance.UnitTests;

public class ConfigSaveShould
{
    /// <summary>Every property, nothing skipped: two configs with the same neutral JSON are equal field by field.</summary>
    private static readonly JsonSerializerOptions Neutral = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static string Neutral_(BalanceConfig c) => JsonSerializer.Serialize(c.Scenarios, Neutral)
        + JsonSerializer.Serialize(c.Targets, Neutral) + JsonSerializer.Serialize(c.Rotations, Neutral);

    private static BalanceConfig Reload(BalanceConfig config)
    {
        (string s, string t, string r) = ConfigFiles.Save(config);
        return new BalanceConfig(ConfigFiles.ParseScenarios(s), ConfigFiles.ParseTargets(t), ConfigFiles.ParseRotations(r));
    }

    [Fact]
    public void Round_trip_the_checked_in_files()
    {
        BalanceConfig config = TestData.Config();
        (string s, string t, string r) = ConfigFiles.Save(config);

        BalanceConfig again = Reload(config);
        Assert.Equal(ConfigFiles.Save(again), (s, t, r));
    }

    [Fact]
    public void Parse_back_to_an_equal_config()
    {
        BalanceConfig x = TestData.Config();
        x.Scenarios.Scenarios[0].ConeHits = 2;

        BalanceConfig y = Reload(x);

        Assert.Equal(Neutral_(x), Neutral_(y));
    }

    [Fact]
    public void Keep_the_values_that_are_not_defaults()
    {
        BalanceConfig x = TestData.Config();
        x.Scenarios.Scenarios[0].ConeHits = 2;
        BalanceConfig y = Reload(x);

        Assert.Equal(2, y.Scenarios.Scenarios[0].ConeHits);
        Assert.Contains(y.Scenarios.Scenarios, s => s.OffsetFromTemplate);
        Assert.Contains(y.Targets.Scenarios.Values, s => s.HealthLeftPct is { Min: not null, Max: null });
        Assert.Contains(y.Rotations.Values.SelectMany(e => e), e => e.When.Length > 0 && e.When.SelectMany(d => d).Any());
        Assert.Equal(x.Scenarios.GearProfiles.Count, y.Scenarios.GearProfiles.Count);
        Assert.Equal(x.Targets.Global.KillsPerLevel.Min, y.Targets.Global.KillsPerLevel.Min);
    }

    [Fact]
    public void Write_indented_camel_case_with_a_trailing_newline()
    {
        (string s, string t, string r) = ConfigFiles.Save(TestData.Config());

        foreach (string text in new[] { s, t, r })
        {
            Assert.EndsWith("}\n", text);
            Assert.DoesNotContain("\r", text);
        }

        Assert.Contains("\n  \"runs\":", s);
        Assert.Contains("\"gradedGear\":", t);
        Assert.DoesNotContain("\"offset\"", s);
    }

    [Fact]
    public void Write_comparison_operators_literally()
    {
        (_, _, string rotations) = ConfigFiles.Save(TestData.Config());

        Assert.Contains("\">=2\"", rotations);
        Assert.DoesNotContain("\\" + "u003E", rotations, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\" + "u003C", rotations, StringComparison.OrdinalIgnoreCase);
    }
}
