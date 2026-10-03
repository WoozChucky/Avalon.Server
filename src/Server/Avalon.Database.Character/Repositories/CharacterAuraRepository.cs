using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Character.Repositories;

/// <summary>
/// A character's aura rows to write: every row it has is deleted, then <paramref name="Rows" /> inserted, so what is
/// stored is exactly what the character held at the save. Idempotent.
/// </summary>
public sealed record CharacterAuraWrite(IReadOnlyList<CharacterAura> Rows);

public interface ICharacterAuraRepository
{
    /// <summary>The character's saved auras, by slot, untracked.</summary>
    Task<IReadOnlyList<CharacterAura>> GetByCharacterIdAsync(CharacterId owner, CancellationToken cancellationToken = default);
}

public sealed class CharacterAuraRepository(IDbContextFactory<CharacterDbContext> contextFactory) : ICharacterAuraRepository
{
    public async Task<IReadOnlyList<CharacterAura>> GetByCharacterIdAsync(CharacterId owner,
        CancellationToken cancellationToken = default)
    {
        await using CharacterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        List<CharacterAura> rows = await context.CharacterAuras.AsNoTracking()
            .Where(a => a.CharacterId == owner)
            .ToListAsync(cancellationToken);

        // Ordered here rather than in SQL: a character holds few, and the order is the slot's alone.
        return rows.OrderBy(a => a.Slot).ToList();
    }
}
