using System.Drawing;
using System.Numerics;
using Avalon.Common;

namespace Avalon.Game.Entities;

public interface IEntity : IHideObjectMembers
{
    string Id { get; }
    
    public Vector2 Position { get; }
    public Vector2 Velocity { get; }
    public Rectangle Bounds { get; set; }
    
    void Update(TimeSpan deltaTime);
    void InvertDirection();
}

public class Character //TODO: Move this to separate file
{
    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; set; }

    public Rectangle Bounds { get; set; }
    public float ElapsedGameTime { get; set; }
    public bool IsChatting { get; set; }
}
