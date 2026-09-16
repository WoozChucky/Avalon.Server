using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Character.Repositories;

public interface ICharacterRepository : IRepository<Domain.Characters.Character, CharacterId>
{
    Task<Domain.Characters.Character?> FindByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<Domain.Characters.Character?> FindByIdAndAccountAsync(CharacterId id, AccountId accountId, CancellationToken cancellationToken = default);
    Task<List<Domain.Characters.Character>> FindByAccountAsync(AccountId accountId, CancellationToken cancellationToken = default);
}

public class CharacterRepository(IDbContextFactory<CharacterDbContext> contextFactory)
    : EntityFrameworkRepository<Domain.Characters.Character, CharacterId, CharacterDbContext>(contextFactory),
        ICharacterRepository
{
    public async Task<Domain.Characters.Character?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        return await context.Characters
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Name == name, cancellationToken);
    }

    public async Task<Domain.Characters.Character?> FindByIdAndAccountAsync(CharacterId id, AccountId accountId, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        return await context.Characters
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Id == id && entity.AccountId == accountId, cancellationToken);
    }

    public async Task<List<Domain.Characters.Character>> FindByAccountAsync(AccountId accountId, CancellationToken cancellationToken = default)
    {
        await using var context = await CreateContextAsync(cancellationToken);

        return await context.Characters
            .AsNoTracking()
            .Where(entity => entity.AccountId == accountId)
            .ToListAsync(cancellationToken);
    }
}
