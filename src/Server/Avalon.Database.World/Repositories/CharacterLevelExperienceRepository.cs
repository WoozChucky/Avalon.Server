using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

public interface ICharacterLevelExperienceRepository
{
    Task<IReadOnlyCollection<CharacterLevelExperience>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<CharacterLevelExperience?> GetLevelAsync(ushort level, CancellationToken cancellationToken = default);
}

public class CharacterLevelExperienceRepository(IDbContextFactory<WorldDbContext> contextFactory)
    : ICharacterLevelExperienceRepository
{
    public async Task<IReadOnlyCollection<CharacterLevelExperience>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.CharacterLevelExperiences
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<CharacterLevelExperience?> GetLevelAsync(ushort level, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.CharacterLevelExperiences
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Level == level, cancellationToken);
    }
}
