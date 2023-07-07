using System.Drawing;
using System.Numerics;

namespace Avalon.Game.Creatures;

public class Creature
{
    public Guid Id { get; set; }
    public int TemplateId { get; set; }

    public string Name { get; set; }
    
    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; set; }
    
    public Rectangle Bounds { get; set; }
    public float Speed { get; set; }
}
