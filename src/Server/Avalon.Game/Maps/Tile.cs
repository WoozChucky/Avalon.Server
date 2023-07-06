using System.Drawing;
using System.Numerics;

namespace Avalon.Game.Maps
{
    public class Tile
    {
        public Vector2 Position { get; private set; }
        public Vector2 Origin { get; private set; }
        public Rectangle Bounds { get; private set; }
        public Size Size { get; private set; }
        
        public bool IsCollidable { get; private set; }
        
        public Tile(int x, int y, int size, bool collidable = false)
        {
            Position = new Vector2(x, y);
            Size = new Size(size, size);
            Bounds = new Rectangle((int)(Position.X * Size.Width), (int)Position.Y * Size.Height, Size.Width, Size.Height);
            Origin = new Vector2(Bounds.X + (Bounds.Width / 2), Bounds.Y + (Bounds.Height / 2));
            IsCollidable = collidable;
        }
    }
}
