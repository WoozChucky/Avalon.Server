namespace Avalon.Api.Contract;

/// <summary>
/// GET /character (#523): the caller's characters on every world this api serves and the caller may
/// enter, each carrying its world. A character is identified by (WorldId, Id), never by Id alone (#556).
/// </summary>
public sealed class CharacterListDto
{
    public List<CharacterDto> Characters { get; set; } = [];

    /// <summary>
    /// Worlds the caller may enter whose characters could not be read: unavailable since startup, or
    /// failing now. The call itself still succeeds with the other worlds' characters.
    /// </summary>
    public List<ushort> UnavailableWorlds { get; set; } = [];
}
