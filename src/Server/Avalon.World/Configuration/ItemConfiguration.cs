using Avalon.World.Public.Enums;

namespace Avalon.World.Configuration;

/// <summary>
/// Game:Items (#1179): the odds of a made item's rarity and what a sale pays for each rarity, tunable without a
/// migration like the other loot knobs. Read at startup; a value out of range stops the start
/// (<see cref="ItemConfigurationValidation" />).
/// </summary>
public sealed class ItemConfiguration
{
    /// <summary>Game:Items:Rarity: the odds of each rarity by the rarity of the creature that drops the item.</summary>
    public CreatureRarityOdds Rarity { get; set; } = new();

    /// <summary>Game:Items:SellFactor: what a sale pays per rarity, as a multiple of the template's sell price.</summary>
    public SellFactors SellFactor { get; set; } = new();

    /// <summary>Null when every setting is in range, else a message naming the first one that is not.</summary>
    public string? Problem()
    {
        if (Rarity is null)
            return "Game:Items:Rarity must be set";

        foreach (CreatureRarity creature in Enum.GetValues<CreatureRarity>())
        {
            if (Rarity.For(creature) is not { } odds)
                return $"Game:Items:Rarity:{creature} must be set";

            if (!double.IsFinite(odds.MagicPct) || odds.MagicPct is < 0 or > 100
                || !double.IsFinite(odds.RarePct) || odds.RarePct is < 0 or > 100
                || odds.MagicPct + odds.RarePct > 100)
            {
                return $"Game:Items:Rarity:{creature} must hold a MagicPct and a RarePct of 0-100 that add up to at most 100 " +
                       $"(was {odds.MagicPct} and {odds.RarePct})";
            }
        }

        if (SellFactor is null)
            return "Game:Items:SellFactor must be set";

        if (SellFactor.Magic < 1 || SellFactor.Rare < 1 || SellFactor.Unique < 1)
        {
            return "Game:Items:SellFactor:Magic, Rare and Unique must each be at least 1 " +
                   $"(was {SellFactor.Magic}, {SellFactor.Rare} and {SellFactor.Unique})";
        }

        return null;
    }
}

/// <summary>The rarity odds of an item a creature of each rarity drops (B-R14); a chest or a barrel uses Normal's.</summary>
public sealed class CreatureRarityOdds
{
    public RarityOdds Normal { get; set; } = new() { MagicPct = 22, RarePct = 3 };

    public RarityOdds Elite { get; set; } = new() { MagicPct = 32, RarePct = 8 };

    public RarityOdds Rare { get; set; } = new() { MagicPct = 40, RarePct = 15 };

    public RarityOdds Boss { get; set; } = new() { MagicPct = 50, RarePct = 50 };

    /// <summary>The odds of <paramref name="creature" />; Normal's for a value that names no rarity.</summary>
    public RarityOdds For(CreatureRarity creature) => creature switch
    {
        CreatureRarity.Elite => Elite,
        CreatureRarity.Rare => Rare,
        CreatureRarity.Boss => Boss,
        _ => Normal,
    };
}

/// <summary>One creature rarity's odds, in percent: Magic, Rare, and Common for the rest.</summary>
public sealed class RarityOdds
{
    public double MagicPct { get; set; }

    public double RarePct { get; set; }
}

/// <summary>What a sale pays per rarity (B-R23), as a multiple of the sell price; a Common item pays it once.</summary>
public sealed class SellFactors
{
    public int Magic { get; set; } = 2;

    public int Rare { get; set; } = 4;

    public int Unique { get; set; } = 6;
}
