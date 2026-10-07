using Avalon.Combat;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;

namespace Avalon.Balance.Core;

public sealed record RunPlan(
    IReadOnlyList<CharacterClass> Classes,
    IReadOnlyList<ushort> Levels,
    IReadOnlyList<string> Gear,
    IReadOnlyList<Scenario> Scenarios,
    int Runs,
    int Seed)
{
    public IEnumerable<RowKey> Keys() =>
        from s in Scenarios
        from g in Gear
        from c in Classes
        from l in Levels
        select new RowKey(c, l, g, s.Id);
}

/// <summary>A filter or count the files cannot satisfy; <see cref="Path" /> says which (filter.levels, runsPerRow, ...).</summary>
public sealed class PlanRefusedException(string path, string message) : ArgumentException(message)
{
    public string Path { get; } = path;
}

public sealed class BalanceRunner(BalanceData data, ScenarioFile scenarios, RotationFile rotations)
{
    /// <exception cref="ArgumentException">A filter names a class or scenario the files do not have.</exception>
    public RunPlan Plan(CharacterClass? onlyClass, string? onlyScenario, int? runs, int? seed)
    {
        var filter = new RunFilter(
            onlyClass is { } c ? new HashSet<CharacterClass> { c } : null,
            null,
            null,
            onlyScenario is { } id ? new HashSet<string>(StringComparer.Ordinal) { id } : null);
        try
        {
            return Plan(filter, runs, seed);
        }
        catch (PlanRefusedException e)
        {
            throw new ArgumentException(e.Message);
        }
    }

    /// <summary>
    /// The rows the filter keeps, in the files' order. A null set keeps everything; a value the files do not have,
    /// or an empty set, is refused.
    /// </summary>
    /// <exception cref="PlanRefusedException">The first refusal, with the filter it belongs to.</exception>
    public RunPlan Plan(RunFilter filter, int? runs, int? seed)
    {
        IReadOnlyList<CharacterClass> classes = Pick(filter.Classes, scenarios.Classes, "filter.classes", "classes",
            c => $"Class '{c}' is not in scenarios.json classes");
        if (runs is < 1)
            throw new PlanRefusedException("runsPerRow", $"--runs must be 1 or more, not {runs}");
        IReadOnlyList<ushort> levels = Pick(filter.Levels, scenarios.LevelRange(), "filter.levels", "levels",
            l => $"Level {l} is not in scenarios.json levels [{scenarios.Levels[0]}, {scenarios.Levels[1]}]");
        IReadOnlyList<string> gear = Pick(filter.Gear, scenarios.Gear, "filter.gear", "gear",
            g => $"Gear '{g}' is not in scenarios.json gear (known: {string.Join(", ", scenarios.Gear)})");
        IReadOnlyList<Scenario> picked = Pick(filter.Scenarios, scenarios.Scenarios.Select(s => s.Id).ToList(), "filter.scenarios", "scenarios",
                id => $"Unknown scenario '{id}' (known: {string.Join(", ", scenarios.Scenarios.Select(s => s.Id))})")
            .Select(scenarios.Find).ToList();

        return new RunPlan(classes, levels, gear, picked, runs ?? scenarios.Runs, seed ?? scenarios.Seed);
    }

    private static List<T> Pick<T>(IReadOnlySet<T>? wanted, IReadOnlyCollection<T> known, string path, string what,
        Func<T, string> unknown)
        where T : notnull
    {
        if (wanted is null) return known.ToList();
        if (wanted.Count == 0) throw new PlanRefusedException(path, $"The {what} filter is empty; leave it out to keep every one");
        foreach (T value in wanted)
            if (!known.Contains(value))
                throw new PlanRefusedException(path, unknown(value));
        return known.Where(wanted.Contains).ToList();
    }

    /// <summary>
    /// Runs every row in parallel. <paramref name="onRowDone" /> is called from worker threads as each row finishes.
    /// </summary>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="ct" /> was cancelled; possibly wrapped in an <see cref="AggregateException" />.
    /// </exception>
    public IReadOnlyList<RowResult> Run(RunPlan plan, CancellationToken ct = default, Action<RowResult>? onRowDone = null)
    {
        var compiled =
            plan.Classes.ToDictionary(c => c, c => rotations.Compile(c, data));
        RowKey[] keys = plan.Keys().ToArray();
        var results = new RowResult[keys.Length];
        var options = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount };
        Parallel.For(0, keys.Length, options, i =>
        {
            results[i] = RunRow(keys[i], plan, compiled[keys[i].Class], ct);
            onRowDone?.Invoke(results[i]);
        });
        return results;
    }

    private RowResult RunRow(RowKey key, RunPlan plan, IReadOnlyList<CompiledRotationEntry> rotation, CancellationToken ct)
    {
        Scenario scenario = plan.Scenarios.First(s => string.Equals(s.Id, key.Scenario, StringComparison.Ordinal));
        ItemTemplate[] worn = scenarios.GearFor(key.Gear, key.Class).Select(data.Item).ToArray();

        var fights = new List<FightResult>(plan.Runs);
        for (int run = 0; run < plan.Runs; run++)
        {
            ct.ThrowIfCancellationRequested();
            var random = new Random(RunSeed.For(plan.Seed, key.Class, key.Level, key.Gear, key.Scenario, run));
            fights.Add(FightFactory.Create(data, scenario, key, worn, rotation, random).Run());
        }

        return Aggregate(key, fights, Snapshot(key, worn));
    }

    /// <summary>
    /// Win rate and fight length over every run (a loss counts its length, a timeout 300 s); health left over wins only
    /// (null when none won); time to the first spender over runs that cast one (null when none did); starved share over
    /// every run, per run starved / length x 100; damage as per-run averages.
    /// </summary>
    private static RowResult Aggregate(RowKey key, List<FightResult> fights, PlayerSnapshot snapshot)
    {
        var wins = fights.Where(f => f.Won).ToList();
        double[] spenders = fights.Where(f => f.FirstSpenderSeconds is not null).Select(f => f.FirstSpenderSeconds!.Value).ToArray();

        return new RowResult(
            key,
            fights.Count,
            wins.Count * 100d / fights.Count,
            Distribution.Of(fights.Select(f => f.Seconds).ToArray()),
            wins.Count > 0 ? Distribution.Of(wins.Select(f => f.HealthLeftPct).ToArray()) : null,
            spenders.Length > 0 ? Distribution.Of(spenders) : null,
            Distribution.Of(fights.Select(f => f.Seconds > 0 ? f.StarvedSeconds * 100d / f.Seconds : 0d).ToArray()),
            Average(fights, f => f.DamageDealt),
            Average(fights, f => f.DamageTaken),
            snapshot);
    }

    private static IReadOnlyDictionary<string, double> Average(List<FightResult> fights,
        Func<FightResult, IReadOnlyDictionary<string, long>> pick) =>
        fights.SelectMany(pick)
            .GroupBy(kv => kv.Key, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(kv => (double)kv.Value) / fights.Count, StringComparer.Ordinal);

    /// <summary>The player's derived stats and each ability's per-hit amount (AbilityAmounts, as the client is told).</summary>
    private PlayerSnapshot Snapshot(RowKey key, IReadOnlyList<ItemTemplate> worn)
    {
        var player = SimPlayer.Create(data, key.Class, key.Level, worn);
        DerivedCharacterStats s = player.Stats;
        return new PlayerSnapshot(s.MaxHealth, s.MaxPower, s.AttackDamage, s.AbilityDamage, s.Armor, s.CritPct, s.DodgePct,
            s.BlockPct, player.HastePct,
            player.Abilities.Select(a =>
            {
                AbilityAmount amount = AbilityAmounts.For(player.Attack, a.Metadata);
                return new AbilityLine(a.Name, amount.Kind.ToString(), amount.Min, amount.Max);
            }).ToList());
    }
}
