using Avalon.Common;
using Avalon.World.Public.Enums;

namespace Avalon.World.Configuration;

/// <summary>
/// Game:Items (#1179): the odds of a made item's rarity, what a sale pays for each rarity and while unidentified, what
/// identifying an item costs, the share of rares that drop sealed and what fills their bars (#1180), what an imprint costs
/// and the instability it adds (#1180 part C), tunable without a migration like the other loot knobs. Read at startup; a
/// value out of range stops the start (<see cref="ItemConfigurationValidation" />).
/// </summary>
public sealed class ItemConfiguration
{
    /// <summary>Game:Items:Rarity: the odds of each rarity by the rarity of the creature that drops the item.</summary>
    public CreatureRarityOdds Rarity { get; set; } = new();

    /// <summary>Game:Items:SellFactor: what a sale pays per rarity, as a multiple of the template's sell price.</summary>
    public SellFactors SellFactor { get; set; } = new();

    /// <summary>
    /// Game:Items:UnidentifiedSellPercent: what the sale of an unidentified item pays, in percent (0-100) of what it would
    /// pay identified, rounded down.
    /// </summary>
    public int UnidentifiedSellPercent { get; set; } = 50;

    /// <summary>
    /// Game:Items:IdentifyCost: the gold identifying one item at the Resonance Workbench costs, at most Game:MaxGold. 0, the
    /// default, spends nothing and needs no gold.
    /// </summary>
    public ulong IdentifyCost { get; set; }

    /// <summary>
    /// Game:Items:SealedRarePct (#1180): the share, in percent (0-100), of rolled rares whose best prefix or suffix is
    /// sealed, adding nothing until it wakes. 0 seals none and 100 every rare, neither with a draw; any share between
    /// draws once per rare, after its name.
    /// </summary>
    public double SealedRarePct { get; set; } = 5;

    /// <summary>Game:Items:Awakening (#1180): what fills a worn sealed affix's bar, and the chance a wake rises a tier.</summary>
    public AwakeningConfiguration Awakening { get; set; } = new();

    /// <summary>
    /// Game:Items:ImprintCost:{tier} (#1180 part C): the gold imprinting a glyph costs, by its modifier's tier rank (T9 the
    /// weakest), each at most Game:MaxGold. The binder merges configured tiers into these, so configuration can change a
    /// seeded tier's cost, or add a tier, but never remove one; a key outside 1-9 names no tier and stops the start.
    /// </summary>
    public Dictionary<byte, ulong> ImprintCost { get; set; } = new() { [9] = 25, [8] = 75, [7] = 200 };

    /// <summary>Game:Items:Imprint (#1180 part C): the bounds of the instability one imprint adds.</summary>
    public ImprintConfiguration Imprint { get; set; } = new();

    /// <summary>
    /// Null when every setting is in range, else a message naming the first one that is not. <paramref name="maxGold" /> is
    /// Game:MaxGold, which bounds the identify cost and each imprint cost.
    /// </summary>
    public string? Problem(ulong maxGold)
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

        if (UnidentifiedSellPercent is < 0 or > 100)
            return $"Game:Items:UnidentifiedSellPercent must be 0-100 (was {UnidentifiedSellPercent})";

        if (IdentifyCost > maxGold)
            return $"Game:Items:IdentifyCost must be at most Game:MaxGold, {maxGold} (was {IdentifyCost})";

        if (!double.IsFinite(SealedRarePct) || SealedRarePct is < 0 or > 100)
            return $"Game:Items:SealedRarePct must be 0-100 (was {SealedRarePct})";

        if (Awakening is null)
            return "Game:Items:Awakening must be set";

        if (Awakening.Problem() is { } awakening)
            return awakening;

        if (ImprintCost is null)
            return "Game:Items:ImprintCost must be set";

        foreach ((byte tier, ulong cost) in ImprintCost)
        {
            if (tier is < 1 or > 9)
                return $"Game:Items:ImprintCost:{tier} names no tier 1-9";

            if (cost > maxGold)
                return $"Game:Items:ImprintCost:{tier} must be at most Game:MaxGold, {maxGold} (was {cost})";
        }

        if (Imprint is null)
            return "Game:Items:Imprint must be set";

        return Imprint.Problem();
    }
}

/// <summary>The rarity odds of an item a creature of each rarity drops; a chest or a barrel uses Normal's.</summary>
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

/// <summary>What a sale pays per rarity, as a multiple of the sell price; a Common item pays it once.</summary>
public sealed class SellFactors
{
    public int Magic { get; set; } = 2;

    public int Rare { get; set; } = 4;

    public int Unique { get; set; } = 6;

    /// <summary>The factor of a copy of <paramref name="rarity" />: 1 for Common, and for a value that names no rarity.</summary>
    public uint For(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Magic => (uint)Magic,
        ItemRarity.Rare => (uint)Rare,
        ItemRarity.Unique => (uint)Unique,
        _ => 1,
    };
}
