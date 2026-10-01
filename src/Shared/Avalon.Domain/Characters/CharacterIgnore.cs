using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Characters;

/// <summary>
/// One character on another's ignore list (#723): <see cref="CharacterId" /> ignores
/// <see cref="IgnoredCharacterId" />. Both are characters of the same world; deleting either deletes the row.
/// </summary>
public class CharacterIgnore
{
    public CharacterId CharacterId { get; set; } = default!;
    public CharacterId IgnoredCharacterId { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
}
