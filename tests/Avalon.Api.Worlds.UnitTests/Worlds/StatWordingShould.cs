using System.Text.Json;
using Avalon.Api.Contract;
using Avalon.Api.Worlds.Previews;
using Xunit;

namespace Avalon.Api.Worlds.UnitTests.Worlds;

/// <summary>
/// schema/items/stat-wording-v1.json is the agreement between the link preview and the public site's tooltip (#1189):
/// one row per stat type and the attack times, each with the text the preview writes. The site tests its formatter
/// against the same file, so a stat added here, or a change to the preview's wording, fails until the file is
/// re-exported and the site writes it the same.
/// </summary>
public class StatWordingShould
{
    private const string FileName = "stat-wording-v1.json";
    private const string RegenerateCommand = "dotnet run --project tools/Avalon.Exporter -- stat-wording";

    [Fact]
    public void Hold_a_row_for_every_stat_type_with_the_text_the_preview_writes()
    {
        string path = Path.Combine(RepositoryRoot(), "schema", "items", FileName);
        Assert.True(File.Exists(path), $"schema/items/{FileName} is missing. Create it with: {RegenerateCommand}");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        List<(string Stat, int Id, uint Value, string Text)> stats = [.. root.GetProperty("stats").EnumerateArray()
            .Select(row => (row.GetProperty("stat").GetString()!, row.GetProperty("id").GetInt32(),
                row.GetProperty("value").GetUInt32(), row.GetProperty("text").GetString()!))];
        string[] names = Enum.GetNames<StatType>();
        Assert.True(names.SequenceEqual(stats.Select(row => row.Stat)),
            $"schema/items/{FileName} does not hold one row per StatType member in value order (missing: " +
            $"{string.Join(", ", names.Except(stats.Select(row => row.Stat)))}; not a stat: " +
            $"{string.Join(", ", stats.Select(row => row.Stat).Except(names))}). Re-export it with {RegenerateCommand}.");

        IEnumerable<string> wrong = stats
            .Select(row => (row, Type: Enum.Parse<StatType>(row.Stat)))
            .Where(r => r.row.Id != (int)r.Type || r.row.Text != LinkPreviewText.FormatStat(r.Type, r.row.Value))
            .Select(r => $"{r.row.Stat} ({(int)r.Type}) reads \"{LinkPreviewText.FormatStat(r.Type, r.row.Value)}\"")
            .Concat(root.GetProperty("attackTimes").EnumerateArray()
                .Select(row => (Ms: row.GetProperty("attackTimeMs").GetUInt32(), Text: row.GetProperty("text").GetString()!))
                .Where(row => row.Text != LinkPreviewText.AttackTime(row.Ms))
                .Select(row => $"{row.Ms} ms reads \"{LinkPreviewText.AttackTime(row.Ms)}\""))
            .ToList();
        Assert.True(!wrong.Any(),
            $"schema/items/{FileName} no longer matches the link preview's wording ({string.Join("; ", wrong)}). " +
            $"Re-export it with {RegenerateCommand}, and make the public site's tooltip write the same.");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Avalon.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("No Avalon.sln above the test output.");
    }
}
