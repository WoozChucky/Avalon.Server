using Avalon.World.Entities;
using Avalon.World.Public;

namespace Avalon.World.Social;

/// <summary>The one test every chat path and the party invite ask (#723), against memory only.</summary>
public static class Ignoring
{
    /// <summary>
    /// Whether the character the listener's connection holds ignores <paramref name="senderId" />. A connection with no
    /// character, or one that is not the World-side entity, ignores nobody.
    /// </summary>
    public static bool Hides(IWorldConnection listener, uint senderId) =>
        listener.Character is CharacterEntity entity && entity.Ignores.Contains(senderId);
}
