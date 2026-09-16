using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Character.Repositories;

public interface ICharacterStatsRepository
{
    Task<CharacterStats> CreateAsync(CharacterStats stats, CancellationToken cancellationToken = default);
    Task<CharacterStats> UpdateAsync(CharacterStats stats, CancellationToken cancellationToken = default);
    Task<CharacterStats?> GetByCharacterIdAsync(CharacterId characterId, CancellationToken cancellationToken = default);
}

public class CharacterStatsRepository(IDbContextFactory<CharacterDbContext> contextFactory) : ICharacterStatsRepository
{
    public async Task<CharacterStats> CreateAsync(CharacterStats stats, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = context.TrackForInsert(stats);
        await context.SaveChangesAsync(cancellationToken);
        return entity.Entity;
    }

    public async Task<CharacterStats> UpdateAsync(CharacterStats stats, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = context.TrackForUpdate(stats);
        await context.SaveChangesAsync(cancellationToken);
        return entity.Entity;
    }

    public async Task<CharacterStats?> GetByCharacterIdAsync(CharacterId characterId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.CharacterStats
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.CharacterId == characterId, cancellationToken);
    }
}
