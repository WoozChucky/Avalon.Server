namespace Avalon.Api.Contract;

public sealed class CharacterPatchDto
{
    // Cosmetic (any owner or Admin+)
    public string? Name { get; set; }

    // Admin+ only
    public ushort? Level { get; set; }
    public ulong?  Experience { get; set; }
    public int?    Health { get; set; }
    public int?    Power1 { get; set; }
    public int?    Power2 { get; set; }
}
