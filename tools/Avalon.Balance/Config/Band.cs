using System.Globalization;

namespace Avalon.Balance.Config;

public enum Grade
{
    Green,
    Yellow,
    Red,
}

/// <summary>A target band. Either end may be open. Green inside, yellow within the tolerance of an edge, red beyond.</summary>
public sealed class Band
{
    public double? Min { get; set; }

    public double? Max { get; set; }

    /// <summary>How far outside the band <paramref name="value" /> is; 0 inside.</summary>
    public double Distance(double value) =>
        Min is { } lo && value < lo ? lo - value
        : Max is { } hi && value > hi ? value - hi
        : 0d;

    /// <summary>
    /// Green inside the band; yellow when outside by at most <paramref name="tolerancePct" /> percent of the edge it
    /// crossed (at least of 1, so an edge of 0 still has a yellow zone); red further out, or when there is no value.
    /// </summary>
    public Grade Grade(double? value, double tolerancePct)
    {
        if (value is not { } v || double.IsNaN(v))
            return Config.Grade.Red;

        double distance = Distance(v);
        if (distance == 0d)
            return Config.Grade.Green;

        double edge = Min is { } lo && v < lo ? lo : Max!.Value;
        return distance <= tolerancePct / 100d * Math.Max(Math.Abs(edge), 1d) ? Config.Grade.Yellow : Config.Grade.Red;
    }

    public string Describe(string unit) => (Min, Max) switch
    {
        ({ } lo, { } hi) when lo == hi => $"{F(lo)}{unit}",
        ({ } lo, { } hi) => $"{F(lo)}-{F(hi)}{unit}",
        ({ } lo, null) => $">= {F(lo)}{unit}",
        (null, { } hi) => $"<= {F(hi)}{unit}",
        _ => "any",
    };

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
