using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Character.Repositories;

/// <summary>A character's quest rows, as select loads them (#433).</summary>
public sealed record CharacterQuestRows(
    IReadOnlyList<CharacterQuest> Active,
    IReadOnlyList<CharacterQuestObjective> Objectives,
    IReadOnlyList<CharacterCompletedQuest> Completed)
{
    public static readonly CharacterQuestRows None = new([], [], []);
}

public interface ICharacterQuestRepository
{
    Task<CharacterQuestRows> GetByCharacterIdAsync(CharacterId characterId, CancellationToken cancellationToken = default);
}

public class CharacterQuestRepository(IDbContextFactory<CharacterDbContext> contextFactory) : ICharacterQuestRepository
{
    public async Task<CharacterQuestRows> GetByCharacterIdAsync(CharacterId characterId, CancellationToken cancellationToken = default)
    {
        await using CharacterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        List<CharacterQuest> active = await context.CharacterQuests.AsNoTracking()
            .Where(q => q.CharacterId == characterId).ToListAsync(cancellationToken);
        List<CharacterQuestObjective> objectives = await context.CharacterQuestObjectives.AsNoTracking()
            .Where(o => o.CharacterId == characterId).ToListAsync(cancellationToken);
        List<CharacterCompletedQuest> completed = await context.CharacterCompletedQuests.AsNoTracking()
            .Where(c => c.CharacterId == characterId).ToListAsync(cancellationToken);
        return new CharacterQuestRows(active, objectives, completed);
    }
}
