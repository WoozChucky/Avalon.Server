using Avalon.Common.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Character.Repositories;

public sealed class CharacterIgnoreRepository(IDbContextFactory<CharacterDbContext> contextFactory) : ICharacterIgnoreRepository
{
    public async Task<IReadOnlyList<IgnoredCharacterRow>> GetByCharacterIdAsync(CharacterId owner,
        CancellationToken cancellationToken = default)
    {
        await using CharacterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.CharacterIgnores.AsNoTracking()
            .Where(i => i.CharacterId == owner)
            .Join(context.Characters, i => i.IgnoredCharacterId, c => c.Id,
                (i, c) => new { c.Id, c.Name, i.CreatedAt })
            .ToListAsync(cancellationToken);

        // Ordered here rather than in SQL: SQLite (the tests) cannot order by a converted id, and a list is small.
        return rows
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id.Value)
            .Select(r => new IgnoredCharacterRow(r.Id, r.Name, r.CreatedAt))
            .ToList();
    }

    public async Task<CharacterNameMatch?> FindCharacterByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0)
            return null;

        string upper = trimmed.ToUpperInvariant();
        await using CharacterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var matches = await context.Characters.AsNoTracking()
#pragma warning disable MA0011 // translated to SQL upper(); a culture overload has no translation
            .Where(c => c.Name.ToUpper() == upper)
#pragma warning restore MA0011
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(cancellationToken);

        var match = matches
            .OrderBy(m => string.Equals(m.Name, trimmed, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(m => m.Id.Value)
            .FirstOrDefault();
        return match is null ? null : new CharacterNameMatch(match.Id, match.Name);
    }
}
