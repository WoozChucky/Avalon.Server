using System.Diagnostics;
using System.Text.Json;
using Avalon.Balance;
using Avalon.Balance.Core;
using Avalon.Balance.Data;
using Avalon.Balance.Reporting;

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
    OverrideReport overrides = OverrideFiles.Apply(tables, overridesPath, required: options.OverridesPath is not null);
    BalanceData data = BalanceData.From(tables);

    ScenarioFile scenarios = ConfigFileStore.Load(Path.Combine(balanceDir, "scenarios.json"), ConfigFiles.ParseScenarios);
    scenarios.Validate(data);
    TargetFile targets = ConfigFileStore.Load(Path.Combine(balanceDir, "targets.json"), ConfigFiles.ParseTargets);
    targets.Validate(scenarios);
    RotationFile rotations = ConfigFileStore.Load(Path.Combine(balanceDir, "rotations.json"), ConfigFiles.ParseRotations);

    var runner = new BalanceRunner(data, scenarios, rotations);
    RunPlan plan = runner.Plan(options.Class, options.Scenario, options.Runs, options.Seed);
    var clock = Stopwatch.StartNew();
    IReadOnlyList<RowResult> rows = runner.Run(plan);
    GradeReport grades = Grader.Grade(rows, data, scenarios, targets);

    string outDir = options.OutDir is { } o ? Path.GetFullPath(o) : Path.Combine(balanceDir, "out");
    Directory.CreateDirectory(outDir);
    var context = new ReportContext(DateTimeOffset.UtcNow, Commit(root), plan.Seed, plan.Runs, overrides, rows, grades, scenarios, targets);
    File.WriteAllText(Path.Combine(outDir, "report.html"), HtmlReport.Render(context));
    File.WriteAllText(Path.Combine(outDir, "results.csv"), CsvReport.Render(rows, grades));

    Console.WriteLine($"{rows.Count} rows x {plan.Runs} runs in {clock.Elapsed.TotalSeconds:0.0} s: " +
                      $"{grades.Count(Grade.Green)} green, {grades.Count(Grade.Yellow)} yellow, {grades.Count(Grade.Red)} red");
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
            WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false,
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
