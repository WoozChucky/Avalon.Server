using System.Text.Json;

namespace Avalon.World.Testing.Scenarios;

/// <summary>
/// The committed allocation baseline (<c>perf/scenario-allocations.json</c>): each scenario's
/// <see cref="ScenarioReport.BytesPerWindow" />, and the rule that judges a run against it.
/// </summary>
public static class AllocationBaseline
{
    /// <summary>A change this small never counts, so a near-zero baseline does not fail on one stray allocation.</summary>
    private const long SlackBytes = 256;

    /// <summary>One scenario's committed figures.</summary>
    public sealed record Entry(long BytesPerWindow, double BytesPerPlayerPerTick);

    /// <summary>The whole file: the commit and date (<c>yyyy-MM-dd</c>) it was measured at, and every scenario by name.</summary>
    public sealed record File(string? Commit, string Date, IReadOnlyDictionary<string, Entry> Scenarios);

    public enum Verdict { Within, Regressed, Improved }

    /// <summary>
    /// The gate rule: regressed when more than 5% and more than 256 B over the committed bytes, improved when more than
    /// 5% and more than 256 B under them, within otherwise.
    /// </summary>
    public static Verdict Compare(long current, long committed)
    {
        // current > committed × 1.05 and current < committed × 0.95, kept in whole numbers.
        if (current * 20 > committed * 21 && current > committed + SlackBytes)
            return Verdict.Regressed;
        if (current * 20 < committed * 19 && current < committed - SlackBytes)
            return Verdict.Improved;
        return Verdict.Within;
    }

    public static File Read(string path)
    {
        using FileStream stream = System.IO.File.OpenRead(path);
        return JsonSerializer.Deserialize<File>(stream, ScenarioReport.JsonOptions)
               ?? throw new InvalidDataException($"{path} holds no allocation baseline");
    }

    /// <summary>Writes the file as indented JSON, its scenarios sorted by name so a regenerated file diffs line by line.</summary>
    public static void Write(string path, File file)
    {
        ArgumentNullException.ThrowIfNull(file);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var scenarios = new SortedDictionary<string, Entry>(StringComparer.Ordinal);
        foreach ((string name, Entry entry) in file.Scenarios)
            scenarios.Add(name, entry);

        using FileStream stream = System.IO.File.Create(path);
        JsonSerializer.Serialize(stream, file with { Scenarios = scenarios }, ScenarioReport.JsonOptions);
        stream.WriteByte((byte)'\n');
    }
}
