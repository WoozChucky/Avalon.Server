using System.Text.Json;
using Avalon.Exporter;
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
    private const string RegenerateCommand = "dotnet run --project tools/Avalon.Exporter -- stat-wording";

    [Fact]
    public void Match_the_committed_wording_for_every_stat_type_and_attack_time()
    {
        string path = Path.Combine(RepositoryRoot(), "schema", StatWordingExport.DirectoryName, StatWordingExport.FileName);
        Assert.True(File.Exists(path), $"schema/items/{StatWordingExport.FileName} is missing. Create it with: {RegenerateCommand}");

        string committed = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        string rendered = StatWordingExport.Render();

        Dictionary<string, string> was = Rows(committed);
        Dictionary<string, string> now = Rows(rendered);
        IEnumerable<string> differences = now
            .Where(row => !was.TryGetValue(row.Key, out string? text) || text != row.Value)
            .Select(row => $"{row.Key} reads \"{row.Value}\"")
            .Concat(was.Keys.Except(now.Keys).Select(key => $"{key} is gone"));
        Assert.True(string.Equals(committed, rendered, StringComparison.Ordinal),
            $"schema/items/{StatWordingExport.FileName} no longer matches the link preview's wording " +
            $"({string.Join("; ", differences)}). Re-export it with {RegenerateCommand}, and make the public site's " +
            "tooltip write the same.");
    }

    /// <summary>Each row's text by its stat name, or by its attack time.</summary>
    private static Dictionary<string, string> Rows(string json)
    {
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        return root.GetProperty("stats").EnumerateArray()
            .Select(row => (Key: row.GetProperty("stat").GetString()!, Text: row.GetProperty("text").GetString()!))
            .Concat(root.GetProperty("attackTimes").EnumerateArray()
                .Select(row => (Key: $"{row.GetProperty("attackTimeMs").GetUInt32()} ms", Text: row.GetProperty("text").GetString()!)))
            .ToDictionary(row => row.Key, row => row.Text);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Avalon.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("No Avalon.sln above the test output.");
    }
}
