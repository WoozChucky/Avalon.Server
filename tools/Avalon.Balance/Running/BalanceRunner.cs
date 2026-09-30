using Avalon.Balance.Config;
using Avalon.Balance.Data;
using Avalon.Balance.Simulation;
using Avalon.Combat;
using Avalon.Domain.World;
using Avalon.World.Public.Enums;

namespace Avalon.Balance.Running;

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

public sealed class BalanceRunner(BalanceData data, ScenarioFile scenarios, RotationFile rotations)
{
    /// <exception cref="ArgumentException">A filter names a class or scenario the files do not have.</exception>
    public RunPlan Plan(CharacterClass? onlyClass, string? onlyScenario, int? runs, int? seed)
    {
        if (onlyClass is { } c && !scenarios.Classes.Contains(c))
            throw new ArgumentException($"Class '{c}' is not in scenarios.json classes");
        if (runs is < 1)
            throw new ArgumentException($"--runs must be 1 or more, not {runs}");

        return new RunPlan(
            onlyClass is { } only ? [only] : scenarios.Classes,
            scenarios.LevelRange(),
            scenarios.Gear,
            onlyScenario is { } id ? [scenarios.Find(id)] : scenarios.Scenarios,
            runs ?? scenarios.Runs,
            seed ?? scenarios.Seed);
    }

    public IReadOnlyList<RowResult> Run(RunPlan plan)
    {
        Dictionary<CharacterClass, IReadOnlyList<CompiledRotationEntry>> compiled =
            plan.Classes.ToDictionary(c => c, c => rotations.Compile(c, data));
        RowKey[] keys = plan.Keys().ToArray();
        var results = new RowResult[keys.Length];
        Parallel.For(0, keys.Length, i => results[i] = RunRow(keys[i], plan, compiled[keys[i].Class]));
        return results;
    }

    private RowResult RunRow(RowKey key, RunPlan plan, IReadOnlyList<CompiledRotationEntry> rotation)
    {
        Scenario scenario = plan.Scenarios.First(s => string.Equals(s.Id, key.Scenario, StringComparison.Ordinal));
        ItemTemplate[] worn = scenarios.GearFor(key.Gear, key.Class).Select(data.Item).ToArray();

        var fights = new List<FightResult>(plan.Runs);
        for (int run = 0; run < plan.Runs; run++)
        {
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
        List<FightResult> wins = fights.Where(f => f.Won).ToList();
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
        SimPlayer player = SimPlayer.Create(data, key.Class, key.Level, worn);
        var s = player.Stats;
        return new PlayerSnapshot(s.MaxHealth, s.MaxPower, s.AttackDamage, s.AbilityDamage, s.Armor, s.CritPct, s.DodgePct,
            s.BlockPct, player.HastePct,
            player.Abilities.Select(a =>
            {
                AbilityAmount amount = AbilityAmounts.For(player.Attack, a.Metadata);
                return new AbilityLine(a.Name, amount.Kind.ToString(), amount.Min, amount.Max);
            }).ToList());
    }
}
