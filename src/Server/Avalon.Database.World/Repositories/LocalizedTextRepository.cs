using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface ILocalizedTextRepository
{
    Task<IReadOnlyCollection<LocalizedText>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<LocalizedTextLocale>> GetAllLocalesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<CharacterClassName>> GetAllClassNamesAsync(CancellationToken cancellationToken = default);
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
}
