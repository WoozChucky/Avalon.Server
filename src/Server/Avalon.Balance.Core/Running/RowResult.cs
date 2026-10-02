using Avalon.World.Public.Enums;

namespace Avalon.Balance.Core;

public sealed record RowKey(CharacterClass Class, ushort Level, string Gear, string Scenario);

public sealed record Distribution(double P10, double Median, double P90)
{
    /// <summary>Nearest-rank percentiles of a non-empty list.</summary>
    public static Distribution Of(IReadOnlyCollection<double> values)
    {
        double[] sorted = values.Order().ToArray();
        double At(double q) => sorted[(int)Math.Round((sorted.Length - 1) * q, MidpointRounding.AwayFromZero)];
        return new Distribution(At(0.1), At(0.5), At(0.9));
    }
}

public sealed record AbilityLine(string Name, string Kind, uint Min, uint Max);

public sealed record PlayerSnapshot(uint Health, uint Power, uint AttackDamage, uint AbilityDamage, uint Armor,
    float CritPct, float DodgePct, float BlockPct, float HastePct, IReadOnlyList<AbilityLine> Abilities);

/// <summary>
/// One row's runs, aggregated: win rate over all runs; fight length over all runs (a loss counts its length, a
/// timeout 300 s); health left over wins only (null when none won); time to the first spender over runs that cast
/// one (null when none did); starved share over every run, per run starved / length x 100; damage per-run averages.
/// </summary>
public sealed record RowResult(
    RowKey Key,
    int Runs,
    double WinRatePct,
    Distribution FightSeconds,
    Distribution? HealthLeftPct,
    Distribution? FirstSpenderSeconds,
    Distribution StarvedPct,
    IReadOnlyDictionary<string, double> DamageDealtPerRun,
    IReadOnlyDictionary<string, double> DamageTakenPerRun,
    PlayerSnapshot Snapshot);
