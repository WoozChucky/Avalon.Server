using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Character.Repositories;

public interface ICharacterSpellRepository
{
    Task<CharacterSpell> CreateAsync(CharacterSpell spell, CancellationToken cancellationToken = default);
    Task<IList<CharacterSpell>> CreateAsync(IList<CharacterSpell> spells, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<CharacterSpell>> GetCharacterSpellsAsync(CharacterId characterId, CancellationToken cancellationToken = default);
}

public class CharacterSpellRepository : ICharacterSpellRepository
{
    private readonly CharacterDbContext _dbContext;

    public CharacterSpellRepository(CharacterDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CharacterSpell> CreateAsync(CharacterSpell spell, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.CharacterSpells.AddAsync(spell, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entity.Entity;
    }

    public async Task<IList<CharacterSpell>> CreateAsync(IList<CharacterSpell> spells, CancellationToken cancellationToken = default)
    {
        var entityList = new List<CharacterSpell>();
        foreach (var spell in spells)
        {
            var entity = await _dbContext.CharacterSpells.AddAsync(spell, cancellationToken);
            entityList.Add(entity.Entity);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entityList;
    }

    public async Task<IReadOnlyCollection<CharacterSpell>> GetCharacterSpellsAsync(CharacterId characterId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.CharacterSpells
            .AsNoTracking()
            .Where(cs => cs.CharacterId == characterId)
            .ToListAsync(cancellationToken);
    }


}
