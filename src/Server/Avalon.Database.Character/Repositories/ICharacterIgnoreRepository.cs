using Avalon.Common.ValueObjects;

namespace Avalon.Database.Character.Repositories;

public interface ICharacterIgnoreRepository
{
    /// <summary>Every character the owner ignores, oldest entry first, as select loads them.</summary>
    Task<IReadOnlyList<IgnoredCharacterRow>> GetByCharacterIdAsync(CharacterId owner, CancellationToken cancellationToken = default);

    /// <summary>
    /// The character with this name, ignoring case and surrounding spaces, online or not, found by its key (#757):
    /// names are unique by key, so there is at most one. None when no character has it.
    /// </summary>
    Task<CharacterNameMatch?> FindCharacterByNameAsync(string name, CancellationToken cancellationToken = default);
}
