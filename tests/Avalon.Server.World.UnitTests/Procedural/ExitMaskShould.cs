using Avalon.World.ChunkLayouts;

namespace Avalon.Server.World.UnitTests.Procedural;

public class ExitMaskShould
{
    // 12-bit layout: side order N,E,S,W, each side has 3 bits (Left,Center,Right).
    // Side offset: N=0, E=3, S=6, W=9. Slot offset within side: Left=0, Center=1, Right=2.
    [Theory]
    [InlineData(1, 0b_0000_0000_0001_0000)]   // 90°: N-Center (bit 1) to E-Center (bit 4)
    [InlineData(2, 0b_0000_0000_1000_0000)]   // 180°: N-Center to S-Center (bit 7)
    public void Rotate_the_north_centre_exit(byte rotation, int expected) =>
        Assert.Equal((ushort)expected, ExitMask.Rotate(0b_0000_0000_0000_0010, rotation));

    [Theory]
    [InlineData(ExitSide.N, ExitSide.S)]
    [InlineData(ExitSide.E, ExitSide.W)]
    [InlineData(ExitSide.S, ExitSide.N)]
    [InlineData(ExitSide.W, ExitSide.E)]
    public void Find_the_opposite_side(ExitSide side, ExitSide opposite) => Assert.Equal(opposite, ExitMask.Opposite(side));

    [Fact]
    public void Has_returns_true_for_set_slot()
    {
        ushort mask = 0b_0000_0000_0000_0010; // N-Center
        Assert.True(ExitMask.Has(mask, ExitSide.N, ExitSlot.Center));
        Assert.False(ExitMask.Has(mask, ExitSide.N, ExitSlot.Left));
    }

    [Fact]
    public void GridDir_N_is_pos_z()
    {
        (int dx, int dz) = ExitMask.GridDir(ExitSide.N);
        Assert.Equal(0, dx);
        Assert.Equal(1, dz);
    }
}
