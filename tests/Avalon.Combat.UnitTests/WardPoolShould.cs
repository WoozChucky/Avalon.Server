namespace Avalon.Combat.UnitTests;

/// <summary>The ward pool (#1178): what a hit takes from it, when it recharges, and what a new maximum leaves.</summary>
public class WardPoolShould
{
    private static readonly DateTime s_t0 = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);

    private static readonly RegenConfiguration s_config = new() { WardRechargeDelaySeconds = 4f, WardRechargeSeconds = 2f };

    [Theory]
    [InlineData(100u, 30u, 30u, 70u)]
    [InlineData(100u, 150u, 100u, 0u)]
    [InlineData(0u, 10u, 0u, 0u)]
    public void Absorb_up_to_what_it_holds(uint max, uint hit, uint absorbed, uint left)
    {
        WardPool ward = default;
        ward.Reset(max);

        Assert.Equal(absorbed, ward.Absorb(hit, s_t0));
        Assert.Equal(left, ward.Current);
    }

    /// <summary>A slow recharge (Unbroken, #1181 Part D) waits twice the delay: 8 s, not 4.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recharge_only_after_the_delay_and_restart_the_delay_on_any_damage(bool slowRecharge)
    {
        double delay = slowRecharge ? 8d : 4d;
        WardPool ward = default;
        ward.Reset(100);
        ward.Absorb(30, s_t0);
        Assert.Equal(70u, ward.Current);

        Assert.False(ward.Advance(s_t0.AddSeconds(delay - 0.1), s_config, slowRecharge));
        Assert.Equal(70u, ward.Current);
        if (slowRecharge)
        {
            Assert.False(ward.Advance(s_t0.AddSeconds(4.5), s_config, slowRecharge));
        }

        Assert.True(ward.Advance(s_t0.AddSeconds(delay + 0.5), s_config, slowRecharge));
        Assert.Equal(95u, ward.Current);

        // Damage the ward never saw (blight, a bleed) still restarts the delay.
        ward.Damaged(s_t0.AddSeconds(delay + 0.5));
        Assert.False(ward.Advance(s_t0.AddSeconds((2 * delay) - 0.1), s_config, slowRecharge));
        Assert.Equal(95u, ward.Current);

        Assert.True(ward.Advance(s_t0.AddSeconds((2 * delay) + 1.5), s_config, slowRecharge));
        Assert.Equal(100u, ward.Current);
    }

    [Theory]
    [InlineData(50u, 100u, 200u, 100u)]
    [InlineData(100u, 100u, 40u, 40u)]
    [InlineData(0u, 100u, 80u, 0u)]
    [InlineData(0u, 0u, 80u, 0u)]   // from none, a stats change gives a ward that starts empty and recharges
    public void Keep_its_share_when_its_maximum_changes(uint current, uint max, uint newMax, uint expected)
    {
        WardPool ward = default;
        ward.Reset(max);
        ward.Absorb(max - current, s_t0);

        ward.SetMax(newMax, s_t0);

        Assert.Equal((newMax, expected), (ward.Max, ward.Current));
    }
}
