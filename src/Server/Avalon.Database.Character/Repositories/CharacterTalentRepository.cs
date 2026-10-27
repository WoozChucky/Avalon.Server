using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Character.Repositories;

public interface ICharacterTalentRepository
{
    /// <summary>Every talent node the character holds, by ascending node id, as select loads them.</summary>
    Task<IReadOnlyList<CharacterTalent>> GetByCharacterIdAsync(CharacterId id, CancellationToken ct = default);
}

public sealed class CharacterTalentRepository(IDbContextFactory<CharacterDbContext> contextFactory) : ICharacterTalentRepository
{
    public async Task<IReadOnlyList<CharacterTalent>> GetByCharacterIdAsync(CharacterId id, CancellationToken ct = default)
    {
        await using CharacterDbContext context = await contextFactory.CreateDbContextAsync(ct);
        return await context.CharacterTalents.AsNoTracking()
            .Where(t => t.CharacterId == id)
            .OrderBy(t => t.NodeId)
            .ToListAsync(ct);
    }
}
