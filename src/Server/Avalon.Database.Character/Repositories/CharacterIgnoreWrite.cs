using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;

namespace Avalon.Database.Character.Repositories;

/// <summary>
/// A character's ignore entries to write (#723). Every ignored character in <paramref name="Rewrite" /> has its entry
/// deleted, and then <paramref name="Insert" /> (only those still on the list) are inserted, each only while the
/// ignored character still exists: one deleted since it was added is left out rather than failing the save on its
/// foreign key. Idempotent.
/// </summary>
public sealed record CharacterIgnoreWrite(
    IReadOnlyList<CharacterId> Rewrite,
    IReadOnlyList<CharacterIgnore> Insert);
