using System.Text.Json;
using Avalon.Balance.Core;
using Xunit;

namespace Avalon.Balance.UnitTests;

public class CatalogShould
{
    [Fact]
    public void Describe_every_overridable_value_with_a_key_the_overrides_accept()
    {
        SeedTables seed = TestData.Seed();
        IReadOnlyList<Tunable> tunables = Catalog.Describe(seed);

        Assert.Contains(tunables, t => t.Key == "CombatFormula.CritMultiplier" && t.Type == "float");
        Assert.All(tunables.OrderBy(t => t.Key, StringComparer.Ordinal).Take(50), t =>
        {
            // Setting each value to its own seed value is accepted and reported stale, never refused.
            OverrideReport report = Overrides.Apply(seed.Clone(), JsonDocument.Parse($$"""{ "{{t.Key}}": {{Json(t)}} }""").RootElement);
            Assert.Contains(t.Key, report.Stale);
        });
    }

    [Fact]
    public void Describe_nothing_the_overrides_would_refuse_and_no_key_twice()
    {
        SeedTables seed = TestData.Seed();
        IReadOnlyList<Tunable> tunables = Catalog.Describe(seed);

        Assert.Equal(tunables.Count, tunables.Select(t => t.Key).Distinct(StringComparer.Ordinal).Count());
        string all = "{" + string.Join(",", tunables.Select(t => $"\"{t.Key}\": {Json(t)}")) + "}";
        OverrideReport report = Overrides.Apply(seed.Clone(), JsonDocument.Parse(all).RootElement);
        Assert.Equal(tunables.Count, report.Stale.Count);
        Assert.Empty(report.Applied);
    }

    [Fact]
    public void Show_seed_values_the_way_the_override_report_does()
    {
        SeedTables seed = TestData.Seed();
        Tunable crit = Catalog.Describe(seed).Single(t => t.Key == "CombatFormula.CritMultiplier");

        OverrideReport report = Overrides.Apply(seed.Clone(), JsonDocument.Parse("""{ "CombatFormula.CritMultiplier": 9.5 }""").RootElement);

        Assert.Equal(crit.SeedValue, report.Applied.Single().Seed);
    }

    [Fact]
    public void Name_abilities_creatures_and_items()
    {
        IReadOnlyList<Tunable> tunables = Catalog.Describe(TestData.Seed());
        Assert.Contains(tunables, t => t.Table == "Ability" && !string.IsNullOrWhiteSpace(t.Display) && !t.Display.StartsWith("Ability ", StringComparison.Ordinal));
        Assert.Contains(tunables, t => t.Table == "CreatureTemplate" && !string.IsNullOrWhiteSpace(t.Display));
        Assert.Contains(tunables, t => t.Table == "Item" && !string.IsNullOrWhiteSpace(t.Display));
    }

    private static string Json(Tunable t) => t.SeedValue == "null" ? "null"
        : t.Type is "bool" ? t.SeedValue.ToLowerInvariant()
        : t.Type is "int" or "uint" or "ushort" or "float" or "double" or "byte" or "short" or "long" or "ulong" ? t.SeedValue
        : JsonSerializer.Serialize(t.SeedValue);
}
