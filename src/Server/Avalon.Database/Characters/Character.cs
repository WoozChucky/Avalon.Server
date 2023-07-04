using System.Numerics;

namespace Avalon.Database.Characters;

public class Character
{
    public int Id { get; set; }
    public int Account { get; set; }
    public string Name { get; set; }
    
    public float PositionX { get; set; }
    public float PositionY { get; set; }

    public bool IsChatting { get; set; }
    public float ElapsedGameTime { get; set; }
    public CharacterMovement Movement { get; set; }
}

public class CharacterMovement
{
    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; set; }
}
