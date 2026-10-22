namespace Avalon.World.Public.Abilities;

[System.Flags]
public enum AbilityFlags : uint
{
    None = 0,
    RequiresOutOfCombat = 1u << 0,
    RequiresInCombat = 1u << 1,

    /// <summary>A class's basic attack: the one ability a silence does not stop.</summary>
    Basic = 1u << 2,

    /// <summary>
    /// A Hostile ability that always lands (#1164): it skips the landing check every other Hostile ability makes on each
    /// unit its shape affects (the target's dodge chance, and the targets every hit passes over), so its aura lands on
    /// every one of them and its damage, when it has any, is never dodged. Its shape still decides who it affects, a
    /// telegraph's players judged by their own screens as any other's. An Ally ability is never dodged anyway, and the
    /// catalog refuses the flag on one.
    /// </summary>
    Unavoidable = 1u << 3,
}
