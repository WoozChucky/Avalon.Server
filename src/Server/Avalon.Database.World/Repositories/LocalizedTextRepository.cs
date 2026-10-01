using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface ILocalizedTextRepository
{
    Task<IReadOnlyCollection<LocalizedText>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<LocalizedTextLocale>> GetAllLocalesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<CharacterClassName>> GetAllClassNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>The base-locale (enUS) rows of these texts, untracked; an id with no row is left out (#714).</summary>
    Task<IReadOnlyCollection<LocalizedText>> GetByIdsAsync(IEnumerable<LocalizedTextId> ids, CancellationToken cancellationToken = default);
}

public class LocalizedTextRepository(IDbContextFactory<WorldDbContext> contextFactory)
    : ILocalizedTextRepository
{
    public async Task<IReadOnlyCollection<LocalizedText>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.LocalizedTexts.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<LocalizedTextLocale>> GetAllLocalesAsync(CancellationToken cancellationToken = default)
    {
        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.LocalizedTextLocales.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<CharacterClassName>> GetAllClassNamesAsync(CancellationToken cancellationToken = default)
    {
        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.CharacterClassNames.AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<LocalizedText>> GetByIdsAsync(IEnumerable<LocalizedTextId> ids,
        CancellationToken cancellationToken = default)
    {
        LocalizedTextId[] idSet = ids.Distinct().ToArray();
        if (idSet.Length == 0) return [];

        await using WorldDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.LocalizedTexts.AsNoTracking().Where(t => idSet.Contains(t.Id)).ToListAsync(cancellationToken);
    }
}
