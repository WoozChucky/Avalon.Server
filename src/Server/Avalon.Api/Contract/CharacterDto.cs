using Avalon.Common.ValueObjects;

namespace Avalon.Api.Contract;

public class CharacterDto
{
    public uint Id { get; set; }

    public string Name { get; set; }

    public CharacterClass Class { get; set; }

    public CharacterGender Gender { get; set; }

    public ushort Level { get; set; }

    public ulong Experience { get; set; }

    public ushort Map { get; set; }

    public bool Online { get; set; }

    public ulong TotalTime { get; set; }

    public int TotalKills { get; set; }

    public int ChosenTitle { get; set; }

    public int Health { get; set; }

    public int Latency { get; set; }

    public DateTime CreationDate { get; set; }

    public ulong DeleteDate { get; set; }
}


public enum CharacterClass : ushort
{
    Warrior = 1,
    Wizard = 2,
    Hunter = 3,
    Healer = 4,
}

public enum CharacterGender : byte
{
    Male = 0,
    Female = 1,
}
