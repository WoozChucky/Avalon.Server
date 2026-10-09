using System.Text.Json;

namespace Avalon.World.Testing.Scenarios;

/// <summary>
/// The committed allocation baseline (<c>perf/scenario-allocations.json</c>): each scenario's
/// <see cref="ScenarioReport.BytesPerWindow" />, and the rule that judges a run against it.
/// </summary>
public static class AllocationBaseline
{
    /// <summary>
    /// The relative band, in percent. Every run since #854 (Debug and Release, Windows and Linux, alone and beside the
    /// other test assemblies) reads within 0.05% of the committed figures, and CI's runner read within 0.09% of them once
    /// the per-send delegate it paid was accounted for; 1% is ten times the widest of those.
    /// </summary>
    public const int TolerancePercent = 1;

    /// <summary>A change this small never counts, so a near-zero baseline does not fail on one stray allocation.</summary>
    public const long SlackBytes = 256;

    /// <summary>One scenario's committed figures.</summary>
    public sealed record Entry(long BytesPerWindow, double BytesPerPlayerPerTick);

    /// <summary>The whole file: the commit and date (<c>yyyy-MM-dd</c>) it was measured at, and every scenario by name.</summary>
    public sealed record File(string? Commit, string Date, IReadOnlyDictionary<string, Entry> Scenarios);

    public enum Verdict { Within, Regressed, Improved }

    /// <summary>
    /// The gate rule: regressed when more than <see cref="TolerancePercent" /> and more than <see cref="SlackBytes" /> over
    /// the committed bytes, improved when as far under them, within otherwise.
    /// </summary>
    public static Verdict Compare(long current, long committed)
    {
        // current > committed × 1.01 and current < committed × 0.99, kept in whole numbers.
        if (current * 100 > committed * (100 + TolerancePercent) && current > committed + SlackBytes)
            return Verdict.Regressed;
        if (current * 100 < committed * (100 - TolerancePercent) && current < committed - SlackBytes)
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
