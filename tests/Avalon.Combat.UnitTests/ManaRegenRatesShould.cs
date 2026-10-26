namespace Avalon.Combat.UnitTests;

/// <summary>
/// #1181: mana regenerates a share of its maximum a second, 1 % in combat and 4 % out of it by default, scaled by the
/// increased mana regeneration worn. The fraction of a point is carried between steps (PowerRegen.TakeWholePoints), so
/// the per-second rate holds at any tick rate.
/// </summary>
public class ManaRegenRatesShould
{
    private const double Tick = 1d / 60d;
    private const double Rate = 6.9;   // mana a second

    [Theory]
    [InlineData(192u, 0f, 1.92, 7.68)]
    [InlineData(416u, 0f, 4.16, 16.64)]
    [InlineData(192u, 50f, 2.88, 11.52)]
    [InlineData(192u, -150f, 0d, 0d)]   // at -100 % or below, nothing
    [InlineData(0u, 0f, 0d, 0d)]        // no maximum, nothing
    public void Regenerate_a_share_of_maximum_mana_a_second(uint maxMana, float increasedPct, double inCombat,
        double outOfCombat)
    {
        var rates = ManaRegenRates.Of(new RegenConfiguration(), maxMana, increasedPct);

        Assert.Equal(inCombat, rates.InCombat, precision: 6);
        Assert.Equal(outOfCombat, rates.OutOfCombat, precision: 6);
    }

    [Fact]
    public void Give_the_same_points_in_sixty_ticks_as_in_one_second()
    {
        double carry = 0d;
        uint total = 0;
        for (int tick = 0; tick < 60; tick++)
            total += PowerRegen.TakeWholePoints(Rate * Tick, ref carry);

        Assert.Equal(6u, total);
        Assert.Equal(0.9, carry, precision: 3);
    }

    [Fact]
    public void Pay_a_carried_fraction_on_a_later_step()
    {
        double carry = 0.9;

        // 0.9 + 6.9 / 60 = 1.015
        Assert.Equal(1u, PowerRegen.TakeWholePoints(Rate * Tick, ref carry));
        Assert.Equal(0.015, carry, precision: 3);
    }
}
