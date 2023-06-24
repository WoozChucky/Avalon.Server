using System.Drawing;
using System.Numerics;
using Avalon.Common.Extensions;

namespace Avalon.Map
{
    public class Tile
    {
        public Vector2 Position { get; private set; }
        public Rectangle Bounds { get; private set; }
        public Size Size { get; private set; }
        
        public Tile(int x, int y, int size)
        {
            Position = new Vector2(x, y);
            Size = new Size(size, size);
            Bounds = new Rectangle(Position.ToPoint(), Size);
        }
    }
}