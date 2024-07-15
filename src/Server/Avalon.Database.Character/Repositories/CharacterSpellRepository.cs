using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Character.Repositories;

public interface ICharacterSpellRepository
{
    Task<CharacterSpell> CreateAsync(CharacterSpell spell);
    Task<IReadOnlyCollection<CharacterSpell>> GetCharacterSpellsAsync(CharacterId characterId);
}

public class CharacterSpellRepository : ICharacterSpellRepository
{
    private readonly CharacterDbContext _dbContext;
    
    public CharacterSpellRepository(CharacterDbContext dbContext)
    {
        _dbContext = dbContext;
    }
    
    public async Task<CharacterSpell> CreateAsync(CharacterSpell spell)
    {
        var entity = await _dbContext.CharacterSpells.AddAsync(spell);
        await _dbContext.SaveChangesAsync();
        return entity.Entity;
    }
    
    public async Task<IReadOnlyCollection<CharacterSpell>> GetCharacterSpellsAsync(CharacterId characterId)
    {
        return await _dbContext.CharacterSpells
            .AsNoTracking()
            .Where(cs => cs.CharacterId == characterId)
            .ToListAsync();
    }
    
    
}
