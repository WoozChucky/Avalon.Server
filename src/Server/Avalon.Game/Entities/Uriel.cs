using System.Drawing;
using System.Numerics;
using Avalon.Common.Extensions;

namespace Avalon.Game.Entities;

public enum UrielState
{
    Idle,
    Walking,
    Collided
}

public class Uriel : IEntity
{
    public Guid Id { get; }

    public Vector2 Position { get; set; } = new Vector2(80, 1152.4f);
    public Vector2 Velocity { get; set; } = new Vector2(1, 0);
    public Vector2 PreviousVelocity { get; set; } = new Vector2(0, 0);
    public Rectangle Bounds { get; set; }
    public float Speed { get; set; } = 20f;

    public UrielState State { get; set; } = UrielState.Idle;

    public Uriel()
    {
        Bounds = new Rectangle((int)Position.X, (int)Position.Y, 32, 32);
        State = UrielState.Walking;
    }
    
    public void Update(TimeSpan deltaTime)
    {
        if (State == UrielState.Collided)
        {
            return;
        }
        
        Position += Velocity * Speed * (float)deltaTime.TotalSeconds;
        Bounds = new Rectangle(Position.ToPoint(), new Size(32, 32));
    }

    public void InvertDirection()
    {
        Velocity = -Velocity;
    }
}
