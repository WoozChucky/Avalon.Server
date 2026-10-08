using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using CharacterRow = Avalon.Domain.Characters.Character;

namespace Avalon.Database.Character;

public partial class CharacterDbContext
{
    // Set only after CharacterSaveRepository has locked and validated all of its session guards.
    internal bool ValidatedGameplaySave { get; set; }

    /// <summary>
    /// Refused (#824): a save goes through the gameplay-write fence, its transaction and its query, which are asynchronous,
    /// and running them here would block a thread on them. Nothing saves this context synchronously; this override exists
    /// so that nothing can skip the fence by doing so. Use <see cref="SaveChangesAsync(bool, CancellationToken)" />.
    /// </summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException(
            $"{nameof(CharacterDbContext)} saves only asynchronously, through the gameplay-write fence: call {nameof(SaveChangesAsync)}.");

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EntityEntry[] owned = ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => e.Entity is CharacterRow || e.Metadata.FindProperty("CharacterId") is not null).ToArray();
        if (ValidatedGameplaySave || owned.Length == 0)
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
        bool ownsTransaction = Database.CurrentTransaction is null;
        await using IDbContextTransaction? transaction = ownsTransaction ? await Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false) : null;
        CharacterRow[] added = owned.Where(e => e.Entity is CharacterRow && e.State == EntityState.Added).Select(e => (CharacterRow)e.Entity).ToArray();
        CharacterRow[] existingRows = owned.Where(e => e.Entity is CharacterRow && e.State != EntityState.Added).Select(e => (CharacterRow)e.Entity).ToArray();
        // var, not an explicit type: an EF Core query captures this array, and an explicit non-nullable
        // array type makes the compiler add a Convert node to the expression tree EF Core translates.
#pragma warning disable IDE0008
        var ids = owned.Where(e => e.Entity is not CharacterRow).Select(e => e.Property("CharacterId").CurrentValue)
            .OfType<CharacterId>().Concat(existingRows.Select(c => c.Id)).Distinct().ToArray();
#pragma warning restore IDE0008
        var before = await Characters.AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.AccountId }).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (ids.Any(id => !before.Any(c => c.Id == id) && !added.Any(c => c.Id == id)) ||
            existingRows.Any(row => !before.Any(c => c.Id == row.Id && c.AccountId == row.AccountId)))
        {
            throw new GameplayWriteRejectedException();
        }

        AccountId[] accounts = before.Select(c => c.AccountId).Concat(added.Select(c => c.AccountId)).Distinct().OrderBy(a => a.Value).ToArray();
        foreach (AccountId? account in accounts)
        {
            if (account.Value <= 0) throw new GameplayWriteRejectedException();
            AccountGameplayFence guard = await GameplayFenceRepository.LockAsync(this, account, cancellationToken).ConfigureAwait(false);
            if (guard.ConsolidationId is not null) throw new GameplayWriteRejectedException();
        }
        var after = await Characters.AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.AccountId }).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (before.Count != after.Count || before.Any(old => !after.Any(current => current.Id == old.Id && current.AccountId == old.AccountId)))
            throw new GameplayWriteRejectedException();
        int result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    // ExecuteUpdate (rename) bypasses SaveChanges, so its caller holds an explicit transaction too.
    internal async Task GuardCharacterMutationAsync(CharacterId id, CancellationToken cancellationToken)
    {
        AccountId? owner = await Characters.AsNoTracking().Where(c => c.Id == id).Select(c => c.AccountId).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (owner is null) return;
        AccountGameplayFence guard = await GameplayFenceRepository.LockAsync(this, owner, cancellationToken).ConfigureAwait(false);
        if (guard.ConsolidationId is not null || !await Characters.AnyAsync(c => c.Id == id && c.AccountId == owner, cancellationToken).ConfigureAwait(false))
            throw new GameplayWriteRejectedException();
    }
}
