using System.Text.Json;
using Avalon.Balance.Core;
using Avalon.Balance.Data;
using Avalon.Common;
using Avalon.World.Public.Enums;
using Xunit;

namespace Avalon.Balance.UnitTests;

public class OverridesShould
{
    private static (SeedTables Tables, OverrideReport Report) Apply(string json)
    {
        SeedTables tables = SeedSource.Load();
        using var document = JsonDocument.Parse(json);
        return (tables, Overrides.Apply(tables, document.RootElement));
    }

    [Fact]
    public void Change_an_ability_column()
    {
        (SeedTables tables, OverrideReport report) = Apply("""{ "Ability.201.Cost": 18 }""");

        Assert.Equal(18u, tables.AbilityTemplates.Single(a => a.Id.Value == 201).Cost);
        Assert.Equal(new AppliedOverride("Ability.201.Cost", "20", "18"), Assert.Single(report.Applied));
    }

    [Fact]
    public void Change_rows_keyed_by_class_and_level_by_one_column_and_by_nothing()
    {
        (SeedTables tables, _) = Apply("""
            { "ClassLevelStat.Warrior.3.Strength": 28,
              "CombatFormula.ManaPerIntellect": 2.5,
              "CombatFormula.ArmorBase": 60,
              "CreatureBaseStats.4.Health": 90,
              "CreatureTemplate.8.HealthModifier": 1.2,
              "CreatureRarityModifiers.Elite.DamageMultiplier": 1.5,
              "Item.7.BuyPrice": 300 }
            """);

        Assert.Equal(28u, tables.ClassLevelStats.Single(r => r.Class == CharacterClass.Warrior && r.Level == 3).Strength);
        Assert.Equal(2.5f, tables.CombatFormulas.Single().ManaPerIntellect);
        Assert.Equal(60f, tables.CombatFormulas.Single().ArmorBase);
        Assert.Equal(90u, tables.CreatureBaseStats.Single(r => r.Level == 4).Health);
        Assert.Equal(1.2f, tables.CreatureTemplates.Single(t => t.Id.Value == 8).HealthModifier);
        Assert.Equal(1.5f, tables.CreatureRarityModifiers.Single(r => r.Rarity == CreatureRarity.Elite).DamageMultiplier);
        Assert.Equal(300u, tables.ItemTemplates.Single(t => t.Id.Value == 7).BuyPrice);
    }

    [Fact]
    public void Report_an_override_equal_to_the_seed_as_already_applied()
    {
        (_, OverrideReport report) = Apply("""{ "Ability.200.Cost": 6 }""");

        Assert.Empty(report.Applied);
        Assert.Equal(["Ability.200.Cost"], report.Stale);
    }

    /// <summary>A <c>string?</c> column (an item's use script) takes null; a non-nullable string (a name) still refuses it.</summary>
    [Fact]
    public void Clear_a_nullable_text_column_and_refuse_null_on_a_required_one()
    {
        (SeedTables tables, OverrideReport report) = Apply("""{ "Item.1.UseCooldownGroup": null }""");

        Assert.Null(tables.ItemTemplates.Single(t => t.Id.Value == 1).UseCooldownGroup);
        Assert.Equal(new AppliedOverride("Item.1.UseCooldownGroup", "potion", "null"), Assert.Single(report.Applied));
        Assert.Contains("cannot be null",
            Assert.Throws<InvalidDataException>(() => Apply("""{ "Item.1.Name": null }""")).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "Nope.1.Cost": 1 }""", "unknown table 'Nope'")]
    [InlineData("""{ "Ability.999.Cost": 1 }""", "no Ability row '999'")]
    [InlineData("""{ "Ability.201.Nope": 1 }""", "Ability has no column 'Nope'")]
    [InlineData("""{ "Ability.201.Id": 5 }""", "'Id' is the row's key")]
    [InlineData("""{ "Ability.201.Cost": -1 }""", "-1 does not fit")]
    [InlineData("""{ "Ability.201.Cost": 1.5 }""", "1.5 does not fit")]
    [InlineData("""{ "Item.1.AllowedClasses": 1 }""", "cannot be overridden")]
    [InlineData("""{ "CombatFormula.1.ArmorBase": 1 }""", "CombatFormula has one row and takes no key")]
    [InlineData("""{ "Ability": 1 }""", "is not Table.key.Column")]
    [InlineData("""{ "ItemModifierStat.16.0.Stat": 9999 }""", "9999 is no StatType")]
    [InlineData("""{ "ItemTemplateBaseStat.7.4.Min": 1 }""", "no ItemTemplateBaseStat row '7.4'")]
    public void Stop_naming_the_override(string json, string message)
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => Apply(json));

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuse_a_key_given_twice_and_write_nothing()
    {
        SeedTables tables = SeedSource.Load();
        using var document = JsonDocument.Parse(
            """{ "Ability.200.Cost": 30, "Ability.201.Cost": 18, "Ability.201.Cost": 25 }""");

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => Overrides.Apply(tables, document.RootElement));

        Assert.Contains("'Ability.201.Cost' is given twice", error.Message, StringComparison.Ordinal);
        Assert.Equal(6u, tables.AbilityTemplates.Single(a => a.Id.Value == 200).Cost);
        Assert.Equal(20u, tables.AbilityTemplates.Single(a => a.Id.Value == 201).Cost);
    }

    [Fact]
    public void Refuse_a_computed_column_and_write_nothing()
    {
        SeedTables tables = SeedSource.Load();
        using var document = JsonDocument.Parse(
            """{ "Ability.201.Cost": 18, "Item.5.Stackable": true }""");

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => Overrides.Apply(tables, document.RootElement));

        Assert.Contains("Override 'Item.5.Stackable'", error.Message, StringComparison.Ordinal);
        Assert.Contains("cannot be overridden", error.Message, StringComparison.Ordinal);
        Assert.Equal(20u, tables.AbilityTemplates.Single(a => a.Id.Value == 201).Cost);
    }

    [Theory]
    [InlineData("""{ "Ability.200.Reach": -1 }""", "ability 200 'Cleave'")]
    [InlineData("""{ "CombatFormula.ArmorCap": 2 }""", "ArmorCap")]
    [InlineData("""{ "ItemTemplateBaseStat.7.DamageMin.Min": 999 }""", "item 7 '")]
    [InlineData("""{ "Item.112.Rarity": "Magic" }""", "item 112 'Hearthstone Ring': a Magic template links no modifier")]
    [InlineData("""{ "ItemModifierStat.16.0.Min": 999 }""", "modifier 16 line 0: its range 999-")]
    public void Stop_on_an_override_the_server_would_refuse(string json, string message)
    {
        (SeedTables tables, _) = Apply(json);

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => BalanceData.From(tables));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Treat_a_missing_default_file_as_empty_and_a_missing_named_file_as_an_error()
    {
        SeedTables tables = SeedSource.Load();
        string missing = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");

        Assert.Same(OverrideReport.None, OverrideFiles.Apply(tables, missing, required: false));
        Assert.Throws<FileNotFoundException>(() => OverrideFiles.Apply(tables, missing, required: true));
    }

    [Fact]
    public void Name_a_file_that_is_not_json()
    {
        SeedTables tables = SeedSource.Load();
        string broken = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(broken, "{ bad");
        try
        {
            InvalidDataException e = Assert.Throws<InvalidDataException>(() => OverrideFiles.Apply(tables, broken, required: true));
            Assert.Contains(broken, e.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(broken);
        }
    }
}
