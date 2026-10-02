using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
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
        string key = CharacterName.LookupKey(name);
        if (key.Length == 0)
            return null;

        await using CharacterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var match = await context.Characters.AsNoTracking()
            .Where(c => c.NameKey == key)
            .Select(c => new { c.Id, c.Name })
            .FirstOrDefaultAsync(cancellationToken);
        return match is null ? null : new CharacterNameMatch(match.Id, match.Name);
    }
}
