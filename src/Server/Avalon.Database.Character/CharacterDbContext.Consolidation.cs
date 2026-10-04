using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Microsoft.EntityFrameworkCore;
using CharacterRow = Avalon.Domain.Characters.Character;

namespace Avalon.Database.Character;

public partial class CharacterDbContext
{
    // Set only after CharacterSaveRepository has locked and validated all of its session guards.
    internal bool ValidatedGameplaySave { get; set; }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        SaveChangesAsync(acceptAllChangesOnSuccess, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var owned = ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => e.Entity is CharacterRow || e.Metadata.FindProperty("CharacterId") is not null).ToArray();
        if (ValidatedGameplaySave || owned.Length == 0)
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
        var ownsTransaction = Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false) : null;
        var added = owned.Where(e => e.Entity is CharacterRow && e.State == EntityState.Added).Select(e => (CharacterRow)e.Entity).ToArray();
        var existingRows = owned.Where(e => e.Entity is CharacterRow && e.State != EntityState.Added).Select(e => (CharacterRow)e.Entity).ToArray();
        var ids = owned.Where(e => e.Entity is not CharacterRow).Select(e => e.Property("CharacterId").CurrentValue)
            .OfType<CharacterId>().Concat(existingRows.Select(c => c.Id)).Distinct().ToArray();
        var before = await Characters.AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.AccountId }).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (ids.Any(id => !before.Any(c => c.Id == id) && !added.Any(c => c.Id == id)) ||
            existingRows.Any(row => !before.Any(c => c.Id == row.Id && c.AccountId == row.AccountId))) throw new GameplayWriteRejectedException();
        var accounts = before.Select(c => c.AccountId).Concat(added.Select(c => c.AccountId)).Distinct().OrderBy(a => a.Value).ToArray();
        foreach (var account in accounts)
        {
            if (account.Value <= 0) throw new GameplayWriteRejectedException();
            var guard = await GameplayFenceRepository.LockAsync(this, account, cancellationToken).ConfigureAwait(false);
            if (guard.ConsolidationId is not null) throw new GameplayWriteRejectedException();
        }
        var after = await Characters.AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.AccountId }).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (before.Count != after.Count || before.Any(old => !after.Any(current => current.Id == old.Id && current.AccountId == old.AccountId)))
            throw new GameplayWriteRejectedException();
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    // ExecuteUpdate (rename) bypasses SaveChanges, so its caller holds an explicit transaction too.
    internal async Task GuardCharacterMutationAsync(CharacterId id, CancellationToken cancellationToken)
    {
        var owner = await Characters.AsNoTracking().Where(c => c.Id == id).Select(c => c.AccountId).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (owner is null) return;
        var guard = await GameplayFenceRepository.LockAsync(this, owner, cancellationToken).ConfigureAwait(false);
        if (guard.ConsolidationId is not null || !await Characters.AnyAsync(c => c.Id == id && c.AccountId == owner, cancellationToken).ConfigureAwait(false))
            throw new GameplayWriteRejectedException();
    }
}
