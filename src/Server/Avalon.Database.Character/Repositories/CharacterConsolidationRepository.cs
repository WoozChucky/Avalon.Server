using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Character.Repositories;

public sealed record CharacterConsolidationResult(string? Error, int TransferredCharacters = 0);
public interface ICharacterConsolidationRepository
{
    Task<bool> PrepareAsync(Guid operation, AccountId source, AccountId target, CancellationToken cancellationToken);
    Task<CharacterConsolidationResult> TransferAsync(Guid operation, AccountId source, AccountId target, CancellationToken cancellationToken);
    Task<bool> ReleaseTargetAsync(Guid operation, AccountId source, AccountId target, CancellationToken cancellationToken);
}

public sealed class CharacterConsolidationRepository(IDbContextFactory<CharacterDbContext> factory, TimeProvider? clock = null) : ICharacterConsolidationRepository
{
    public async Task<bool> PrepareAsync(Guid operation, AccountId source, AccountId target, CancellationToken cancellationToken)
    {
        if (!Valid(operation, source, target)) return false;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var guards = await LockRoots(db, source, target, cancellationToken);
        var receipt = await db.CharacterConsolidationReceipts.AsNoTracking().SingleOrDefaultAsync(r => r.Id == operation, cancellationToken);
        if (receipt is not null) return Matches(receipt, source, target);
        if (guards.Any(g => g.ConsolidationId is not null && g.ConsolidationId != operation)) return false;
        var now = await GameplayFenceRepository.NowAsync(db, clock, cancellationToken);
        foreach (var guard in guards)
        {
            guard.ConsolidationId = operation;
            if (guard.Mode is GameplayFenceMode.Active or GameplayFenceMode.Draining && guard.LeaseUntil > now)
                guard.Mode = GameplayFenceMode.Draining;
            else { guard.Mode = GameplayFenceMode.Blocked; guard.LeaseUntil = now; }
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
    public async Task<CharacterConsolidationResult> TransferAsync(Guid operation, AccountId source, AccountId target, CancellationToken cancellationToken)
    {
        if (!Valid(operation, source, target)) return new("INVALID_CONSOLIDATION");
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var guards = await LockRoots(db, source, target, cancellationToken);
        var receipt = await db.CharacterConsolidationReceipts.AsNoTracking().SingleOrDefaultAsync(r => r.Id == operation, cancellationToken);
        if (receipt is not null) return Matches(receipt, source, target) ? new(null, receipt.TransferredCharacters) : new("INVALID_CONSOLIDATION");
        if (guards.Any(g => g.ConsolidationId != operation)) return new("INVALID_CONSOLIDATION");
        var now = await GameplayFenceRepository.NowAsync(db, clock, cancellationToken);
        if (guards.Any(g => g.Mode == GameplayFenceMode.Draining && g.LeaseUntil > now)) return new("WAITING_FOR_SESSION");
        foreach (var guard in guards) { guard.Mode = GameplayFenceMode.Blocked; guard.LeaseUntil = now; }
        var count = await db.Characters.Where(c => c.AccountId == source)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.AccountId, target).SetProperty(c => c.Online, false), cancellationToken);
        await db.Characters.Where(c => c.AccountId == target && c.Online)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.Online, false), cancellationToken);
        db.CharacterConsolidationReceipts.Add(new CharacterConsolidationReceipt { Id = operation, SourceAccountId = source,
            TargetAccountId = target, TransferredCharacters = count, TransferredAt = now });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(null, count);
    }
    public async Task<bool> ReleaseTargetAsync(Guid operation, AccountId source, AccountId target, CancellationToken cancellationToken)
    {
        if (!Valid(operation, source, target)) return false;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var guards = await LockRoots(db, source, target, cancellationToken);
        var receipt = await db.CharacterConsolidationReceipts.AsNoTracking().SingleOrDefaultAsync(r => r.Id == operation, cancellationToken);
        if (receipt is null || !Matches(receipt, source, target)) return false;
        var sourceGuard = guards.Single(g => g.AccountId == source);
        var targetGuard = guards.Single(g => g.AccountId == target);
        if (sourceGuard.ConsolidationId != operation || sourceGuard.Mode != GameplayFenceMode.Blocked ||
            (targetGuard.ConsolidationId is not null && targetGuard.ConsolidationId != operation)) return false;
        if (targetGuard.ConsolidationId == operation)
        {
            targetGuard.ConsolidationId = null;
            targetGuard.Mode = GameplayFenceMode.Blocked;
            targetGuard.LeaseUntil = await GameplayFenceRepository.NowAsync(db, clock, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
    private static bool Valid(Guid operation, AccountId source, AccountId target) => operation != Guid.Empty && source.Value > 0 && target.Value > 0 && source != target;
    private static bool Matches(CharacterConsolidationReceipt receipt, AccountId source, AccountId target) => receipt.SourceAccountId == source && receipt.TargetAccountId == target;
    private static async Task<AccountGameplayFence[]> LockRoots(CharacterDbContext db, AccountId source, AccountId target, CancellationToken cancellationToken)
    {
        var guards = new List<AccountGameplayFence>();
        foreach (var id in new[] { source, target }.OrderBy(a => a.Value)) guards.Add(await GameplayFenceRepository.LockAsync(db, id, cancellationToken));
        return guards.ToArray();
    }
}
