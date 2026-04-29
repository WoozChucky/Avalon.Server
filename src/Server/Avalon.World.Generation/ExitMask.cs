using Avalon.Common.Mathematics;

namespace Avalon.World.ChunkLayouts;

public enum ExitSide : byte { N = 0, E = 1, S = 2, W = 3 }
public enum ExitSlot : byte { Left = 0, Center = 1, Right = 2 }

public static class ExitMask
{
    public static ExitSide Opposite(ExitSide side) => (ExitSide)(((int)side + 2) & 0b11);

    public static bool Has(ushort mask, ExitSide side, ExitSlot slot) =>
        (mask & (1 << ((int)side * 3 + (int)slot))) != 0;

    public static ushort Rotate(ushort mask, int rotation)
    {
        rotation &= 0b11;
        if (rotation == 0) return mask;
        ushort result = 0;
        for (int s = 0; s < 4; s++)
        {
            int ns = (s + rotation) & 0b11;
            int bits = (mask >> (s * 3)) & 0b111;
            result |= (ushort)(bits << (ns * 3));
        }
        return result;
    }

    public static Vector3 DirVec(ExitSide side) => side switch
    {
        ExitSide.N => new Vector3(0, 0, 1),
        ExitSide.E => new Vector3(1, 0, 0),
        ExitSide.S => new Vector3(0, 0, -1),
        ExitSide.W => new Vector3(-1, 0, 0),
        _ => default
    };

    public static (int dx, int dz) GridDir(ExitSide side) => side switch
    {
        ExitSide.N => (0, 1),
        ExitSide.E => (1, 0),
        ExitSide.S => (0, -1),
        ExitSide.W => (-1, 0),
        _ => (0, 0)
    };
}
