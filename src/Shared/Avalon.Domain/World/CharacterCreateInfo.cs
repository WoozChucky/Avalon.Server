using Avalon.Common.ValueObjects;
using Avalon.World.Public.Enums;

namespace Avalon.Domain.World;

public class CharacterCreateInfo
{
    public CharacterClass Class { get; set; }
    public ushort Map { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Rotation { get; set; }
    
    public List<ItemTemplateId> StartingItems { get; set; } = [];
    
    public List<SpellId> StartingSpells { get; set; } = [];
}
