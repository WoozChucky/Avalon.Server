using System.Text.RegularExpressions;
using Avalon.World.Public.Enums;

namespace Avalon.Balance.Core;

/// <summary>The one entry point a balance service calls. Nothing escapes as an exception but a bug: refusals are Issues.</summary>
public static partial class Simulation
{
    /// <summary>
    /// Runs the request against a copy of <paramref name="seed" /> (which is never changed, so one loaded seed serves
    /// many runs). <paramref name="progress" /> may be called from worker threads, one row at a time, with RowsDone
    /// rising by one each call. Progress is best-effort: an exception from the handler is ignored. A cancelled run
    /// returns Cancelled with no rows, even when the token fires as the last row finishes.
    /// </summary>
    public static RunResult Run(SeedTables seed, BalanceConfig defaults, RunRequest request,
        IProgress<RunProgress>? progress, CancellationToken ct)
    {
        BalanceConfig config = request.Config ?? defaults;
        int seedValue = request.Seed ?? config.Scenarios.Seed;
        int runs = request.RunsPerRow ?? config.Scenarios.Runs;
        OverrideReport overrides = OverrideReport.None;

        RunResult Invalid(params Issue[] issues) => Empty(RunStatus.Invalid, overrides, issues, seedValue, runs);

        if (ct.IsCancellationRequested)
            return Empty(RunStatus.Cancelled, overrides, [], seedValue, runs);

        SeedTables tables = seed.Clone();
        if (request.Overrides is { } given)
        {
            try
            {
                overrides = Overrides.Apply(tables, given);
            }
            catch (InvalidDataException e)
            {
                Match key = OverrideKey().Match(e.Message);
                return Invalid(new Issue(key.Success ? $"overrides.{key.Groups[1].Value}" : "overrides", e.Message));
            }
        }

        BalanceData data;
        try
        {
            data = BalanceData.From(tables);
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or InvalidOperationException)
        {
            return Invalid(new Issue("seed", e.Message));
        }

        List<Issue> configIssues = [];
        try
        {
            config.Scenarios.Validate(data);
            foreach (CharacterClass characterClass in config.Scenarios.Classes)
            foreach (ushort level in config.Scenarios.LevelRange())
                if (!data.Tables.ClassLevelStats.Any(r => r.Class == characterClass && r.Level == level))
                    throw new InvalidDataException($"scenarios: ClassLevelStat {characterClass} level {level} is not seeded");
            try
            {
                config.Targets.Validate(config.Scenarios);
            }
            catch (InvalidDataException e)
            {
                configIssues.Add(new Issue("targets", e.Message));
            }
        }
        catch (InvalidDataException e)
        {
            configIssues.Add(new Issue("scenarios", e.Message));
        }

        foreach (CharacterClass characterClass in config.Scenarios.Classes)
        {
            try
            {
                config.Rotations.Compile(characterClass, data);
            }
            catch (InvalidDataException e)
            {
                configIssues.Add(new Issue($"rotations.{characterClass}", e.Message));
            }
        }

        if (configIssues.Count > 0)
            return Invalid(configIssues.ToArray());

        var runner = new BalanceRunner(data, config.Scenarios, config.Rotations);
        RunPlan plan;
        try
        {
            plan = runner.Plan(request.Filter ?? RunFilter.None, request.RunsPerRow, request.Seed);
        }
        catch (PlanRefusedException e)
        {
            return Invalid(new Issue(e.Path, e.Message));
        }

        int total = plan.Keys().Count();
        int done = 0;
        object gate = new();
        Action<RowResult> onRowDone = _ =>
        {
            if (progress is null) return;
            // Under the lock so the reports reach the caller in order, one row each.
            lock (gate)
            {
                RunProgress update = new(++done, total);
                try
                {
                    progress.Report(update);
                }
                catch (Exception)
                {
                    // Progress is best-effort: a caller's faulty handler must not abort the run.
                }
            }
        };

        IReadOnlyList<RowResult> rows;
        try
        {
            rows = runner.Run(plan, ct, onRowDone);
        }
        catch (OperationCanceledException)
        {
            return Empty(RunStatus.Cancelled, overrides, [], plan.Seed, plan.Runs);
        }
        catch (AggregateException e) when (e.Flatten().InnerExceptions.All(x => x is OperationCanceledException))
        {
            return Empty(RunStatus.Cancelled, overrides, [], plan.Seed, plan.Runs);
        }
        catch (AggregateException e) when (e.Flatten().InnerExceptions.All(x => x is InvalidDataException))
        {
            // Backstop: data a check above missed, found while a row ran.
            return Invalid(new Issue("run", e.Flatten().InnerExceptions[0].Message));
        }

        if (ct.IsCancellationRequested)
            return Empty(RunStatus.Cancelled, overrides, [], plan.Seed, plan.Runs);

        GradeReport grades = Grader.Grade(rows, data, config.Scenarios, config.Targets);
        return new RunResult(RunStatus.Done, rows, grades, rows.Select(r => r.Key).ToList(),
            new RunSummary(grades.Count(Grade.Green), grades.Count(Grade.Yellow), grades.Count(Grade.Red)),
            overrides, [], plan.Seed, plan.Runs);
    }

    private static RunResult Empty(RunStatus status, OverrideReport overrides, IReadOnlyList<Issue> issues, int seed, int runs) =>
        new(status, [], new GradeReport([]), [], new RunSummary(0, 0, 0), overrides, issues, seed, runs);

    [GeneratedRegex(@"^Override '([^']+)'")]
    private static partial Regex OverrideKey();
}
