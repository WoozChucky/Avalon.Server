namespace Avalon.World.Public.Enums;

[Flags]
public enum GameEntityFields
{
    None = 1 << 0,
    Position = 1 << 1,
    CurrentHealth = 1 << 2,
    CurrentPower = 1 << 3,
    Velocity = 1 << 4,
    Orientation = 1 << 5,
    MoveState = 1 << 6,
    
    All = Position | CurrentHealth | CurrentPower | Velocity | Orientation | MoveState
}
