using Avalon.World.Public.Enums;

namespace Avalon.Domain.World;

public class ClassLevelStat
{
    public CharacterClass Class { get; set; }
    public ushort Level { get; set; }
    public uint BaseHp { get; set; }
    public uint BaseMana { get; set; }
    public uint Stamina { get; set; }
    public uint Strength { get; set; }
    public uint Agility { get; set; }
    public uint Intellect { get; set; }
}
