namespace Avalon.World.Configuration;

/// <summary>
/// Game:Talents (#1181 Part C): what refunding talent nodes at the Lectern of Paths costs
/// (<see cref="Avalon.World.Talents.TalentPrice" />). Read at startup; each price setting must be at most Game:MaxGold,
/// or the start stops (<see cref="TalentConfigurationValidation" />). The per-node price a high level works out to is
/// not bounded by the cap: one no wallet holds is refused NotEnoughGold.
/// </summary>
public sealed class TalentConfiguration
{
    /// <summary>Game:Talents:FreeRefundMaxLevel: the highest level at which a refund is free.</summary>
    public ushort FreeRefundMaxLevel { get; set; } = 10;

    /// <summary>
    /// Game:Talents:RefundBasePrice: the gold one node costs at the first level past
    /// <see cref="FreeRefundMaxLevel" />, at most Game:MaxGold.
    /// </summary>
    public ulong RefundBasePrice { get; set; } = 100;

    /// <summary>
    /// Game:Talents:RefundPricePerLevel: the gold one node costs more for each level after that, at most Game:MaxGold.
    /// </summary>
    public ulong RefundPricePerLevel { get; set; } = 25;

    /// <summary>The first setting out of range, named; null when every one is in range.</summary>
    public string? Problem(ulong maxGold)
    {
        if (RefundBasePrice > maxGold)
            return $"Game:Talents:RefundBasePrice must be at most Game:MaxGold, {maxGold} (was {RefundBasePrice})";

        if (RefundPricePerLevel > maxGold)
            return $"Game:Talents:RefundPricePerLevel must be at most Game:MaxGold, {maxGold} (was {RefundPricePerLevel})";

        return null;
    }
}
