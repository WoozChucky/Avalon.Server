using System.Text.Json;
using Avalon.Api.Contract;
using Avalon.Api.Worlds.Previews;
using Xunit;

namespace Avalon.Api.Worlds.UnitTests.Worlds;

/// <summary>
/// The link preview writes item stats and attack time as the public site's tooltip does (#1189). stat-wording.json
/// beside this file is a verbatim copy of the Dashboard's packages/shared/src/items/stat-wording.json (Dashboard #460),
/// which the site's formatter tests against too; refresh the copy from there, never edit it here. A new stat type fails
/// here until the shared file has its row and the preview writes it. Its <c>spans</c> rows, the ranges a copy rolls
/// within (#1179), run ahead of the Dashboard's file until it takes them.
/// </summary>
public class StatWordingShould
{
    [Fact]
    public void Write_every_row_of_the_shared_wording_file()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Worlds", "stat-wording.json")));
        JsonElement root = document.RootElement;
        long value = root.GetProperty("value").GetInt64();

        List<string> rows = [.. root.GetProperty("stats").EnumerateObject().Select(row => row.Name)];
        Assert.Equal(Enum.GetNames<StatType>().Order(StringComparer.Ordinal), rows.Order(StringComparer.Ordinal));

        // Each row the preview does not write as the shared file does.
        List<string> differing = [.. root.GetProperty("stats").EnumerateObject()
            .Select(row => (Type: Enum.Parse<StatType>(row.Name), Value: value, Text: row.Value.GetString()))
            .Concat(root.GetProperty("negative").EnumerateArray().Select(row =>
                (Type: Enum.Parse<StatType>(row.GetProperty("type").GetString()!), Value: row.GetProperty("value").GetInt64(),
                    Text: row.GetProperty("text").GetString())))
            .Where(row => LinkPreviewText.FormatStat(row.Type, row.Value) != row.Text)
            .Select(row => $"{row.Type} {row.Value}: the preview writes \"{LinkPreviewText.FormatStat(row.Type, row.Value)}\"")
            .Concat(root.GetProperty("attackTime").EnumerateArray()
                .Select(row => (Ms: row.GetProperty("ms") is { ValueKind: JsonValueKind.Number } ms ? ms.GetUInt32() : (uint?)null,
                    Text: row.GetProperty("text").GetString()))
                .Where(row => LinkPreviewText.AttackTime(row.Ms) != row.Text)
                .Select(row => $"attack time {row.Ms?.ToString() ?? "null"}: the preview writes " +
                               $"{LinkPreviewText.AttackTime(row.Ms) ?? "nothing"}"))
            .Concat(root.GetProperty("spans").EnumerateArray()
                .Select(row => (Type: Enum.Parse<StatType>(row.GetProperty("type").GetString()!),
                    Min: row.GetProperty("min").GetInt32(), Max: row.GetProperty("max").GetInt32(),
                    Min2: row.TryGetProperty("min2", out JsonElement min2) ? min2.GetInt32() : (int?)null,
                    Max2: row.TryGetProperty("max2", out JsonElement max2) ? max2.GetInt32() : (int?)null,
                    Text: row.GetProperty("text").GetString()))
                .Select(row => (Row: row, Written: LinkPreviewText.FormatSpan(row.Type, row.Min, row.Max, row.Min2, row.Max2)))
                .Where(span => span.Written != span.Row.Text)
                .Select(span => $"{span.Row.Type} span: the preview writes \"{span.Written}\""))];
        Assert.Empty(differing);
    }
}
