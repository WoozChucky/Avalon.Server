using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database.Character.Repositories;

public partial class CharacterSaveRepository
{
    private async Task<AccountGameplayFence[]> GuardAsync(CharacterDbContext db, IReadOnlyList<CharacterSaveBatch> batches, CancellationToken cancellationToken)
    {
        var guards = new List<AccountGameplayFence>();
        // Every account lock is acquired in the same order by saves and barrier/consolidation operations.
        foreach (IGrouping<long, CharacterSaveBatch>? group in batches.GroupBy(b => b.Row.AccountId.Value).OrderBy(g => g.Key))
        {
            CharacterSaveBatch first = group.First();
            GameplayWriteAuthority? authority = first.Authority;
            if (authority is null || !GameplayFenceRepository.Valid(authority) || authority.AccountId != first.Row.AccountId ||
                group.Any(b => b.Authority is null || b.Authority.AccountId != authority.AccountId ||
                    b.Authority.GameSessionId != authority.GameSessionId || b.Authority.FencingToken != authority.FencingToken))
                throw new GameplayWriteRejectedException();
            AccountGameplayFence guard = await GameplayFenceRepository.LockAsync(db, authority.AccountId, cancellationToken);
            if (guard.GameSessionId != authority.GameSessionId || guard.FencingToken != authority.FencingToken ||
                guard.Mode is not (GameplayFenceMode.Active or GameplayFenceMode.Draining)) throw new GameplayWriteRejectedException();
            guards.Add(guard);
        }
        DateTime now = await GameplayFenceRepository.NowAsync(db, clock, cancellationToken);
        if (guards.Any(g => g.LeaseUntil <= now)) throw new GameplayWriteRejectedException();
        // var, not an explicit type: an EF Core query captures this array, and an explicit non-nullable
        // array type makes the compiler add a Convert node to the expression tree EF Core translates.
#pragma warning disable IDE0008
        var ids = batches.Select(b => b.Row.Id).ToArray();
#pragma warning restore IDE0008
        var owners = await db.Characters.Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.AccountId }).ToListAsync(cancellationToken);
        if (owners.Count != batches.Count || batches.Any(b => !owners.Any(c => c.Id == b.Row.Id && c.AccountId == b.Row.AccountId)))
            throw new GameplayWriteRejectedException();
        // A snapshot cannot restore ownership after a transfer, nor touch another character's child rows.
        foreach (CharacterSaveBatch batch in batches)
        {
            if (batch.UpsertItems.Any(i => i.CharacterId != batch.Row.Id) || batch.UpsertSlots.Any(i => i.CharacterId != batch.Row.Id) ||
                (batch.Stats is { } stats && stats.CharacterId != batch.Row.Id) ||
                (batch.Quests is { } quests && (quests.Active.Any(q => q.CharacterId != batch.Row.Id) ||
                    quests.Objectives.Any(q => q.CharacterId != batch.Row.Id) || quests.Completed.Any(q => q.CharacterId != batch.Row.Id))) ||
                (batch.Auras is { } auras && auras.Rows.Any(a => a.CharacterId != batch.Row.Id)) ||
                (batch.Ignores is { } ignores && ignores.Insert.Any(i => i.CharacterId != batch.Row.Id))) throw new GameplayWriteRejectedException();
        }
        // var, not an explicit type: an EF Core query captures this array, and an explicit non-nullable
        // array type makes the compiler add a Convert node to the expression tree EF Core translates.
#pragma warning disable IDE0008
        var itemIds = batches.SelectMany(b => b.UpsertItems.Select(i => i.Id).Concat(b.DeleteItems).Concat(b.UpsertSlots.Select(i => i.ItemId))).Distinct().ToArray();
#pragma warning restore IDE0008
        if (itemIds.Length > 0 && await db.ItemInstances.AnyAsync(i => itemIds.Contains(i.Id) && !ids.Contains(i.CharacterId), cancellationToken))
            throw new GameplayWriteRejectedException();
        return guards.ToArray();
    }
    private async Task CheckDeadlineAsync(CharacterDbContext db, AccountGameplayFence[] guards, CancellationToken cancellationToken)
    {
        DateTime now = await GameplayFenceRepository.NowAsync(db, clock, cancellationToken);
        if (guards.Any(g => g.LeaseUntil <= now)) throw new GameplayWriteRejectedException();
    }
}
