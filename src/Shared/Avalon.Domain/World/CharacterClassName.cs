using Avalon.Common.ValueObjects;
using Avalon.World.Public.Enums;

namespace Avalon.Domain.World;

/// <summary>
/// A class's display name as a localised string. "Warrior" is an English word, and in Portuguese
/// it is gender-inflected (Guerreiro / Guerreira), so the {class} token cannot just print the enum.
/// </summary>
public class CharacterClassName
{
    public CharacterClass Class { get; set; }
    public LocalizedTextId TextId { get; set; } = default!;
}
