using Avalon.Network.Packets.Combat;

namespace Avalon.Combat.UnitTests;

/// <summary>#1178: a hit's damage split by type, its total, the types present and the part that leads.</summary>
public class DamagePartsShould
{
    [Theory]
    [InlineData(60d, 0d, 40d, 0d, 0d, 100d, DamageTypeMask.Physical | DamageTypeMask.Cold, DamageType.Physical)]
    [InlineData(0d, 10d, 10d, 0d, 0d, 20d, DamageTypeMask.Fire | DamageTypeMask.Cold, DamageType.Fire)]
    [InlineData(0d, 0d, 0d, 0d, 0d, 0d, DamageTypeMask.None, DamageType.Lightning)]
    public void Report_the_total_the_mask_and_the_largest_part(double physical, double fire, double cold, double lightning,
        double blight, double total, DamageTypeMask mask, DamageType primary)
    {
        DamageParts p = default;
        p.Set(DamageType.Physical, physical); p.Set(DamageType.Fire, fire); p.Set(DamageType.Cold, cold);
        p.Set(DamageType.Lightning, lightning); p.Set(DamageType.Blight, blight);
        Assert.Equal(total, p.Total);
        Assert.Equal(mask, p.Mask);
        Assert.Equal(primary, p.Primary(fallback: DamageType.Lightning));
    }
}
