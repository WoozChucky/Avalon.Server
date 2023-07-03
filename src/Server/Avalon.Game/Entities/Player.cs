using System.Drawing;
using System.Numerics;

namespace Avalon.Game.Entities;

public class Player : IEntity
{
    public string Id { get; set; }

    public Vector2 Position
    {
        get => this.Character.Position;
        set => Character.Position = value;
    }
    public Vector2 Velocity
    {
        get => this.Character.Velocity;
        set => Character.Velocity = value;
    }
    public Rectangle Bounds
    {
        get => new((int)Character.Position.X, (int)Character.Position.Y, 32, 32);
        set => Character.Bounds = value;
    }

    public AvalonConnection Connection { get; private set; }
    public Character Character { get; }

    public PartyGroup Party { get; set; }
    
    public Player(AvalonConnection connection, Character character)
    {
        Id = connection.Id;
        Connection = connection;
        Character = character;
        Party = new PartyGroup();
    }
    
    public void Update(TimeSpan deltaTime)
    {
        
    }

    public void InvertDirection()
    {
        Velocity = -Velocity;
    }
}

public class PartyGroup
{
    public bool Active { get; set; }
    public bool Leader { get; set; }
    public List<string> Members { get; set; } = new();
    
    public PartyGroup()
    {
        
    }
}
