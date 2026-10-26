using Avalon.World.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Avalon.Server.World.UnitTests.Loot;

/// <summary>
/// Game:Items (#1179) out of range stops the start: rarity odds that cannot be drawn, a sale below the sell price, an
/// unidentified sale outside 0-100 percent, an identify cost above the gold cap, a sealed-rare share outside 0-100
/// percent (#1180), and Awakening settings out of range: a creature weight outside 0-10000, a negative level gap, a
/// tier-up chance outside 0-100 percent (#1180), and imprint settings out of range: a cost for no tier 1-9 or above the
/// gold cap, instability bounds outside 0 &lt;= min &lt;= max &lt;= 100 (#1180 part C).
/// </summary>
public class ItemConfigurationValidationShould
{
    [Theory]
    [InlineData(22.0, 3.0, 2, true)]
    [InlineData(double.NaN, 3.0, 2, false)]
    [InlineData(101.0, 0.0, 2, false)]
    [InlineData(60.0, 50.0, 2, false)]
    [InlineData(22.0, 3.0, 0, false)]
    [InlineData(22.0, 3.0, 2, false, -1)]
    [InlineData(22.0, 3.0, 2, false, 101)]
    [InlineData(22.0, 3.0, 2, true, 0)]
    [InlineData(22.0, 3.0, 2, true, 50, 9_999_999_999UL)]
    [InlineData(22.0, 3.0, 2, false, 50, 10_000_000_000UL)]
    [InlineData(22.0, 3.0, 2, false, 50, 0UL, -0.1)]
    [InlineData(22.0, 3.0, 2, false, 50, 0UL, 100.1)]
    [InlineData(22.0, 3.0, 2, false, 50, 0UL, double.NaN)]
    [InlineData(22.0, 3.0, 2, true, 50, 0UL, 0.0)]
    [InlineData(22.0, 3.0, 2, true, 50, 0UL, 100.0)]
    [InlineData(22.0, 3.0, 2, false, 50, 0UL, 5.0, "Weights:Boss 10001")]
    [InlineData(22.0, 3.0, 2, false, 50, 0UL, 5.0, "Weights:Normal -1")]
    [InlineData(22.0, 3.0, 2, false, 50, 0UL, 5.0, "LevelGap -1")]
    [InlineData(22.0, 3.0, 2, false, 50, 0UL, 5.0, "TierUpChancePct 100.5")]
    [InlineData(22.0, 3.0, 2, false, 50, 0UL, 5.0, "TierUpChancePct NaN")]
    [InlineData(22.0, 3.0, 2, true, 50, 0UL, 5.0, "Weights:Boss 10000")]
    [InlineData(22.0, 3.0, 2, false, 50, 0UL, 5.0, null, "Items:ImprintCost:0=5")]
    [InlineData(22.0, 3.0, 2, false, 50, 0UL, 5.0, null, "Items:ImprintCost:10=5")]
    [InlineData(22.0, 3.0, 2, false, 50, 0UL, 5.0, null, "Items:ImprintCost:7=10000000000")]
    [InlineData(22.0, 3.0, 2, false, 50, 0UL, 5.0, null, "Items:Imprint:InstabilityMinPct=-1")]
    [InlineData(22.0, 3.0, 2, false, 50, 0UL, 5.0, null, "Items:Imprint:InstabilityMaxPct=101")]
    [InlineData(22.0, 3.0, 2, false, 50, 0UL, 5.0, null, "Items:Imprint:InstabilityMinPct=50; Items:Imprint:InstabilityMaxPct=40")]
    [InlineData(22.0, 3.0, 2, true, 50, 0UL, 5.0, null, "Items:Imprint:InstabilityMinPct=0; Items:Imprint:InstabilityMaxPct=0")]
    [InlineData(22.0, 3.0, 2, true, 50, 0UL, 5.0, null, "Items:Imprint:InstabilityMinPct=100; Items:Imprint:InstabilityMaxPct=100")]
    [InlineData(22.0, 3.0, 2, true, 50, 0UL, 5.0, null, "Items:ImprintCost:7=300")]
    public void Refuse_game_items_settings_out_of_range(
        double magicPct, double rarePct, int magicSellFactor, bool accepted, int unidentifiedSellPercent = 50,
        ulong identifyCost = 0, double sealedRarePct = 5, string? awakening = null, string? imprint = null)
    {
        var items = new ItemConfiguration
        {
            UnidentifiedSellPercent = unidentifiedSellPercent,
            IdentifyCost = identifyCost,
            SealedRarePct = sealedRarePct,
        };
        items.Rarity.Elite = new RarityOdds { MagicPct = magicPct, RarePct = rarePct };
        items.SellFactor.Magic = magicSellFactor;
        switch (awakening)
        {
            case null:
                break;
            case "Weights:Boss 10001":
                items.Awakening.Weights.Boss = 10_001;
                break;
            case "Weights:Normal -1":
                items.Awakening.Weights.Normal = -1;
                break;
            case "LevelGap -1":
                items.Awakening.LevelGap = -1;
                break;
            case "TierUpChancePct 100.5":
                items.Awakening.TierUpChancePct = 100.5;
                break;
            case "TierUpChancePct NaN":
                items.Awakening.TierUpChancePct = double.NaN;
                break;
            case "Weights:Boss 10000":
                items.Awakening.Weights.Boss = 10_000;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(awakening), awakening, null);
        }

        // An imprint row goes through the configuration binder, as Game:Items does at startup, so a cost keyed by a tier
        // the binder reads as a byte, and the binder merging configured costs into the seeded ones, are what is checked.
        var game = new GameConfiguration { Items = items };
        if (imprint is not null)
            new ConfigurationBuilder().AddInMemoryCollection(Settings(imprint)).Build().Bind(game);

        ValidateOptionsResult result = new ItemConfigurationValidation().Validate(null, game);

        Assert.Equal(accepted, result.Succeeded);
        if (imprint == "Items:ImprintCost:7=300")
            Assert.Equal(new Dictionary<byte, ulong> { [9] = 25, [8] = 75, [7] = 300 }, game.Items.ImprintCost);
    }

    /// <summary>The row's settings, each a full key and its value ("key=value"), separated by "; ".</summary>
    private static IEnumerable<KeyValuePair<string, string?>> Settings(string imprint) =>
        imprint.Split("; ").Select(pair => pair.Split('=')).Select(kv => new KeyValuePair<string, string?>(kv[0], kv[1]));
}
