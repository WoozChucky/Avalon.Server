using Avalon.Common.ValueObjects;
using Avalon.World.Public.Enums;

namespace Avalon.Api.Contract;

/// <summary>
/// A character (#523). <see cref="Id"/> is unique only within one world, since each world keeps its
/// own characters database: the pair (<see cref="WorldId"/>, <see cref="Id"/>) identifies a character
/// across worlds (#556).
/// </summary>
public class CharacterDto
{
    /// <summary>The character's id in its world's characters database; unique only within that world (#556).</summary>
    public uint Id { get; set; }

    /// <summary>The world this character lives on (#523): its id in the auth Worlds table.</summary>
    public ushort WorldId { get; set; }

    /// <summary>That world's name, from the auth Worlds row.</summary>
    public string WorldName { get; set; } = "";

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
