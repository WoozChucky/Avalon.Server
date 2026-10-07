namespace Avalon.Api.Contract;

public sealed class CharacterPatchDto
{
    // Cosmetic (any owner or Admin+). 3 to 12 ASCII letters (#757), stored first letter upper-case and the rest
    // lower-case; a name another character holds in any case is refused as "Name already taken".
    [CharacterNameRule] public string? Name { get; set; }

    // Admin+ only
    public ushort? Level { get; set; }
    public ulong? Experience { get; set; }
    public int? Health { get; set; }
    public int? Power1 { get; set; }
    public int? Power2 { get; set; }
}
