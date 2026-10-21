namespace Avalon.World.Public.Abilities;

[System.Flags]
public enum AbilityFlags : uint
{
    None = 0,
    RequiresOutOfCombat = 1u << 0,
    RequiresInCombat = 1u << 1,

    /// <summary>A class's basic attack: the one ability a silence does not stop.</summary>
    Basic = 1u << 2,
}
