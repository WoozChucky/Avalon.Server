namespace Avalon.Api.Contract;

/// <summary>A character's auras as its world last saved them (auras), by slot. Their time stands still while it is offline.</summary>
public sealed class CharacterAurasDto
{
    public uint CharacterId { get; set; }
    public IList<CharacterAuraDto> Auras { get; set; } = [];
}
