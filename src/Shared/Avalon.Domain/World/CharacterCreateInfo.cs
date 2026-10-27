using Avalon.Common;

namespace Avalon.Domain.World;

/// <summary>Where a new character of a class enters the world, and what it carries (<see cref="StartingItems" />).</summary>
public class CharacterCreateInfo
{
    public CharacterClass Class { get; set; }
    public ushort Map { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Rotation { get; set; }

    /// <summary>The items a new character of the class carries in its Bag, one row per slot.</summary>
    public List<CharacterCreateItem> StartingItems { get; set; } = [];
}
