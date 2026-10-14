using Avalon.Common;
using Avalon.Common.ValueObjects;

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

    public List<AbilityId> StartingSpells { get; set; } = [];
}
