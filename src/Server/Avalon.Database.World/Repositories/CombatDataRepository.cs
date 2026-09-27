using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.World.Repositories;

/// <summary>The combat area's tables (#506): the formula row and the per-class stat factors.</summary>
public interface ICombatDataRepository
{
    Task<IReadOnlyCollection<CombatFormula>> GetFormulasAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ClassStatFactors>> GetClassStatFactorsAsync(CancellationToken cancellationToken = default);
}

public class CombatDataRepository(IDbContextFactory<WorldDbContext> contextFactory) : ICombatDataRepository
{
    public async Task<IReadOnlyCollection<CombatFormula>> GetFormulasAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.CombatFormulas
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<ClassStatFactors>> GetClassStatFactorsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.ClassStatFactors
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
