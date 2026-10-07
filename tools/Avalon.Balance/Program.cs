using System.Diagnostics;
using System.Text.Json;
using Avalon.Balance;
using Avalon.Balance.Core;
using Avalon.Balance.Data;
using Avalon.Balance.Reporting;
using Avalon.World.Public.Enums;

// dotnet run --project tools/Avalon.Balance
// dotnet run --project tools/Avalon.Balance -- --class Warrior --scenario normal-3 --runs 5000
// dotnet run --project tools/Avalon.Balance -- --overrides balance/try-slam.json --seed 1
// Exit code: 0 no graded row is red, 1 some graded row is red, 2 the input was refused.

try
{
    CliOptions options = CliOptions.Parse(args);
    if (options.Help)
    {
        Console.WriteLine("Usage: Avalon.Balance [--class C] [--scenario ID] [--runs N] [--seed N] [--overrides FILE] [--out DIR]");
        return 0;
    }

    string root = RepositoryRoot.Find();
    string balanceDir = Path.Combine(root, "balance");

    SeedTables tables = SeedSource.Load();
    string overridesPath = options.OverridesPath is { } given ? Path.GetFullPath(given) : Path.Combine(balanceDir, "overrides.json");
    using JsonDocument? overridesFile = OverrideFiles.Read(overridesPath, required: options.OverridesPath is not null);

    var config = new BalanceConfig(
        ConfigFileStore.Load(Path.Combine(balanceDir, "scenarios.json"), ConfigFiles.ParseScenarios),
        ConfigFileStore.Load(Path.Combine(balanceDir, "targets.json"), ConfigFiles.ParseTargets),
        ConfigFileStore.Load(Path.Combine(balanceDir, "rotations.json"), ConfigFiles.ParseRotations));

    var request = new RunRequest(
        overridesFile?.RootElement,
        null,
        new RunFilter(
            options.Class is { } c ? new HashSet<CharacterClass> { c } : null,
            null,
            null,
            options.Scenario is { } scenarioId ? new HashSet<string>(StringComparer.Ordinal) { scenarioId } : null),
        options.Runs,
        options.Seed);

    var clock = Stopwatch.StartNew();
    RunResult result = Simulation.Run(tables, config, request, null, CancellationToken.None);
    if (result.Status != RunStatus.Done)
    {
        foreach (Issue issue in result.Issues)
            Console.Error.WriteLine(issue.Message);
        return 2;
    }

    IReadOnlyList<RowResult> rows = result.Rows;
    GradeReport grades = result.Grades;
    OverrideReport overrides = result.Overrides;

    string outDir = options.OutDir is { } o ? Path.GetFullPath(o) : Path.Combine(balanceDir, "out");
    Directory.CreateDirectory(outDir);
    var context = new ReportContext(DateTimeOffset.UtcNow, Commit(root), result.Seed, result.RunsPerRow, overrides, rows, grades,
        config.Scenarios, config.Targets);
    File.WriteAllText(Path.Combine(outDir, "report.html"), HtmlReport.Render(context));
    File.WriteAllText(Path.Combine(outDir, "results.csv"), CsvReport.Render(rows, grades));

    Console.WriteLine($"{rows.Count} rows x {result.RunsPerRow} runs in {clock.Elapsed.TotalSeconds:0.0} s: " +
                      $"{result.Summary.Green} green, {result.Summary.Yellow} yellow, {result.Summary.Red} red");
    foreach (string stale in overrides.Stale)
        Console.WriteLine($"stale override (already in the seed): {stale}");
    Console.WriteLine($"Report: {Path.Combine(outDir, "report.html")}");
    return grades.AnyRed ? 1 : 0;
}
catch (Exception e) when (e is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException
                                or JsonException or RepositoryNotFoundException)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}

static string Commit(string root)
{
    try
    {
        using var git = Process.Start(new ProcessStartInfo("git", "rev-parse --short HEAD")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        });
        if (git is null) return "unknown";
        string sha = git.StandardOutput.ReadToEnd().Trim();
        git.WaitForExit();
        return git.ExitCode == 0 && sha.Length > 0 ? sha : "unknown";
    }
    catch (System.ComponentModel.Win32Exception)
    {
        return "unknown";
    }
}
