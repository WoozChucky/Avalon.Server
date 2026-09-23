using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalon.Exporter;
using Xunit;

namespace Avalon.Shared.UnitTests.Schema;

/// <summary>
/// Holds the checked-in item schema to the C# it was exported from. The packet carries an
/// ItemTemplateId and a Rarity as bare numbers; without this file a client has no way to learn
/// that 3 means Epic, and a reordered enum would change every item's rarity silently.
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
        using JsonDocument document = JsonDocument.Parse(ItemSchema.Generate());
        JsonElement enums = document.RootElement.GetProperty("enums");

        foreach (string name in new[]
                 {
                     "ItemRarity", "ItemSlotType", "ItemClass", "ItemSubClass",
                     "DamageType", "StatType", "ItemTemplateFlags",
                 })
        {
            Assert.True(enums.TryGetProperty(name, out JsonElement values), $"{name} is missing");
            Assert.NotEmpty(values.EnumerateArray());
        }

        JsonElement rarity = enums.GetProperty("ItemRarity");
        Assert.Contains(rarity.EnumerateArray(),
            value => value.GetProperty("name").GetString() == "Epic" && value.GetProperty("value").GetInt64() == 4);
    }

    [Fact]
    public void Say_Which_Fields_Are_Nullable()
    {
        using JsonDocument document = JsonDocument.Parse(ItemSchema.Generate());
        JsonElement fields = document.RootElement.GetProperty("fields");

        JsonElement name = fields.EnumerateArray().Single(f => f.GetProperty("name").GetString() == "Name");
        Assert.Equal("string", name.GetProperty("type").GetString());

        JsonElement slot = fields.EnumerateArray().Single(f => f.GetProperty("name").GetString() == "Slot");
        Assert.True(slot.GetProperty("nullable").GetBoolean());

        JsonElement id = fields.EnumerateArray().Single(f => f.GetProperty("name").GetString() == "Id");
        Assert.False(id.GetProperty("nullable").GetBoolean());
    }
}
