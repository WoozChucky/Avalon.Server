using System.Text.Json;
using Avalon.Common;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Avalon.Database.Character.Conversions;

/// <summary>
/// Stores an instance's <see cref="ItemRolls" /> (#1179) as JSON in one nullable text column:
/// <c>{"rarity":0,"itemLevel":1,"modifiers":[{"id":21,"tier":2,"stat":36,"min":2,"max":5}]}</c>. Rolls are written with
/// their row and never queried, and one column type keeps the Postgres and SQLite models identical. Null stays null.
/// </summary>
public sealed class ItemRollsConverter : ValueConverter<ItemRolls?, string?>
{
    public ItemRollsConverter()
        : base(rolls => ToJson(rolls), json => FromJson(json))
    {
    }

    /// <summary>Structural: two rolls compare equal when every field and modifier does, so EF sees a real change only.</summary>
    public static ValueComparer<ItemRolls?> Comparer { get; } = new(
        (left, right) => Equals(left, right),
        rolls => rolls == null ? 0 : rolls.GetHashCode(),
        rolls => rolls);

    public static string? ToJson(ItemRolls? rolls)
    {
        if (rolls is null)
            return null;

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("rarity", (byte)rolls.Rarity);
            writer.WriteNumber("itemLevel", rolls.ItemLevel);
            writer.WriteStartArray("modifiers");
            foreach (ItemModifierRoll modifier in rolls.Modifiers)
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", modifier.ModifierId);
                writer.WriteNumber("tier", modifier.Tier);
                writer.WriteNumber("stat", (int)modifier.Stat);
                writer.WriteNumber("min", modifier.Min);
                writer.WriteNumber("max", modifier.Max);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    public static ItemRolls? FromJson(string? json)
    {
        if (json is null)
            return null;

        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement modifiers = root.GetProperty("modifiers");
        var rolls = new ItemModifierRoll[modifiers.GetArrayLength()];
        int i = 0;
        foreach (JsonElement modifier in modifiers.EnumerateArray())
        {
            rolls[i++] = new ItemModifierRoll(
                modifier.GetProperty("id").GetUInt16(),
                modifier.GetProperty("tier").GetByte(),
                (StatType)modifier.GetProperty("stat").GetInt32(),
                modifier.GetProperty("min").GetInt32(),
                modifier.GetProperty("max").GetInt32());
        }

        return new ItemRolls(
            (ItemRarity)root.GetProperty("rarity").GetByte(),
            root.GetProperty("itemLevel").GetUInt16(),
            rolls);
    }
}
