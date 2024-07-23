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
    Level = 1 << 7,
    Power = 1 << 8,
    Health = 1 << 9,
    PowerType = 1 << 10,
    CreatureMetadataId = 1 << 11,
    Name = 1 << 12,
    
    WorldObjectUpdate = Position | Velocity | Orientation,
    CreatureUpdate = Position | CurrentHealth | CurrentPower | Velocity | Orientation | MoveState,
    CharacterUpdate = Position | CurrentHealth | CurrentPower | Velocity | Orientation | MoveState | Level | Health | Power,
    All = Position | CurrentHealth | CurrentPower | Velocity | Orientation | MoveState | Level | Power | Health | PowerType | CreatureMetadataId | Name,
    
}
