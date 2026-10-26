using Avalon.World.Public.Enums;

namespace Avalon.World.Configuration;

/// <summary>
/// Game:Items:Awakening (#1180): what fills a worn sealed affix's bar, and the chance its wake rises a tier. A kill of a
/// creature gives its rarity's weight in points; damage dealt to one gives the weight times the share of its maximum
/// health removed. A creature more than <see cref="LevelGap" /> levels below the character gives nothing. Checked with the
/// rest of Game:Items (<see cref="ItemConfiguration.Problem" />), so a value out of range stops the start.
/// </summary>
public sealed class AwakeningConfiguration
{
    /// <summary>The highest weight a rarity may have: it keeps a hit's points, worked out in 64 bits, from overflowing.</summary>
    public const int MaxWeight = 10_000;

    /// <summary>Game:Items:Awakening:Weights: the points a kill of each creature rarity gives, 0-10000 each.</summary>
    public CreatureWeights Weights { get; set; } = new();

    /// <summary>
    /// Game:Items:Awakening:LevelGap: how many levels below the character a creature may be and still give points; at
    /// least 0.
    /// </summary>
    public int LevelGap { get; set; } = 5;

    /// <summary>Game:Items:Awakening:TierUpChancePct: the chance, in percent (0-100), that a wake rises one tier.</summary>
    public double TierUpChancePct { get; set; } = 20;

    /// <summary>Null when every setting is in range, else a message naming the first one that is not.</summary>
    internal string? Problem()
    {
        if (Weights is null)
            return "Game:Items:Awakening:Weights must be set";

        foreach (CreatureRarity rarity in Enum.GetValues<CreatureRarity>())
        {
            int weight = Weights.For(rarity);
            if (weight is < 0 or > MaxWeight)
                return $"Game:Items:Awakening:Weights:{rarity} must be 0-{MaxWeight} (was {weight})";
        }

        if (LevelGap < 0)
            return $"Game:Items:Awakening:LevelGap must be at least 0 (was {LevelGap})";

        if (!double.IsFinite(TierUpChancePct) || TierUpChancePct is < 0 or > 100)
            return $"Game:Items:Awakening:TierUpChancePct must be 0-100 (was {TierUpChancePct})";

        return null;
    }
}

/// <summary>The points a kill of a creature of each rarity gives a worn sealed affix's bar (#1180).</summary>
public sealed class CreatureWeights
{
    public int Normal { get; set; } = 1;

    public int Elite { get; set; } = 3;

    public int Rare { get; set; } = 6;

    public int Boss { get; set; } = 20;

    /// <summary>The weight of <paramref name="rarity" />; Normal's for a value that names no rarity.</summary>
    public int For(CreatureRarity rarity) => rarity switch
    {
        CreatureRarity.Elite => Elite,
        CreatureRarity.Rare => Rare,
        CreatureRarity.Boss => Boss,
        _ => Normal,
    };
}
