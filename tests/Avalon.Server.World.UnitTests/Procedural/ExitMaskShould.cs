using Avalon.World.ChunkLayouts;
using Xunit;

namespace Avalon.Server.World.UnitTests.Procedural;

public class ExitMaskShould
{
    [Fact]
    public void Rotate_90_maps_N_to_E()
    {
        // 12-bit layout: side order N,E,S,W, each side has 3 bits (Left,Center,Right).
        // Side offset: N=0, E=3, S=6, W=9. Slot offset within side: Left=0, Center=1, Right=2.
        ushort mask = 0b_0000_0000_0000_0010; // N-Center (bit 1) set
        ushort rotated = ExitMask.Rotate(mask, rotation: 1); // 90° → side N maps to E
        Assert.Equal((ushort)0b_0000_0000_0001_0000, rotated); // E-Center (bit 4) set
    }

    [Fact]
    public void Rotate_180_maps_N_to_S()
    {
        ushort mask = 0b_0000_0000_0000_0010; // N-Center
        ushort rotated = ExitMask.Rotate(mask, rotation: 2);
        Assert.Equal((ushort)0b_0000_0000_1000_0000, rotated); // S-Center (bit 7)
    }

    [Fact]
    public void Opposite_N_is_S() => Assert.Equal(ExitSide.S, ExitMask.Opposite(ExitSide.N));

    [Fact]
    public void Opposite_E_is_W() => Assert.Equal(ExitSide.W, ExitMask.Opposite(ExitSide.E));

    [Fact]
    public void Opposite_S_is_N() => Assert.Equal(ExitSide.N, ExitMask.Opposite(ExitSide.S));

    [Fact]
    public void Opposite_W_is_E() => Assert.Equal(ExitSide.E, ExitMask.Opposite(ExitSide.W));

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
        var (dx, dz) = ExitMask.GridDir(ExitSide.N);
        Assert.Equal(0, dx);
        Assert.Equal(1, dz);
    }
}
