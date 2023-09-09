using System.Drawing;
using System.Numerics;
using Avalon.Common.Extensions;

namespace Avalon.Game.Npc;

public enum UrielState
{
    Idle,
    Walking,
    Collided
}

public class Uriel : IEntity
{
    public int AccountId { get; }
    public string Name { get; set; }

    public Vector2 Position { get; set; } = new Vector2(80, 1152.4f);
    public Vector2 PreviousPosition { get; set; } = new Vector2(0, 0);
    public Vector2 Velocity { get; set; } = new Vector2(1, 0);
    public Vector2 PreviousVelocity { get; set; } = new Vector2(0, 0);
    public Rectangle Bounds { get; set; }
    public float Speed { get; set; } = 35f;

    public UrielState State { get; set; } = UrielState.Idle;

    public Uriel()
    {
        AccountId = new Random().Next(1, 9999);
        Name = $"Uriel#{AccountId}";
        Bounds = new Rectangle((int)Position.X, (int)Position.Y, 32, 32);
        State = UrielState.Walking;
    }
    
    public void Update(TimeSpan deltaTime)
    {
        if (State == UrielState.Collided)
        {
            return;
        }

        if (Velocity == Vector2.Zero)
        {
            Velocity = PreviousVelocity;
        }
        
        Position += Velocity * Speed * (float)deltaTime.TotalSeconds;
        PreviousPosition = Position;
        Bounds = new Rectangle(Position.ToPoint(), new Size(32, 32));
    }

    public void InvertDirection()
    {
        Velocity = -Velocity;
    }
    
    public void RandomizeDirection()
    {
        var random = new Random();
        var x = random.Next(-1, 2);
        var y = random.Next(-1, 2);
        Velocity = new Vector2(x, y);
    }
}
