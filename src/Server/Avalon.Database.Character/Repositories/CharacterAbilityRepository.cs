using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Character.Repositories;

public interface ICharacterAbilityRepository
{
    Task<CharacterAbility> CreateAsync(CharacterAbility ability, CancellationToken cancellationToken = default);
    Task<IList<CharacterAbility>> CreateAsync(IList<CharacterAbility> abilities, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<CharacterAbility>> GetCharacterAbilitiesAsync(CharacterId characterId, CancellationToken cancellationToken = default);
}

public class CharacterAbilityRepository : ICharacterAbilityRepository
{
    private readonly CharacterDbContext _dbContext;

    public CharacterAbilityRepository(CharacterDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CharacterAbility> CreateAsync(CharacterAbility ability, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.CharacterAbilities.AddAsync(ability, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entity.Entity;
    }

    public async Task<IList<CharacterAbility>> CreateAsync(IList<CharacterAbility> abilities, CancellationToken cancellationToken = default)
    {
        var entityList = new List<CharacterAbility>();
        foreach (var ability in abilities)
        {
            var entity = await _dbContext.CharacterAbilities.AddAsync(ability, cancellationToken);
            entityList.Add(entity.Entity);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entityList;
    }

    public async Task<IReadOnlyCollection<CharacterAbility>> GetCharacterAbilitiesAsync(CharacterId characterId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.CharacterAbilities
            .AsNoTracking()
            .Where(cs => cs.CharacterId == characterId)
            .ToListAsync(cancellationToken);
    }


}
