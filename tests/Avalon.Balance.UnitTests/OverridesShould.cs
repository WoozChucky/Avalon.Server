using System.Text.Json;
using Avalon.Balance.Core;
using Avalon.Balance.Data;
using Avalon.World.Public.Enums;
using Xunit;

namespace Avalon.Balance.UnitTests;

public class OverridesShould
{
    private static (SeedTables Tables, OverrideReport Report) Apply(string json)
    {
        SeedTables tables = SeedSource.Load();
        using JsonDocument document = JsonDocument.Parse(json);
        return (tables, Overrides.Apply(tables, document.RootElement));
    }

    [Fact]
    public void Change_an_ability_column()
    {
        (SeedTables tables, OverrideReport report) = Apply("""{ "Ability.201.EffectValue": 18 }""");

        Assert.Equal(18u, tables.AbilityTemplates.Single(a => a.Id.Value == 201).EffectValue);
        Assert.Equal(new AppliedOverride("Ability.201.EffectValue", "25", "18"), Assert.Single(report.Applied));
    }

    [Fact]
    public void Change_rows_keyed_by_class_and_level_by_class_and_by_nothing()
    {
        (SeedTables tables, _) = Apply("""
            { "ClassLevelStat.Warrior.3.Strength": 28,
              "ClassStatFactors.Warrior.AttackPerStrength": 2.5,
              "CombatFormula.ArmorBase": 60,
              "CreatureBaseStats.4.Health": 90,
              "CreatureTemplate.8.HealthModifier": 1.2,
              "CreatureRarityModifiers.Elite.DamageMultiplier": 1.5,
              "Item.7.DamageMax1": 12 }
            """);

        Assert.Equal(28u, tables.ClassLevelStats.Single(r => r.Class == CharacterClass.Warrior && r.Level == 3).Strength);
        Assert.Equal(2.5, tables.ClassStatFactors.Single(r => r.Class == CharacterClass.Warrior).AttackPerStrength);
        Assert.Equal(60f, tables.CombatFormulas.Single().ArmorBase);
        Assert.Equal(90u, tables.CreatureBaseStats.Single(r => r.Level == 4).Health);
        Assert.Equal(1.2f, tables.CreatureTemplates.Single(t => t.Id.Value == 8).HealthModifier);
        Assert.Equal(1.5f, tables.CreatureRarityModifiers.Single(r => r.Rarity == CreatureRarity.Elite).DamageMultiplier);
        Assert.Equal(12u, tables.ItemTemplates.Single(t => t.Id.Value == 7).DamageMax1);
    }

    [Fact]
    public void Report_an_override_equal_to_the_seed_as_already_applied()
    {
        (_, OverrideReport report) = Apply("""{ "Ability.200.EffectValue": 12 }""");

        Assert.Empty(report.Applied);
        Assert.Equal(["Ability.200.EffectValue"], report.Stale);
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
    [InlineData("""{ "Nope.1.EffectValue": 1 }""", "unknown table 'Nope'")]
    [InlineData("""{ "Ability.999.EffectValue": 1 }""", "no Ability row '999'")]
    [InlineData("""{ "Ability.201.Nope": 1 }""", "Ability has no column 'Nope'")]
    [InlineData("""{ "Ability.201.Id": 5 }""", "'Id' is the row's key")]
    [InlineData("""{ "Ability.201.Cost": -1 }""", "-1 does not fit")]
    [InlineData("""{ "Ability.201.Cost": 1.5 }""", "1.5 does not fit")]
    [InlineData("""{ "Ability.201.AllowedClasses": 1 }""", "cannot be overridden")]
    [InlineData("""{ "CombatFormula.1.ArmorBase": 1 }""", "CombatFormula has one row and takes no key")]
    [InlineData("""{ "Ability": 1 }""", "is not Table.key.Column")]
    public void Stop_naming_the_override(string json, string message)
    {
        var error = Assert.Throws<InvalidDataException>(() => Apply(json));

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuse_a_key_given_twice_and_write_nothing()
    {
        SeedTables tables = SeedSource.Load();
        using JsonDocument document = JsonDocument.Parse(
            """{ "Ability.200.EffectValue": 30, "Ability.201.EffectValue": 18, "Ability.201.EffectValue": 25 }""");

        var error = Assert.Throws<InvalidDataException>(() => Overrides.Apply(tables, document.RootElement));

        Assert.Contains("'Ability.201.EffectValue' is given twice", error.Message, StringComparison.Ordinal);
        Assert.Equal(12u, tables.AbilityTemplates.Single(a => a.Id.Value == 200).EffectValue);
        Assert.Equal(25u, tables.AbilityTemplates.Single(a => a.Id.Value == 201).EffectValue);
    }

    [Fact]
    public void Refuse_a_computed_column_and_write_nothing()
    {
        SeedTables tables = SeedSource.Load();
        using JsonDocument document = JsonDocument.Parse(
            """{ "Ability.201.EffectValue": 18, "Item.5.Stackable": true }""");

        var error = Assert.Throws<InvalidDataException>(() => Overrides.Apply(tables, document.RootElement));

        Assert.Contains("Override 'Item.5.Stackable'", error.Message, StringComparison.Ordinal);
        Assert.Contains("cannot be overridden", error.Message, StringComparison.Ordinal);
        Assert.Equal(25u, tables.AbilityTemplates.Single(a => a.Id.Value == 201).EffectValue);
    }

    [Fact]
    public void Stop_on_an_override_the_server_would_refuse()
    {
        (SeedTables tables, _) = Apply("""{ "Ability.200.Reach": -1 }""");

        var error = Assert.Throws<InvalidDataException>(() => BalanceData.From(tables));
        Assert.Contains("ability 200 'Cleave'", error.Message, StringComparison.Ordinal);

        (SeedTables formula, _) = Apply("""{ "CombatFormula.ArmorCap": 2 }""");
        Assert.Contains("ArmorCap", Assert.Throws<InvalidDataException>(() => BalanceData.From(formula)).Message,
            StringComparison.Ordinal);
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
