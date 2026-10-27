using Avalon.Domain.Characters;

namespace Avalon.Database.Character.Repositories;

/// <summary>
/// A character's talent rows to write (#1181). Every node in <paramref name="Rewrite" /> has its row deleted, and then
/// <paramref name="Insert" /> (only the nodes still held) are inserted, in the same transaction. Idempotent: a retry
/// deletes and inserts the same rows again.
/// </summary>
public sealed record CharacterTalentWrite(
    IReadOnlyList<int> Rewrite,
    IReadOnlyList<CharacterTalent> Insert);
