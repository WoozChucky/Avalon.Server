using System.Drawing;
using System.Numerics;

namespace Avalon.Game.Maps.Virtual;

public class BaseMapElement : IMapElement
{
    public Vector2 Position { get; }
    public Vector2 Origin { get; }
    public Rectangle Bounds { get; }
    public Size Size { get; }
    
    protected BaseMapElement(int x, int y, int width, int height)
    {
        Position = new Vector2(x, y);
        Size = new Size(width, height);
        Bounds = new Rectangle((int)(Position.X * Size.Width), (int)Position.Y * Size.Height, Size.Width, Size.Height);
        Origin = new Vector2(Size.Width / 2f, Size.Height / 2f);
    }
}
