using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Character.Repositories;

public partial interface ICharacterRepository : IRepository<Domain.Characters.Character, CharacterId>
{
    /// <summary>
    /// The character with this name in any case, ignoring surrounding spaces, found by its key (#757), or none. Names
    /// are unique by key, so there is at most one.
    /// </summary>
    Task<Domain.Characters.Character?> FindByNameAsync(string name, CancellationToken cancellationToken = default);
    /// <summary>
    /// Renames a character that is offline, in one conditional statement (#757): <c>UPDATE ... SET Name, NameKey
    /// WHERE Id = @id AND NOT Online</c>. The world marks a row online with a write of the same row, so the two are
    /// ordered by its row lock: the rename either sees it online and writes nothing, or commits first. The name is
    /// written as given (the caller applies the rule and the display form). No tracked update ever writes the name
    /// (both columns are insert-only to the change tracker), so this is the one way a name changes.
    /// </summary>
    Task<CharacterRename> TryRenameAsync(CharacterId id, string name, CancellationToken cancellationToken = default);

    Task<Domain.Characters.Character?> FindByIdAndAccountAsync(CharacterId id, AccountId accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The account's characters, oldest first: by <see cref="Domain.Characters.Character.CreationDate"/>,
    /// then by id, so two created at the same instant still come back in one fixed order (#727).
    /// Every caller (the in-game character list, the list sent after a create, the REST API) shows
    /// them in this order.
    /// </summary>
    Task<List<Domain.Characters.Character>> FindByAccountAsync(AccountId accountId, CancellationToken cancellationToken = default);
}

public partial class CharacterRepository(IDbContextFactory<CharacterDbContext> contextFactory)
    : EntityFrameworkRepository<Domain.Characters.Character, CharacterId, CharacterDbContext>(contextFactory),
        ICharacterRepository
{
    public async Task<Domain.Characters.Character?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        string key = CharacterName.LookupKey(name);
        await using var context = await CreateContextAsync(cancellationToken);

        return await context.Characters
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.NameKey == key, cancellationToken);
    }

    public async Task<CharacterRename> TryRenameAsync(CharacterId id, string name, CancellationToken cancellationToken = default)
    {
        string key = CharacterName.Key(name);
        await using var context = await CreateContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.GuardCharacterMutationAsync(id, cancellationToken);

        try
        {
            int written = await context.Characters
                .Where(c => c.Id == id && !c.Online)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.Name, name).SetProperty(c => c.NameKey, key),
                    cancellationToken);
            if (written == 1)
            {
                await transaction.CommitAsync(cancellationToken);
                return CharacterRename.Renamed;
            }
        }
        catch (Exception ex) when (CharacterNameKeyViolation.Is(ex))
        {
            return CharacterRename.NameTaken;
        }

        return await context.Characters.AnyAsync(c => c.Id == id, cancellationToken)
            ? CharacterRename.Online
            : CharacterRename.NotFound;
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
