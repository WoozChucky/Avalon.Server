using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Character.Repositories;

public interface ICharacterRepository : IRepository<Domain.Characters.Character, CharacterId>
{
    Task<Domain.Characters.Character?> FindByNameAsync(string name);
    Task<Domain.Characters.Character?> FindByIdAndAccountAsync(CharacterId id, AccountId accountId);
    Task<IList<Domain.Characters.Character>> FindByAccountAsync(AccountId accountId);
}

public class CharacterRepository : EntityFrameworkRepository<Domain.Characters.Character, CharacterId>, ICharacterRepository
{
    public CharacterRepository(CharacterDbContext dbContext)
        : base(dbContext)
    {
    }

    public async Task<Domain.Characters.Character?> FindByNameAsync(string name)
    {
        return await Context.Set<Domain.Characters.Character>()
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Name == name);
    }

    public async Task<Domain.Characters.Character?> FindByIdAndAccountAsync(CharacterId id, AccountId accountId)
    {
        return await Context.Set<Domain.Characters.Character>()
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Id == id && entity.AccountId == accountId);

    }

    public async Task<IList<Domain.Characters.Character>> FindByAccountAsync(AccountId accountId)
    {
        return await Context.Set<Domain.Characters.Character>()
            .AsNoTracking()
            .Where(entity => entity.AccountId == accountId)
            .ToListAsync();
    }
}
