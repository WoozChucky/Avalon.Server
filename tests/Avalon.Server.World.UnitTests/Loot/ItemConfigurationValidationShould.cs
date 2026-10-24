using Avalon.World.Configuration;
using Microsoft.Extensions.Options;

namespace Avalon.Server.World.UnitTests.Loot;

/// <summary>Game:Items (#1179) out of range stops the start: rarity odds that cannot be drawn, a sale below the sell price.</summary>
public class ItemConfigurationValidationShould
{
    [Theory]
    [InlineData(22.0, 3.0, 2, true)]
    [InlineData(double.NaN, 3.0, 2, false)]
    [InlineData(101.0, 0.0, 2, false)]
    [InlineData(60.0, 50.0, 2, false)]
    [InlineData(22.0, 3.0, 0, false)]
    public void Refuse_odds_that_cannot_be_drawn_and_a_sell_factor_below_one(
        double magicPct, double rarePct, int magicSellFactor, bool accepted)
    {
        var items = new ItemConfiguration();
        items.Rarity.Elite = new RarityOdds { MagicPct = magicPct, RarePct = rarePct };
        items.SellFactor.Magic = magicSellFactor;

        ValidateOptionsResult result = new ItemConfigurationValidation().Validate(null, new GameConfiguration { Items = items });

        Assert.Equal(accepted, result.Succeeded);
    }
}
