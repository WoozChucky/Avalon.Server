using System.Drawing;
using System.Numerics;

namespace Avalon.Common.Extensions;

public static class ShapesExtensions
{
    public static Point ToPoint(this Vector2 vector)
    {
        return new Point((int)vector.X, (int)vector.Y);
    }
}