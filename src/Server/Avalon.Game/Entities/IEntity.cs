using System.Drawing;
using System.Numerics;
using Avalon.Common;

namespace Avalon.Game.Entities;

public interface IEntity : IHideObjectMembers
{
    Guid Id { get; }
    
    public Vector2 Position { get; }
    public Vector2 Velocity { get; }
    public Rectangle Bounds { get; set; }
    
    void Update(TimeSpan deltaTime);
    void InvertDirection();
}
