using System.Drawing;
using System.Numerics;
using Avalon.Common;

namespace Avalon.Game.Npc;

public interface IEntity : IHideObjectMembers
{
    int AccountId { get; }
    
    public Vector2 Position { get; }
    public Vector2 Velocity { get; }
    public Rectangle Bounds { get; set; }
    
    void Update(TimeSpan deltaTime);
    void InvertDirection();
}
