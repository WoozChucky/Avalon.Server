using Avalon.World.Configuration;
using Microsoft.Extensions.Options;

namespace Avalon.Server.World.UnitTests.Loot;

/// <summary>
/// Game:Items (#1179) out of range stops the start: rarity odds that cannot be drawn, a sale below the sell price, an
/// unidentified sale outside 0-100 percent, an identify cost above the gold cap.
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
    public void Refuse_game_items_settings_out_of_range(
        double magicPct, double rarePct, int magicSellFactor, bool accepted, int unidentifiedSellPercent = 50,
        ulong identifyCost = 0)
    {
        var items = new ItemConfiguration { UnidentifiedSellPercent = unidentifiedSellPercent, IdentifyCost = identifyCost };
        items.Rarity.Elite = new RarityOdds { MagicPct = magicPct, RarePct = rarePct };
        items.SellFactor.Magic = magicSellFactor;

        ValidateOptionsResult result = new ItemConfigurationValidation().Validate(null, new GameConfiguration { Items = items });

        Assert.Equal(accepted, result.Succeeded);
    }
}
