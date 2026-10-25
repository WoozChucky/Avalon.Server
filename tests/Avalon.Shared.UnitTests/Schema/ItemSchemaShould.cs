using System.Text.Json;
using Avalon.Exporter;
using Xunit;

namespace Avalon.Shared.UnitTests.Schema;

/// <summary>
/// Holds the checked-in item schema to the C# it was exported from. The packet carries an
/// ItemTemplateId and a Rarity as bare numbers; without this file a client has no way to learn
/// that 3 means Unique, and a reordered enum would change every item's rarity silently.
/// </summary>
public class ItemSchemaShould
{
    private const string RegenerateCommand = "dotnet run --project tools/Avalon.Exporter -- item-schema";

    [Fact]
    public void Match_The_Types_It_Was_Exported_From()
    {
        string path = Path.Combine(
            RepositoryLayout.Root(), "schema", ItemSchema.DirectoryName, ItemSchema.FileName);

        Assert.True(File.Exists(path),
            $"schema/{ItemSchema.DirectoryName}/{ItemSchema.FileName} is missing. Create it with:" +
            $"{Environment.NewLine}    {RegenerateCommand}");

        string checkedIn = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        string regenerated = ItemSchema.Generate().Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.True(
            string.Equals(checkedIn, regenerated, StringComparison.Ordinal),
            $"schema/{ItemSchema.DirectoryName}/{ItemSchema.FileName} no longer matches the types it " +
            $"was exported from. Re-export it:{Environment.NewLine}    {RegenerateCommand}");
    }

    [Fact]
    public void Name_Every_Value_Of_Every_Vocabulary()
    {
        using var document = JsonDocument.Parse(ItemSchema.Generate());
        JsonElement enums = document.RootElement.GetProperty("enums");

        foreach (string name in new[]
                 {
                     "ItemRarity", "ItemSlotType", "ItemClass", "ItemSubClass",
                     "DamageType", "StatType", "ItemTemplateFlags", "CharacterClass", "ItemBaseKind", "ItemBaseStat",
                 })
        {
            Assert.True(enums.TryGetProperty(name, out JsonElement values), $"{name} is missing");
            Assert.NotEmpty(values.EnumerateArray());
        }

        JsonElement rarity = enums.GetProperty("ItemRarity");
        Assert.Contains(rarity.EnumerateArray(),
            value => value.GetProperty("name").GetString() == "Unique" && value.GetProperty("value").GetInt64() == 3);
    }

    [Fact]
    public void Name_The_Vocabulary_A_Collection_Field_Draws_Its_Elements_From()
    {
        using var document = JsonDocument.Parse(ItemSchema.Generate());
        JsonElement fields = document.RootElement.GetProperty("fields");

        JsonElement allowedClasses = fields.EnumerateArray()
            .Single(f => f.GetProperty("name").GetString() == "AllowedClasses");

        Assert.Equal("array", allowedClasses.GetProperty("type").GetString());
        Assert.Equal("enum", allowedClasses.GetProperty("element").GetString());
        Assert.Equal("CharacterClass", allowedClasses.GetProperty("enum").GetString());

        // #1179: a base's base-stat ranges are rows of their own, described field by field, without their link back to
        // the template they are nested under.
        JsonElement baseStats = fields.EnumerateArray().Single(f => f.GetProperty("name").GetString() == "BaseStats");
        Assert.Equal(("array", "object"), (baseStats.GetProperty("type").GetString(), baseStats.GetProperty("element").GetString()));
        Assert.Equal(
            [("Stat", "enum", "ItemBaseStat"), ("Min", "int32", null), ("Max", "int32", null)],
            baseStats.GetProperty("fields").EnumerateArray().Select(f => (f.GetProperty("name").GetString(),
                f.GetProperty("type").GetString(), f.TryGetProperty("enum", out JsonElement e) ? e.GetString() : null)));
    }

    [Fact]
    public void Say_Which_Fields_Are_Nullable()
    {
        using var document = JsonDocument.Parse(ItemSchema.Generate());
        JsonElement fields = document.RootElement.GetProperty("fields");

        JsonElement name = fields.EnumerateArray().Single(f => f.GetProperty("name").GetString() == "Name");
        Assert.Equal("string", name.GetProperty("type").GetString());
        Assert.False(name.GetProperty("nullable").GetBoolean());

        // A string declared string? (an item's use script) is optional; a plain string (Name) is not.
        JsonElement useScript = fields.EnumerateArray().Single(f => f.GetProperty("name").GetString() == "UseScript");
        Assert.Equal("string", useScript.GetProperty("type").GetString());
        Assert.True(useScript.GetProperty("nullable").GetBoolean());

        JsonElement slot = fields.EnumerateArray().Single(f => f.GetProperty("name").GetString() == "Slot");
        Assert.True(slot.GetProperty("nullable").GetBoolean());

        JsonElement id = fields.EnumerateArray().Single(f => f.GetProperty("name").GetString() == "Id");
        Assert.False(id.GetProperty("nullable").GetBoolean());

        // A template id declared ItemTemplateId? (the base a unique names, #1179) is optional; the Id is not.
        JsonElement baseTemplate = fields.EnumerateArray().Single(f => f.GetProperty("name").GetString() == "BaseTemplateId");
        Assert.Equal(("uint64", true), (baseTemplate.GetProperty("type").GetString(), baseTemplate.GetProperty("nullable").GetBoolean()));
    }
}
