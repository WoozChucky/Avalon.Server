using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using CharacterRow = Avalon.Domain.Characters.Character;

namespace Avalon.Database.Character.Repositories;

public sealed record CharacterCreationBatch(CharacterRow Row, CharacterStats Stats, IReadOnlyList<CharacterAbility> Abilities,
    IReadOnlyList<ItemInstance> Items, IReadOnlyList<CharacterInventory> Slots);
public sealed record CharacterCreationReply(CharacterRow? Character = null, string? Error = null);

public partial interface ICharacterRepository
{
    Task<CharacterCreationReply> CreateForGameplayAsync(GameplayWriteAuthority authority, CharacterCreationBatch batch, int maximum, CancellationToken cancellationToken = default);
    Task<bool> DeleteForGameplayAsync(GameplayWriteAuthority authority, CharacterId id, CancellationToken cancellationToken = default);
    Task<CharacterRow> UpdateForGameplayAsync(GameplayWriteAuthority authority, CharacterRow row, CancellationToken cancellationToken = default);
    Task<CharacterRow?> FindForGameplayAsync(GameplayWriteAuthority authority, CharacterId id, CancellationToken cancellationToken = default);
}

public partial class CharacterRepository
{
    public async Task<CharacterCreationReply> CreateForGameplayAsync(GameplayWriteAuthority authority, CharacterCreationBatch batch, int maximum, CancellationToken cancellationToken = default)
    {
        if (batch.Row.AccountId != authority.AccountId || batch.Row.Id is { Value: not 0 } || maximum <= 0 ||
            batch.Items.Any(i => i.Id.Value == Guid.Empty) || batch.Items.Select(i => i.Id).Distinct().Count() != batch.Items.Count ||
            batch.Slots.Any(s => !batch.Items.Any(i => i.Id == s.ItemId))) throw new GameplayWriteRejectedException();
        await using CharacterDbContext db = await CreateContextAsync(cancellationToken);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        AccountGameplayFence guard = await HoldGameplayAsync(db, authority, cancellationToken);
        if (await db.Characters.CountAsync(c => c.AccountId == authority.AccountId, cancellationToken) >= maximum)
            return new(Error: GameAuthErrors.MaxCharacters);
        db.ValidatedGameplaySave = true;
        db.Characters.Add(batch.Row);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            CharacterId id = batch.Row.Id;
            batch.Stats.CharacterId = id; db.CharacterStats.Add(batch.Stats);
            foreach (CharacterAbility ability in batch.Abilities) { ability.CharacterId = id; db.CharacterAbilities.Add(ability); }
            foreach (ItemInstance item in batch.Items) { item.CharacterId = id; db.ItemInstances.Add(item); }
            foreach (CharacterInventory slot in batch.Slots) { slot.CharacterId = id; db.CharacterInventory.Add(slot); }
            await db.SaveChangesAsync(cancellationToken);
            await CheckGameplayDeadlineAsync(db, guard, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(batch.Row);
        }
        catch (DbUpdateException error) when (CharacterNameKeyViolation.Is(error)) { return new(Error: GameAuthErrors.NameTaken); }
    }
    public async Task<bool> DeleteForGameplayAsync(GameplayWriteAuthority authority, CharacterId id, CancellationToken cancellationToken = default)
    {
        await using CharacterDbContext db = await CreateContextAsync(cancellationToken);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        AccountGameplayFence guard = await HoldGameplayAsync(db, authority, cancellationToken);
        int deleted = await db.Characters.Where(c => c.Id == id && c.AccountId == authority.AccountId).ExecuteDeleteAsync(cancellationToken);
        await CheckGameplayDeadlineAsync(db, guard, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return deleted == 1;
    }
    public async Task<CharacterRow> UpdateForGameplayAsync(GameplayWriteAuthority authority, CharacterRow row, CancellationToken cancellationToken = default)
    {
        if (row.AccountId != authority.AccountId) throw new GameplayWriteRejectedException();
        await using CharacterDbContext db = await CreateContextAsync(cancellationToken);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        AccountGameplayFence guard = await HoldGameplayAsync(db, authority, cancellationToken);
        if (!await db.Characters.AnyAsync(c => c.Id == row.Id && c.AccountId == authority.AccountId, cancellationToken)) throw new GameplayWriteRejectedException();
        db.ValidatedGameplaySave = true;
        db.Update(row);
        await db.SaveChangesAsync(cancellationToken);
        await CheckGameplayDeadlineAsync(db, guard, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return row;
    }
    public async Task<CharacterRow?> FindForGameplayAsync(GameplayWriteAuthority authority, CharacterId id, CancellationToken cancellationToken = default)
    {
        await using CharacterDbContext db = await CreateContextAsync(cancellationToken);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        AccountGameplayFence guard = await HoldGameplayAsync(db, authority, cancellationToken);
        CharacterRow? row = await db.Characters.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id && c.AccountId == authority.AccountId, cancellationToken);
        await CheckGameplayDeadlineAsync(db, guard, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return row;
    }
    private static async Task<AccountGameplayFence> HoldGameplayAsync(CharacterDbContext db, GameplayWriteAuthority authority, CancellationToken cancellationToken)
    {
        if (!GameplayFenceRepository.Valid(authority)) throw new GameplayWriteRejectedException();
        AccountGameplayFence guard = await GameplayFenceRepository.LockAsync(db, authority.AccountId, cancellationToken);
        if (guard.GameSessionId != authority.GameSessionId || guard.FencingToken != authority.FencingToken ||
            guard.Mode != GameplayFenceMode.Active || guard.ConsolidationId is not null) throw new GameplayWriteRejectedException();
        await CheckGameplayDeadlineAsync(db, guard, cancellationToken);
        return guard;
    }
    private static async Task CheckGameplayDeadlineAsync(CharacterDbContext db, AccountGameplayFence guard, CancellationToken cancellationToken)
    {
        if (guard.LeaseUntil <= await GameplayFenceRepository.NowAsync(db, null, cancellationToken)) throw new GameplayWriteRejectedException();
    }
}
