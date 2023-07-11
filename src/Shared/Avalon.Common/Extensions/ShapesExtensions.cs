using System.Drawing;
using System.Numerics;

namespace Avalon.Common.Extensions;

public static class ShapesExtensions
{
    public static Point ToPoint(this Vector2 vector)
    {
        return new Point((int)vector.X, (int)vector.Y);
    }
    
    public static Vector2 Normalized(this Vector2 vector)
    {
        float num = 1f / MathF.Sqrt((float) ((double) vector.X * (double) vector.X + (double) vector.Y * (double) vector.Y));
        vector.X *= num;
        vector.Y *= num;
        return vector;
    }
}
