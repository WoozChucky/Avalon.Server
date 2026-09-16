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

public class CharacterAbilityRepository(IDbContextFactory<CharacterDbContext> contextFactory) : ICharacterAbilityRepository
{
    public async Task<CharacterAbility> CreateAsync(CharacterAbility ability, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.CharacterAbilities.AddAsync(ability, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return entity.Entity;
    }

    public async Task<IList<CharacterAbility>> CreateAsync(IList<CharacterAbility> abilities, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var entityList = new List<CharacterAbility>();
        foreach (var ability in abilities)
        {
            var entity = await context.CharacterAbilities.AddAsync(ability, cancellationToken);
            entityList.Add(entity.Entity);
        }
        await context.SaveChangesAsync(cancellationToken);
        return entityList;
    }

    public async Task<IReadOnlyCollection<CharacterAbility>> GetCharacterAbilitiesAsync(CharacterId characterId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.CharacterAbilities
            .AsNoTracking()
            .Where(cs => cs.CharacterId == characterId)
            .ToListAsync(cancellationToken);
    }
}
