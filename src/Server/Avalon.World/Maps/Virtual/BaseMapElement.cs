using System.Drawing;
using System.Numerics;

namespace Avalon.World.Maps.Virtual;

public class BaseMapElement : IMapElement
{
    public Vector2 Position { get; protected set; }
    public Vector2 Origin { get; protected set; }
    public Rectangle Bounds { get; protected set; }
    public Size Size { get; protected set; }
    
    protected BaseMapElement(int x, int y, int width, int height)
    {
        Position = new Vector2(x, y);
        Size = new Size(width, height);
        Bounds = new Rectangle((int)Position.X, (int)Position.Y, Size.Width, Size.Height);
        Origin = new Vector2(Size.Width / 2f, Size.Height / 2f);
    }
}
