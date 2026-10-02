using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Character.Repositories;

public interface ICharacterRepository : IRepository<Domain.Characters.Character, CharacterId>
{
    /// <summary>
    /// The character with this name in any case, ignoring surrounding spaces, found by its key (#757), or none. Names
    /// are unique by key, so there is at most one.
    /// </summary>
    Task<Domain.Characters.Character?> FindByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<Domain.Characters.Character?> FindByIdAndAccountAsync(CharacterId id, AccountId accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The account's characters, oldest first: by <see cref="Domain.Characters.Character.CreationDate"/>,
    /// then by id, so two created at the same instant still come back in one fixed order (#727).
    /// Every caller (the in-game character list, the list sent after a create, the REST API) shows
    /// them in this order.
    /// </summary>
    Task<List<Domain.Characters.Character>> FindByAccountAsync(AccountId accountId, CancellationToken cancellationToken = default);
}

public class CharacterRepository(IDbContextFactory<CharacterDbContext> contextFactory)
    : EntityFrameworkRepository<Domain.Characters.Character, CharacterId, CharacterDbContext>(contextFactory),
        ICharacterRepository
{
    public async Task<Domain.Characters.Character?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        string key = CharacterName.Key(name);
        await using var context = await CreateContextAsync(cancellationToken);

        return await context.Characters
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.NameKey == key, cancellationToken);
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
            .OrderBy(entity => entity.CreationDate)
            .ThenBy(entity => entity.Id)
            .ToListAsync(cancellationToken);
    }
}
